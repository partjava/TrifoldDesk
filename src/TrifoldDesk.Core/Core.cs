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
    public string ChildFolderId { get; set; } = "";
    public string LaunchMode { get; set; } = "shell";
    public string ExecutablePath { get; set; } = "";
}
public sealed class ShortcutConfig { public int SchemaVersion { get; set; } = 2; public List<ShortcutItem> Items { get; set; } = []; }
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 2;
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
    public string SelectedGpuId { get; set; } = "";
    public bool DesktopEmbedded { get; set; }
    public bool SensorEnabled { get; set; }
}
public sealed record LoadResult<T>(T Value, string? Warning);
public sealed class ConfigManager(string directory)
{
    public string DirectoryPath { get; } = directory;
    private readonly object _gate = new();
    private readonly HashSet<string> _readOnlyFiles = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyList<string> ReadOnlyFiles { get { lock (_gate) return _readOnlyFiles.ToArray(); } }
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    public LoadResult<T> Load<T>(string file) where T : new()
    {
        lock (_gate)
        {
            var path = Path.Combine(DirectoryPath, file);
            if (!File.Exists(path)) return new(new T(), null);
            try { CheckSchema<T>(path); return new(Read<T>(path), null); }
            catch (FutureSchemaException ex) { _readOnlyFiles.Add(file); return new(new T(), ex.Message); }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                string preserved = path + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
                // A failed preservation must stop the load; silently overwriting would lose user data.
                File.Copy(path, preserved);
                try
                {
                    CheckSchema<T>(path + ".bak");
                    var recovered = Read<T>(path + ".bak");
                    return new(recovered, $"{file} 损坏，已保留原文件并恢复备份。原文件：{preserved}");
                }
                catch (FutureSchemaException futureBackup) { _readOnlyFiles.Add(file); return new(new T(), futureBackup.Message); }
                catch (Exception backupError) when (backupError is IOException or JsonException or UnauthorizedAccessException)
                { return new(new T(), $"{file} 无法读取，原文件已保留：{preserved}。本次使用默认配置。"); }
            }
        }
    }
    private static T Read<T>(string path)
    {
        var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path));
        if(node is System.Text.Json.Nodes.JsonObject obj && typeof(T).GetProperty("SchemaVersion") is { } property)
        {
            int supported=(int)(property.GetValue(Activator.CreateInstance<T>())??1);
            int prior=obj["SchemaVersion"]?.GetValue<int>()??1;
            if(prior<supported)obj["SchemaVersion"]=supported;
        }
        if(node is null)throw new JsonException("Configuration cannot be null.");
        var value=node.Deserialize<T>(Options);if(value is null)throw new JsonException("Configuration cannot be null.");return value;
    }
    internal static void CheckSchema<T>(string path)
    {
        if (!File.Exists(path)) return;
        string name=Path.GetFileName(path).Replace(".bak", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        if(name is "monitors.json" or "events.json")
        {
            try{ProfilePackage.ValidateConfig(name,File.ReadAllBytes(path));}
            catch(InvalidDataException ex){throw new JsonException(ex.Message,ex);}
        }
        var property = typeof(T).GetProperty("SchemaVersion");
        int supported = Path.GetFileName(path).Replace(".bak", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant() switch
        {
            "settings.json" => new AppSettings().SchemaVersion,
            "shortcuts.json" => new ShortcutConfig().SchemaVersion,
            "folders.json" => new FolderConfig().SchemaVersion,
            "widgets.json" => new WidgetConfig().SchemaVersion,
            _ => property == null ? 1 : (int)(property.GetValue(Activator.CreateInstance<T>()) ?? 1)
        };
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("SchemaVersion", out var version))
        {
            if (version.ValueKind != JsonValueKind.Number) throw new JsonException("配置版本必须为整数。");
            if (version.GetDouble() > supported)
                throw new FutureSchemaException($"{Path.GetFileName(path)} 使用不支持的未来配置版本 {version.GetRawText()}（支持 {supported}）；原文件保留，禁止保存覆盖。请使用更新版本。");
            if (!version.TryGetInt32(out int number) || number < 1) throw new JsonException("配置版本必须为正整数。");
        }
    }
    public void Save<T>(string file, T value)
    {
        lock (_gate)
        {
            if (_readOnlyFiles.Contains(file)) throw new FutureSchemaException($"{file} 使用未来配置版本，禁止保存覆盖。");
            // Check on disk even when this manager has not loaded the file.
            try { CheckSchema<T>(Path.Combine(DirectoryPath, file)); }
            catch (JsonException) { /* Existing corrupt-primary recovery still applies. */ }
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
