using System.Text.Json;

namespace TrifoldDesk.Core;

public sealed class MonitorLayoutConfig
{
    public int SchemaVersion { get; set; } = 1;
    public Dictionary<string, WidgetConfig> Layouts { get; set; } = [];
}
public static class MonitorLayoutRules
{
    public static WidgetConfig LoadActive(MonitorLayoutConfig config, string device, WidgetConfig fallback)
    {
        ArgumentNullException.ThrowIfNull(config); ArgumentNullException.ThrowIfNull(fallback);
        string? key = Key(config, device);
        return PluginRules.Clone(key != null && config.Layouts[key] != null ? config.Layouts[key] : fallback);
    }
    public static MonitorLayoutConfig SaveActive(MonitorLayoutConfig config, string device, WidgetConfig widgets)
    {
        ArgumentNullException.ThrowIfNull(config); ArgumentNullException.ThrowIfNull(widgets);
        if (string.IsNullOrWhiteSpace(device)) throw new ArgumentException("显示器标识不能为空。", nameof(device));
        var next = JsonSerializer.Deserialize<MonitorLayoutConfig>(JsonSerializer.Serialize(config))!; next.Layouts ??= [];
        next.Layouts[Key(next, device) ?? device] = PluginRules.Clone(widgets); return next;
    }
    public static IReadOnlyList<string> OfflineDevices(MonitorLayoutConfig config, IEnumerable<string> onlineDevices)
    {
        var online = onlineDevices.ToHashSet(StringComparer.OrdinalIgnoreCase);
        return (config.Layouts ?? []).Keys.Where(device => !online.Contains(device)).OrderBy(device => device, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    private static string? Key(MonitorLayoutConfig config, string device)
    {
        if (string.IsNullOrWhiteSpace(device) || config.Layouts == null) return null;
        if (config.Layouts.ContainsKey(device)) return device;
        return config.Layouts.Keys.FirstOrDefault(key => string.Equals(key, device, StringComparison.OrdinalIgnoreCase));
    }
}
