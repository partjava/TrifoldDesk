using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.PawnIo;
using System.Security.Principal;

namespace TrifoldDesk.Services;
/// <summary>One optional sensor backend shared by active summary components. No timer or driver installer.</summary>
public sealed class HardwareSensorService : IDisposable
{
    private static readonly object Gate = new(), BackendGate = new();
    private static Computer? _computer;
    private static Task<HardwareSensorSnapshot>? _sampling;
    private static HardwareSensorSnapshot _latest = HardwareSensorSnapshot.Unavailable("硬件传感器尚未采样");
    private static DateTime _nextSample;
    private static int _users, _generation;
    private bool _disposed;
    public HardwareSensorService() { lock (Gate) _users++; }
    public HardwareSensorSnapshot Read()
    {
        lock (Gate)
        {
            if (_disposed) return HardwareSensorSnapshot.Unavailable("硬件传感器组件已停止");
            _ = RefreshAsync();
            return _latest;
        }
    }
    public Task<HardwareSensorSnapshot> RefreshAsync()
    {
        lock (Gate)
        {
            if (_disposed) return Task.FromResult(HardwareSensorSnapshot.Unavailable("硬件传感器组件已停止"));
            if (_sampling is { IsCompleted: false }) return _sampling;
            if (DateTime.UtcNow < _nextSample) return Task.FromResult(_latest);
            int generation = _generation;
            _nextSample = DateTime.UtcNow.AddSeconds(5);
            _sampling = Task.Run(() =>
            {
                HardwareSensorSnapshot snapshot;
                lock (BackendGate) snapshot = Sample();
                lock (Gate)
                    if (_users > 0 && generation == _generation)
                    { _latest = snapshot; _nextSample = DateTime.UtcNow.AddSeconds(5); }
                return snapshot;
            });
            return _sampling;
        }
    }
    private static HardwareSensorSnapshot Sample()
    {
        var warnings = new List<string>();
        var values = new List<HardwareSensorValue>();
        try
        {
            bool pawnInstalled;
            try { pawnInstalled = PawnIo.IsInstalled; }
            catch (Exception ex) { pawnInstalled = false; warnings.Add("PawnIO状态无法读取：" + ex.Message); }
            bool administrator;
            using (var identity = WindowsIdentity.GetCurrent()) administrator = new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            bool lowLevel = pawnInstalled && administrator;
            string lowLevelReason = !pawnInstalled ? "PawnIO未安装，CPU/主板低层传感器不可用；程序不会安装驱动" : !administrator ? "当前进程无管理员权限，CPU/主板低层传感器不可用" : "";
            if (lowLevelReason.Length > 0) warnings.Add(lowLevelReason);
            _computer ??= OpenComputer(lowLevel);
            foreach (var hardware in _computer.Hardware) Collect(hardware, values, warnings, lowLevel, lowLevelReason);
            if (!values.Any(s => s.HardwareType == "Storage" && s.Kind == "Temperature" && s.Value.HasValue))
                warnings.Add("磁盘温度不可用：设备/驱动未提供读数或访问权限不足");
            foreach (var kind in new[] { "Temperature", "Fan" })
                if (!values.Any(s => s.Kind == kind && s.Value.HasValue)) warnings.Add(kind == "Fan" ? "设备/驱动没有提供可读风扇RPM" : "设备/驱动没有提供可读温度");
            return new(DateTime.UtcNow, values.ToArray(), warnings.Distinct().ToArray());
        }
        catch (Exception ex)
        {
            try { _computer?.Close(); } catch { }
            _computer = null;
            return new(DateTime.UtcNow, [], warnings.Append("硬件传感器后端不可用：" + ex.Message).Distinct().ToArray());
        }
    }
    private static Computer OpenComputer(bool lowLevel)
    {
        // CPU enumeration is also needed by the library's Intel GPU backend. Values
        // requiring low-level access are withheld unless PawnIO and permission are present.
        var computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsStorageEnabled = true, IsMotherboardEnabled = lowLevel };
        try { computer.Open(); return computer; }
        catch { try { computer.Close(); } catch { } throw; }
    }
    private static void Collect(IHardware hardware, List<HardwareSensorValue> values, List<string> warnings, bool lowLevel, string lowLevelReason)
    {
        string type = hardware.HardwareType.ToString();
        string? error = null;
        bool blocked = !lowLevel && type is "Cpu" or "Motherboard" or "SuperIO";
        if (blocked) error = lowLevelReason;
        else try { hardware.Update(); } catch (Exception ex) { error = "读取失败：" + ex.Message; warnings.Add(hardware.Name + "：" + error); }
        foreach (var sensor in hardware.Sensors)
        {
            string unit = sensor.SensorType switch { SensorType.Temperature => "°C", SensorType.Fan => "RPM", SensorType.SmallData => "MB", SensorType.Data => "GB", _ => "" };
            if (unit.Length == 0) continue;
            double? value = error == null && sensor.Value.HasValue && float.IsFinite(sensor.Value.Value) ? sensor.Value.Value : null;
            if (sensor.SensorType == SensorType.Fan && value < 0) value = null;
            values.Add(new(hardware.Identifier.ToString(), hardware.Name, type, sensor.Identifier.ToString(), sensor.Name,
                sensor.SensorType.ToString(), value, unit, value.HasValue ? "真实传感器读数" : error ?? "设备或驱动未提供此传感器读数"));
        }
        foreach (var sub in hardware.SubHardware) Collect(sub, values, warnings, lowLevel, lowLevelReason);
    }
    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed) return; _disposed = true;
            if (--_users != 0) return;
            _generation++; _nextSample = default;
            _latest = HardwareSensorSnapshot.Unavailable("硬件传感器已暂停");
        }
        // A slow storage sensor must never hold the UI thread while stopping.
        _ = Task.Run(() =>
        {
            lock (BackendGate)
            {
                lock (Gate) if (_users != 0) return;
                try { _computer?.Close(); } catch (Exception ex) { App.Log(ex); }
                _computer = null;
            }
        });
    }
}
