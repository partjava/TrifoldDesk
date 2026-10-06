using System.Windows.Threading;

namespace TrifoldDesk.Services;
public sealed record DriveSnapshot(string Name, string Label, string Kind, long? TotalBytes, long? FreeBytes, string Status)
{
    public string Title => string.IsNullOrEmpty(Label) ? Name : Name + " · " + Label;
    public string CapacityText => TotalBytes is long total && FreeBytes is long free ? $"{free / 1073741824d:0.0} GB 可用 / {total / 1073741824d:0.0} GB" : Status;
    public double UsedPercent => TotalBytes is > 0 && FreeBytes is long free ? Math.Clamp(100d * (TotalBytes.Value - free) / TotalBytes.Value, 0, 100) : 0;
    public bool LowSpace => TotalBytes is > 0 && UsedPercent > 90;
    public bool CanOpen => TotalBytes != null;
}
public sealed class DiskMonitorService : IDisposable
{
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, Task<DriveSnapshot>> _pending = new(StringComparer.OrdinalIgnoreCase);
    private bool _busy, _disposed;
    public event Action<IReadOnlyList<DriveSnapshot>>? Updated;
    public DiskMonitorService(Dispatcher dispatcher)
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background, dispatcher) { Interval = TimeSpan.FromSeconds(30) };
        _timer.Tick += Tick;
    }
    public bool IsEnabled => _timer.IsEnabled;
    public void SetEnabled(bool enabled)
    { if (_disposed || enabled == IsEnabled) return; if (enabled) { _timer.Start(); _ = RefreshAsync(); } else _timer.Stop(); }
    private async void Tick(object? sender, EventArgs e) => await RefreshAsync();
    private async Task RefreshAsync()
    {
        if (_busy || _disposed || !IsEnabled) return; _busy = true;
        try
        {
            var drives = DriveInfo.GetDrives();
            var work = drives.Select(async drive =>
            {
                // Reuse a timed-out pending read so unreachable network drives cannot grow thread usage forever.
                if (!_pending.TryGetValue(drive.Name, out var read)) _pending[drive.Name] = read = Task.Run(() => Read(drive));
                var finished = await Task.WhenAny(read, Task.Delay(900));
                if (finished == read) { _pending.Remove(drive.Name); return await read; }
                return new DriveSnapshot(drive.Name, "", Kind(drive.DriveType), null, null, "暂时无法读取 / 设备离线");
            }).ToArray();
            var snapshots = await Task.WhenAll(work);
            if (!_disposed && IsEnabled) Updated?.Invoke(snapshots.OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToArray());
        }
        catch (Exception ex) { App.Log(ex); }
        finally { _busy = false; }
    }
    private static DriveSnapshot Read(DriveInfo drive)
    {
        try
        {
            if (!drive.IsReady) return new(drive.Name, "", Kind(drive.DriveType), null, null, "未就绪 / 未插入介质");
            return new(drive.Name, drive.VolumeLabel, Kind(drive.DriveType), drive.TotalSize, drive.AvailableFreeSpace, "可用");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return new(drive.Name, "", Kind(drive.DriveType), null, null, "不可访问"); }
    }
    private static string Kind(DriveType type) => type switch { DriveType.Fixed => "本地磁盘", DriveType.Network => "网络磁盘", DriveType.Removable => "可移动磁盘", DriveType.CDRom => "光驱", DriveType.Ram => "内存盘", _ => "磁盘" };
    public void Dispose() { _disposed = true; _timer.Stop(); _timer.Tick -= Tick; }
}
