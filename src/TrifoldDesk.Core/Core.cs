using System.Text.Json;
namespace TrifoldDesk.Core;

public sealed class ShortcutItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string WorkingDirectory { get; set; } = "";
    public string IconPath { get; set; } = "";
    public int IconIndex { get; set; }
    public int ScreenIndex { get; set; }
    public int Order { get; set; }
    public string Group { get; set; } = "未分组";
}
public sealed class ShortcutConfig { public int SchemaVersion { get; set; } = 1; public List<ShortcutItem> Items { get; set; } = []; }
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public double GlassOpacity { get; set; } = 0.14;
    public bool IsCollapsed { get; set; }
    public bool HoverExpand { get; set; }
    public bool AutoCollapse { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool AnimationsEnabled { get; set; } = true;
    public bool TransparentIdle { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public string MonitorDevice { get; set; } = "";
    public string NetworkInterfaceId { get; set; } = "";
}
public sealed record LoadResult<T>(T Value, string? Warning);
public sealed class ConfigManager(string directory)
{
    public string DirectoryPath { get; } = directory;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public LoadResult<T> Load<T>(string file) where T : new()
    {
        lock (_gate)
        {
            var path = Path.Combine(DirectoryPath, file);
            if (!File.Exists(path)) return new(new T(), null);
            try { return new(Read<T>(path), null); }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                string preserved = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
                // A failed preservation must stop the load; silently overwriting would lose user data.
                File.Copy(path, preserved);
                try
                {
                    var recovered = Read<T>(path + ".bak");
                    return new(recovered, $"{file} 损坏，已保留原文件并恢复备份。原文件：{preserved}");
                }
                catch (Exception backupError) when (backupError is IOException or JsonException or UnauthorizedAccessException)
                { return new(new T(), $"{file} 无法读取，原文件已保留：{preserved}。本次使用默认配置。"); }
            }
        }
    }
    private static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options)
        ?? throw new JsonException("Configuration cannot be null.");
    public void Save<T>(string file, T value)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(DirectoryPath);
            var path = Path.Combine(DirectoryPath, file);
            var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                { JsonSerializer.Serialize(stream, value, Options); stream.Flush(true); }
                if (File.Exists(path))
                {
                    // Do not replace a good backup with a corrupt primary recovered during Load.
                    bool valid;
                    try { using var parsed = JsonDocument.Parse(File.ReadAllText(path)); valid = parsed.RootElement.ValueKind != JsonValueKind.Null; }
                    catch (JsonException) { valid = false; }
                    File.Replace(temp, path, valid ? path + ".bak" : null);
                }
                else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
    }
}
public static class ShortcutRules
{
    public static bool IsDuplicate(IEnumerable<ShortcutItem> items, string path, int screen) =>
        items.Any(item => item.ScreenIndex == screen && string.Equals(item.SourcePath, path, StringComparison.OrdinalIgnoreCase));
}
public static class RateMath
{
    public static double BytesPerSecond(long current, long previous, double seconds) =>
        seconds <= 0 || current < previous ? 0 : (current - previous) / seconds;
}
public static class LibraryRules
{
    public static bool Matches(ShortcutItem item, string query, string group)
    {
        bool inGroup = string.IsNullOrWhiteSpace(group) || group == "全部分组" || string.Equals(item.Group, group, StringComparison.OrdinalIgnoreCase);
        query = query.Trim();
        return inGroup && (query.Length == 0 || new[] { item.Name, item.SourcePath, item.Group }.Any(s => (s ?? "").Contains(query, StringComparison.OrdinalIgnoreCase)));
    }
    public static List<ShortcutItem> SortByName(IEnumerable<ShortcutItem> items, bool descending) =>
        (descending ? items.OrderByDescending(i => i.Name, StringComparer.CurrentCultureIgnoreCase) : items.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase)).ToList();
    public static bool CanMove(ShortcutItem item, IEnumerable<ShortcutItem> items, int targetScreen) => targetScreen is 0 or 1 &&
        !items.Any(other => other.Id != item.Id && other.ScreenIndex == targetScreen && string.Equals(other.SourcePath, item.SourcePath, StringComparison.OrdinalIgnoreCase));
}
public sealed record WorkspaceSize(double Width, double Height);
public static class WorkspaceLayout
{
    public static WorkspaceSize Expanded(double physicalWidth, double physicalHeight, double dpi)
    {
        double scale = Math.Max(96, dpi) / 96d;
        return new(Math.Max(1, (physicalWidth - 24) / scale), Math.Max(1, physicalHeight / scale));
    }
}
