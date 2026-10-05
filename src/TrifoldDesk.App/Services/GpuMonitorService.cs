using System.Diagnostics;
namespace TrifoldDesk.Services;
public sealed record GpuReading(double? Value, string Status);
public sealed class GpuMonitorService : IDisposable
{
    private readonly Dictionary<string, PerformanceCounter> _counters = [];
    private DateTime _refreshAt;
    private bool _disposed;
    private readonly object _gate = new();
    public GpuReading Read()
    {
        lock (_gate)
        {
            if (_disposed) return new(null, "组件已停止");
            try
            {
                var newNames = new HashSet<string>();
                if (DateTime.UtcNow >= _refreshAt)
                {
                    var names = new PerformanceCounterCategory("GPU Engine").GetInstanceNames().Where(n => n.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase)).ToHashSet();
                    foreach (var old in _counters.Keys.Where(n => !names.Contains(n)).ToArray()) { _counters[old].Dispose(); _counters.Remove(old); }
                    foreach (var name in names.Where(n => !_counters.ContainsKey(n)))
                    { var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", name, true); try { counter.NextValue(); _counters[name] = counter; newNames.Add(name); } catch { counter.Dispose(); throw; } }
                    _refreshAt = DateTime.UtcNow.AddSeconds(30);
                }
                var engines = new Dictionary<string, double>();
                foreach (var (name, counter) in _counters)
                {
                    if (newNames.Contains(name)) continue;
                    int start = name.IndexOf("luid_", StringComparison.OrdinalIgnoreCase); if (start < 0) continue;
                    string engine = name[start..]; double value = counter.NextValue(); if (!double.IsFinite(value)) continue;
                    engines[engine] = engines.GetValueOrDefault(engine) + Math.Max(0, value);
                }
                return engines.Count == 0 ? new(null, newNames.Count > 0 ? "正在建立采样基线" : "没有可读的3D引擎") : new(Math.Clamp(engines.Values.Max(), 0, 100), "最忙的3D引擎 · 每3秒采样");
            }
            catch (Exception) { _refreshAt = DateTime.UtcNow.AddSeconds(30); foreach (var counter in _counters.Values) counter.Dispose(); _counters.Clear(); return new(null, "GPU计数器不可用 · 驱动或权限限制"); }
        }
    }
    public void Dispose() { lock (_gate) { _disposed = true; foreach (var counter in _counters.Values) counter.Dispose(); _counters.Clear(); } }
}
