using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace TrifoldDesk.Services;
public sealed record NetworkChoice(string Id, string Name);
public sealed record MonitorSnapshot(double? Cpu, double? MemoryPercent, string MemoryText, string Download, string Upload, double? DiskFreeGb, double? DiskUsedPercent, string DiskName, string NetworkName, double? DownloadRate = null);
public sealed class SystemMonitorService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private PerformanceCounter? _cpu;
    private bool _cpuAttempted, _sampling, _disposed;
    private string _lastNetworkId = "";
    private int _resetBaseline;
    private long _received, _sent, _timestamp;
    private ulong _cpuTotal, _cpuIdle;
    private bool _systemTimePrimed;
    public string SelectedNetworkId { get; set; } = "";
    public bool CpuEnabled { get; set; } = true;
    public bool MemoryEnabled { get; set; } = true;
    public bool NetworkEnabled { get; set; } = true;
    public event Action<MonitorSnapshot>? Updated;
    public SystemMonitorService(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += Tick;
    }
    public bool IsEnabled => _timer.IsEnabled;
    public void SetEnabled(bool enabled)
    {
        if (_disposed || enabled == IsEnabled) return;
        if (enabled) { Interlocked.Exchange(ref _resetBaseline, 1); _timer.Start(); }
        else { _timer.Stop(); if (!_sampling) ReleaseCpu(); }
    }
    public void SetCollapsed(bool collapsed)
    { _timer.Interval = TimeSpan.FromSeconds(collapsed ? 5 : 1); Interlocked.Exchange(ref _resetBaseline, 1); }
    private async void Tick(object? sender, EventArgs e)
    {
        if (_sampling || _disposed || !IsEnabled) return;
        _sampling = true;
        string selected = SelectedNetworkId;
        bool cpuEnabled = CpuEnabled, memoryEnabled = MemoryEnabled, networkEnabled = NetworkEnabled;
        try
        {
            var snapshot = await Task.Run(() => Sample(selected, cpuEnabled, memoryEnabled, networkEnabled));
            if (!_disposed && IsEnabled) Updated?.Invoke(snapshot);
        }
        catch (Exception ex) { App.Log(ex); }
        finally { _sampling = false; if (_disposed || !IsEnabled) ReleaseCpu(); }
    }
    private MonitorSnapshot Sample(string selected, bool cpuEnabled, bool memoryEnabled, bool networkEnabled)
    {
        if (Interlocked.Exchange(ref _resetBaseline, 0) == 1) { _lastNetworkId = ""; _systemTimePrimed = false; }
        double? cpu = null;
        if (cpuEnabled)
        {
        try
        {
            if (!_cpuAttempted) { _cpuAttempted = true; _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total", true); _cpu.NextValue(); }
            else if (_cpu != null) cpu = Math.Clamp(_cpu.NextValue(), 0, 100);
        }
        catch (Exception ex) { App.Log(ex); _cpu?.Dispose(); _cpu = null; }
        // Some ordinary-user environments block the performance registry. Use real OS counters as fallback.
        if (_cpu == null && GetSystemTimes(out ulong idle, out ulong kernel, out ulong user))
        {
            ulong total = kernel + user;
            if (_systemTimePrimed && total > _cpuTotal && idle >= _cpuIdle)
                cpu = Math.Clamp(100d * (1 - (idle - _cpuIdle) / (double)(total - _cpuTotal)), 0, 100);
            _cpuTotal = total; _cpuIdle = idle; _systemTimePrimed = true;
        }
        }
        else { ReleaseCpu(); _systemTimePrimed = false; }
        var memory = new MEMORYSTATUSEX { Length = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        bool hasMemory = memoryEnabled && GlobalMemoryStatusEx(ref memory) && memory.TotalPhysical > 0;
        double? memoryPercent = hasMemory ? 100d * (memory.TotalPhysical - memory.AvailablePhysical) / memory.TotalPhysical : null;
        string memoryText = hasMemory ? $"{(memory.TotalPhysical - memory.AvailablePhysical) / 1073741824d:0.0} / {memory.TotalPhysical / 1073741824d:0.0} GB" : "暂不可用";
        double? free = null, diskPercent = null; string diskName = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        // Drive I/O is isolated in DiskMonitorService so network/removable drives cannot block CPU/network updates.
        string down = "—", up = "—", networkName = "无连接"; double? downloadRate = null;
        if (networkEnabled) try
        {
            var interfaces = ActiveInterfaces();
            var nic = string.IsNullOrEmpty(selected)
                ? interfaces.FirstOrDefault(n => n.GetIPProperties().GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any) && !g.Address.Equals(System.Net.IPAddress.IPv6Any))) ?? interfaces.FirstOrDefault()
                : interfaces.FirstOrDefault(n => n.Id == selected);
            if (nic != null)
            {
                var bytes = nic.GetIPStatistics(); long now = Stopwatch.GetTimestamp();
                if (_lastNetworkId == nic.Id && _timestamp > 0)
                {
                    double elapsed = (now - _timestamp) / (double)Stopwatch.Frequency;
                    downloadRate = RateMath.BytesPerSecond(bytes.BytesReceived, _received, elapsed); down = FormatRate(downloadRate.Value);
                    up = FormatRate(RateMath.BytesPerSecond(bytes.BytesSent, _sent, elapsed));
                }
                _lastNetworkId = nic.Id; _received = bytes.BytesReceived; _sent = bytes.BytesSent; _timestamp = now; networkName = nic.Name;
            }
            else { _lastNetworkId = ""; networkName = string.IsNullOrEmpty(selected) ? "无连接" : "所选网卡离线"; }
        }
        catch (NetworkInformationException ex) { App.Log(ex); _lastNetworkId = ""; }
        if (!networkEnabled) _lastNetworkId = "";
        return new(cpu, memoryPercent, memoryText, down, up, free, diskPercent, diskName, networkName, downloadRate);
    }
    private static NetworkInterface[] ActiveInterfaces() => NetworkInterface.GetAllNetworkInterfaces().Where(n =>
        n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel).ToArray();
    public static List<NetworkChoice> GetNetworkChoices()
    {
        try { return NetworkInterface.GetAllNetworkInterfaces().Where(n => n.NetworkInterfaceType is not NetworkInterfaceType.Loopback and not NetworkInterfaceType.Tunnel).Select(n => new NetworkChoice(n.Id, n.Name)).ToList(); }
        catch (NetworkInformationException) { return []; }
    }
    private static string FormatRate(double bytes) => bytes >= 1048576 ? $"{bytes / 1048576:0.0} MB/s" : $"{bytes / 1024:0.0} KB/s";
    private void ReleaseCpu() { _cpu?.Dispose(); _cpu = null; _cpuAttempted = false; }
    public void Dispose() { _disposed = true; _timer.Stop(); _timer.Tick -= Tick; if (!_sampling) ReleaseCpu(); }
    [StructLayout(LayoutKind.Sequential)] private struct MEMORYSTATUSEX
    {
        public uint Length, MemoryLoad; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
}
