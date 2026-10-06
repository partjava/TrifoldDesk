using System.Text.RegularExpressions;
namespace TrifoldDesk.Core;
public sealed record GpuAdapterChoice(string Id, string Name, string CounterId, ulong? DedicatedMemoryBytes);
public sealed record HardwareSensorValue(string HardwareId, string HardwareName, string HardwareType, string SensorId,
    string Name, string Kind, double? Value, string Unit, string Status);
public sealed record HardwareSensorSnapshot(DateTime SampledUtc, IReadOnlyList<HardwareSensorValue> Sensors, IReadOnlyList<string> Warnings)
{
    public static HardwareSensorSnapshot Unavailable(string warning) => new(DateTime.MinValue, [], [warning]);
}
public static partial class GpuCounterRules
{
    [GeneratedRegex(@"(?:^|_)(luid_0x[0-9a-f]{8}_0x[0-9a-f]{8})(?:_|$)", RegexOptions.IgnoreCase)]
    private static partial Regex LuidPattern();
    [GeneratedRegex(@"luid_0x[0-9a-f]{8}_0x[0-9a-f]{8}_phys_\d+_eng_\d+_engtype_3D$", RegexOptions.IgnoreCase)]
    private static partial Regex EnginePattern();
    public static string? GetAdapterLuid(string instance)
    {
        var match=LuidPattern().Match(instance);return match.Success?match.Groups[1].Value.ToLowerInvariant():null;
    }
    public static double? AggregateEngineUtilization(IEnumerable<KeyValuePair<string,double>> samples,string adapterLuid)
    {
        var engines=new Dictionary<string,double>(StringComparer.OrdinalIgnoreCase);
        foreach(var sample in samples)
        {
            if(!double.IsFinite(sample.Value)||!string.Equals(GetAdapterLuid(sample.Key),adapterLuid,StringComparison.OrdinalIgnoreCase))continue;
            var match=EnginePattern().Match(sample.Key);if(!match.Success)continue;
            engines[match.Value]=engines.GetValueOrDefault(match.Value)+Math.Max(0,sample.Value);
        }
        return engines.Count==0?null:Math.Clamp(engines.Values.Max(),0,100);
    }
    public static double? AggregateDedicatedBytes(IEnumerable<KeyValuePair<string,double>> samples,string adapterLuid)
    {
        var values=samples.Where(s=>string.Equals(GetAdapterLuid(s.Key),adapterLuid,StringComparison.OrdinalIgnoreCase)&&double.IsFinite(s.Value)&&s.Value>=0).Select(s=>s.Value).ToArray();
        return values.Length==0?null:values.Sum();
    }
}
