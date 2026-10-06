using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TrifoldDesk.Services;
public sealed record GpuReading(double? Value, string Status, string AdapterId = "", string AdapterName = "",
    double? DedicatedMemoryUsedBytes = null, ulong? DedicatedMemoryTotalBytes = null);
public sealed class GpuMonitorService : IDisposable
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, PerformanceCounter> Engines = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, PerformanceCounter> Memory = new(StringComparer.OrdinalIgnoreCase);
    private static IReadOnlyList<GpuAdapterChoice> _adapters = [];
    private static string _adapterWarning = "";
    private static string _engineWarning = "", _memoryWarning = "";
    private static DateTime _adaptersUntil, _refreshAt, _cacheUntil;
    private static readonly Dictionary<string, GpuReading> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static int _users;
    private bool _disposed;
    public GpuMonitorService() { lock (Gate) _users++; }
    public static IReadOnlyList<GpuAdapterChoice> Adapters()
    {
        lock (Gate) { RefreshAdapters(); return _adapters.ToArray(); }
    }
    public GpuReading Read() => Read("");
    public GpuReading Read(string selectedId)
    {
        lock (Gate)
        {
            if (_disposed) return new(null, "组件已停止");
            RefreshAdapters();
            bool automatic = string.IsNullOrWhiteSpace(selectedId);
            var adapter = automatic ? _adapters.FirstOrDefault() : _adapters.FirstOrDefault(a => a.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
            if (adapter == null) return new(null, string.IsNullOrWhiteSpace(selectedId) ? "没有可读的GPU适配器 · " + _adapterWarning : "所选GPU离线或已更换，请重新选择", selectedId);
            if (DateTime.UtcNow >= _cacheUntil) SampleAll();
            if (automatic && Cache.Count > 0) return Cache.Values.OrderByDescending(r => r.Value ?? -1).First();
            return Cache.TryGetValue(adapter.Id, out var reading) ? reading : new(null, "GPU采样暂不可用", adapter.Id, adapter.Name, null, adapter.DedicatedMemoryBytes);
        }
    }
    private static void SampleAll()
    {
        var newEngines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string engineWarning = _engineWarning, memoryWarning = _memoryWarning;
        if (DateTime.UtcNow >= _refreshAt)
        {
            try { RefreshCounters(Engines, "GPU Engine", "Utilization Percentage", n => n.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase), true, newEngines); engineWarning = ""; }
            catch (Exception ex) { Release(Engines); engineWarning = "3D计数器不可用：" + ex.Message; }
            try { RefreshCounters(Memory, "GPU Adapter Memory", "Dedicated Usage", _ => true, false, null); memoryWarning = ""; }
            catch (Exception ex) { Release(Memory); memoryWarning = "显存计数器不可用：" + ex.Message; }
            _refreshAt = DateTime.UtcNow.AddSeconds(30);
        }
        var engines = SampleCounters(Engines, newEngines, true, ref engineWarning);
        var memory = SampleCounters(Memory, null, false, ref memoryWarning);
        _engineWarning = engineWarning; _memoryWarning = memoryWarning;
        Cache.Clear();
        foreach (var adapter in _adapters)
        {
            double? value = GpuCounterRules.AggregateEngineUtilization(engines, adapter.CounterId);
            double? used = GpuCounterRules.AggregateDedicatedBytes(memory, adapter.CounterId);
            string status = value.HasValue ? "所选GPU最忙3D引擎 · 每5秒" : newEngines.Any(n => GpuCounterRules.GetAdapterLuid(n) == adapter.CounterId) ? "正在建立3D采样基线" : engineWarning.Length > 0 ? engineWarning : "所选GPU没有可读的3D引擎（驱动可能未提供）";
            if (!used.HasValue) status += " · " + (memoryWarning.Length > 0 ? memoryWarning : "显存用量未提供");
            Cache[adapter.Id] = new(value, status, adapter.Id, adapter.Name, used, adapter.DedicatedMemoryBytes);
        }
        _cacheUntil = DateTime.UtcNow.AddSeconds(5);
    }
    private static void RefreshCounters(Dictionary<string, PerformanceCounter> counters, string category, string counterName, Func<string, bool> filter, bool prime, HashSet<string>? added)
    {
        var names = new PerformanceCounterCategory(category).GetInstanceNames().Where(filter).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var old in counters.Keys.Where(n => !names.Contains(n)).ToArray()) { counters[old].Dispose(); counters.Remove(old); }
        foreach (var name in names.Where(n => !counters.ContainsKey(n)))
        {
            var counter = new PerformanceCounter(category, counterName, name, true);
            try { if (prime) counter.NextValue(); counters[name] = counter; added?.Add(name); }
            catch { counter.Dispose(); throw; }
        }
    }
    private static Dictionary<string, double> SampleCounters(Dictionary<string, PerformanceCounter> counters, HashSet<string>? skip, bool rate, ref string warning)
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in counters.ToArray())
        {
            if (skip?.Contains(entry.Key) == true) continue;
            try { double value = rate ? entry.Value.NextValue() : entry.Value.RawValue; if (double.IsFinite(value) && value >= 0) values[entry.Key] = value; }
            catch (Exception ex) { warning = "GPU计数器读取失败：" + ex.Message; entry.Value.Dispose(); counters.Remove(entry.Key); }
        }
        return values;
    }
    private static void RefreshAdapters()
    {
        if (DateTime.UtcNow < _adaptersUntil) return;
        try { _adapters = EnumerateAdapters(); _adapterWarning = ""; }
        catch (Exception ex) { _adapters = []; _adapterWarning = ex.Message; }
        _adaptersUntil = DateTime.UtcNow.AddSeconds(30); _cacheUntil = default;
    }
    private static IReadOnlyList<GpuAdapterChoice> EnumerateAdapters()
    {
        var choices = new List<GpuAdapterChoice>();
        var occurrences = new Dictionary<string, int>();
        Guid iid = new("770aae78-f26f-4dba-a829-253c83d1b387");
        Marshal.ThrowExceptionForHR(CreateDXGIFactory1(ref iid, out var factory));
        try
        {
            var enumerate = Method<EnumAdapter>(factory, 12);
            for (uint index = 0; index < 64; index++)
            {
                int hr = enumerate(factory, index, out var adapter);
                if ((uint)hr == 0x887A0002) break; // DXGI_ERROR_NOT_FOUND
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    Marshal.ThrowExceptionForHR(Method<GetDescription>(adapter, 10)(adapter, out var desc));
                    if ((desc.Flags & 2) != 0) continue; // Software renderers are not hardware GPUs.
                    string hardware = $"dxgi:{desc.VendorId:X4}:{desc.DeviceId:X4}:{desc.SubSysId:X8}:{desc.Revision:X8}";
                    int occurrence = occurrences.GetValueOrDefault(hardware); occurrences[hardware] = occurrence + 1;
                    string luid = $"luid_0x{unchecked((uint)desc.Luid.High):x8}_0x{desc.Luid.Low:x8}";
                    ulong bytes = desc.DedicatedVideoMemory.ToUInt64();
                    choices.Add(new(hardware + ":" + occurrence, desc.Description.Trim(), luid, bytes > 0 ? bytes : null));
                }
                finally { Marshal.Release(adapter); }
            }
        }
        finally { Marshal.Release(factory); }
        return choices;
    }
    private static T Method<T>(IntPtr obj, int index) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(Marshal.ReadIntPtr(Marshal.ReadIntPtr(obj), index * IntPtr.Size));
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int EnumAdapter(IntPtr factory, uint index, out IntPtr adapter);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int GetDescription(IntPtr adapter, out AdapterDescription description);
    [StructLayout(LayoutKind.Sequential)] private struct AdapterLuid { public uint Low; public int High; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct AdapterDescription
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint VendorId, DeviceId, SubSysId, Revision;
        public UIntPtr DedicatedVideoMemory, DedicatedSystemMemory, SharedSystemMemory;
        public AdapterLuid Luid;
        public uint Flags;
    }
    [DllImport("dxgi.dll", ExactSpelling = true)] private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
    private static void Release(Dictionary<string, PerformanceCounter> counters) { foreach (var counter in counters.Values) counter.Dispose(); counters.Clear(); }
    public void Dispose()
    {
        lock (Gate)
        {
            if (_disposed) return; _disposed = true;
            if (--_users == 0) { Release(Engines); Release(Memory); Cache.Clear(); _cacheUntil = default; _refreshAt = default; _engineWarning = _memoryWarning = ""; }
        }
    }
}
