using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TrifoldDesk.Core;

public sealed class FutureSchemaException(string message) : IOException(message);
public sealed record ProfilePackageReport(IReadOnlyList<string> Files, IReadOnlyList<string> MissingExternalPaths,
    IReadOnlyList<string> Warnings, string? BackupDirectory = null);
public sealed class ProfilePackageManifest
{
    public int PackageVersion { get; set; } = 1;
    public string Application { get; set; } = "TrifoldDesk";
    public string CreatedUtc { get; set; } = DateTime.UtcNow.ToString("O");
    public List<ProfilePackageFile> Files { get; set; } = [];
}
public sealed record ProfilePackageFile(string Path, long Length, string Sha256);

/// <summary>Portable profile packages. Import never executes shortcuts or plugin data.</summary>
public static class ProfilePackage
{
    public const int PackageVersion = 1;
    public const long MaxEntryBytes = 32 * 1024 * 1024;
    public const long MaxPackageBytes = 256 * 1024 * 1024;
    public const int MaxEntries = 4096;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly string[] Required = ["settings.json", "shortcuts.json", "widgets.json", "folders.json"];
    private static readonly string[] GeneratedRoots = ["app-links", "icons", "assets", "resources"];
    private static readonly string[] AssetRoots = ["app-links", "icons", "assets", "resources", "plugins", "plugin-data"];
    private static readonly HashSet<string> PathProperties = new(StringComparer.Ordinal)
        { "SourcePath", "TargetPath", "IconPath", "WorkingDirectory", "ExecutablePath", "DirectoryPath" };

    public static void Export(string profileDirectory, string archivePath)
    {
        var root = Path.GetFullPath(profileDirectory);
        var output = Path.GetFullPath(archivePath);
        if (IsWithin(output, root)) throw new InvalidOperationException("配置包必须保存到配置目录以外。");
        var files = new SortedDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Required)
        {
            var path = Path.Combine(root, name);
            if (File.Exists(path))
            {
                var original = ReadBounded(path);
                try { ValidateConfig(name, original); }
                catch (FutureSchemaException)
                {
                    // A newer application's data is an opaque raw backup, never rewritten.
                    AddFile(files, name, original);
                    continue;
                }
            }
            var node = File.Exists(path) ? JsonNode.Parse(File.ReadAllText(path)) : DefaultConfig(name);
            if (node is not JsonObject) throw new InvalidDataException($"配置 {name} 不是对象。");
            RewritePaths(node, value => ToPortable(value, root));
            files.Add(name, System.Text.Encoding.UTF8.GetBytes(node.ToJsonString(Json)));
        }
        // Preserve extra root JSON (including pane state) without stripping unknown properties.
        if (Directory.Exists(root))
            foreach (var path in Directory.EnumerateFiles(root, "*.json"))
            {
                var name = Path.GetFileName(path);
                if (files.ContainsKey(name) || name == ConfigTransaction.PendingFileName || name.Equals("plugin-approvals.json",StringComparison.OrdinalIgnoreCase)) continue;
                var original=ReadBounded(path);
                try{ValidateConfig(name,original);}catch(FutureSchemaException){AddFile(files,name,original);continue;}
                var node = JsonNode.Parse(File.ReadAllText(path)) ?? throw new InvalidDataException($"配置 {name} 为空。");
                RewritePaths(node, value => ToPortable(value, root));
                AddFile(files, name, System.Text.Encoding.UTF8.GetBytes(node.ToJsonString(Json)));
            }
        foreach (var asset in AssetRoots)
        {
            var directory = Path.Combine(root, asset);
            if (!Directory.Exists(directory)) continue;
            foreach (var path in EnumerateSafe(directory))
                AddFile(files, Path.GetRelativePath(root, path).Replace('\\', '/'), ReadBounded(path));
        }
        if (files.Count >= MaxEntries || files.Sum(p => (long)p.Value.Length) > MaxPackageBytes)
            throw new InvalidDataException("配置包超过大小或文件数量限制。");
        var manifest = new ProfilePackageManifest { Files = files.Select(p => new ProfilePackageFile(p.Key, p.Value.LongLength, Hash(p.Value))).ToList() };
        var temp = output + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew))
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                Write(archive, "manifest.json", JsonSerializer.SerializeToUtf8Bytes(manifest, Json));
                foreach (var file in files) Write(archive, file.Key, file.Value);
            }
            File.Move(temp, output, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    public static ProfilePackageReport Preview(string archivePath)
    {
        var files = ReadPackage(archivePath);
        return Report(files);
    }

    public static ProfilePackageReport Import(string archivePath, string profileDirectory)
    {
        // Validation and extraction finish before any existing profile is changed.
        var files = ReadPackage(archivePath);
        var report = Report(files);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(profileDirectory));
        var parent = Path.GetDirectoryName(root) ?? throw new InvalidOperationException("不能恢复到磁盘根目录。");
        ValidateCurrentProfile(root);
        Directory.CreateDirectory(parent);
        var stage = root + ".import-" + Guid.NewGuid().ToString("N");
        var backup = root + ".backup-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff");
        bool backedUp = false;
        try
        {
            Directory.CreateDirectory(stage);
            foreach (var file in files)
            {
                // Trust belongs to this installation, never to a transferable archive.
                if(file.Key.Equals("plugin-approvals.json",StringComparison.OrdinalIgnoreCase)) continue;
                var bytes = file.Value;
                if (file.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase) && !file.Key.Contains('/'))
                {
                    var node = JsonNode.Parse(bytes)!;
                    RewritePaths(node, value => FromPortable(value, root, files));
                    bytes = JsonSerializer.SerializeToUtf8Bytes(node, Json);
                }
                var path = Path.Combine(stage, file.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
            // The backup is the entire original directory, including arbitrary plugin files.
            // Same-parent renames also prevent a half-written active profile.
            ValidateCurrentProfile(root);
            if (Directory.Exists(root)) { Directory.Move(root, backup); backedUp = true; }
            try { Directory.Move(stage, root); }
            catch
            {
                if (backedUp) Directory.Move(backup, root);
                throw;
            }
            return report with { BackupDirectory = backedUp ? backup : null };
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    private static Dictionary<string, byte[]> ReadPackage(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaxEntries) throw new InvalidDataException("配置包文件过多。");
        var all = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var entry in archive.Entries)
        {
            ValidateName(entry.FullName);
            // Reject symlinks and directory entries instead of following archive metadata.
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("配置包不支持符号链接。");
            if (entry.Length > MaxEntryBytes || (total += entry.Length) > MaxPackageBytes) throw new InvalidDataException("配置包超过大小限制。");
            using var input = entry.Open();
            using var output = new MemoryStream();
            var buffer = new byte[81920]; int count; long actual = 0;
            while ((count = input.Read(buffer)) != 0)
            {
                actual += count;
                if (actual > MaxEntryBytes || actual > entry.Length) throw new InvalidDataException("配置包实际解压大小异常。");
                output.Write(buffer, 0, count);
            }
            if (!all.TryAdd(entry.FullName, output.ToArray())) throw new InvalidDataException("配置包包含重复路径。");
        }
        if (!all.Remove("manifest.json", out var metadata)) throw new InvalidDataException("配置包缺少 manifest.json。");
        ProfilePackageManifest manifest;
        try { manifest = JsonSerializer.Deserialize<ProfilePackageManifest>(metadata) ?? throw new JsonException(); }
        catch (JsonException ex) { throw new InvalidDataException("配置包清单无效。", ex); }
        if (manifest.PackageVersion != PackageVersion || manifest.Application != "TrifoldDesk") throw new InvalidDataException("不支持此配置包版本或来源。");
        if (manifest.Files == null || manifest.Files.Count != all.Count) throw new InvalidDataException("配置包清单文件数量不匹配。");
        var declared = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            if (file == null) throw new InvalidDataException("配置包清单条目为空。");
            ValidateName(file.Path);
            if (!declared.Add(file.Path) || !all.TryGetValue(file.Path, out var bytes) || bytes.LongLength != file.Length || Hash(bytes) != file.Sha256)
                throw new InvalidDataException("配置包清单或文件完整性检查失败。");
            if (!AllowedFile(file.Path)) throw new InvalidDataException("配置包含不支持的资源目录。");
        }
        foreach (var name in Required)
        {
            if (!all.TryGetValue(name, out var bytes)) throw new InvalidDataException($"配置包缺少 {name}。");
            ValidateConfig(name, bytes);
        }
        foreach (var file in all.Where(p => !p.Key.Contains('/') && p.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
        {
            ValidateConfig(file.Key,file.Value);
            try
            {
                var node = JsonNode.Parse(file.Value);
                if (node is not JsonObject) throw new JsonException();
                RewritePaths(node, path => FromPortable(path, Path.GetTempPath(), all));
            }
            catch (JsonException ex) { throw new InvalidDataException($"配置 {file.Key} 无效。", ex); }
        }
        return all;
    }

    internal static void ValidateConfig(string name, byte[] bytes)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new JsonException();
            int supported = name switch {"widgets.json"=>new WidgetConfig().SchemaVersion,"settings.json"=>new AppSettings().SchemaVersion,"folders.json"=>new FolderConfig().SchemaVersion,"shortcuts.json"=>new ShortcutConfig().SchemaVersion,_=>1};
            if (root.TryGetProperty("SchemaVersion", out var version))
            {
                if (version.ValueKind == JsonValueKind.Number && version.GetDouble() > supported)
                    throw new FutureSchemaException($"{name} 配置版本 {version.GetRawText()} 高于支持版本 {supported}，禁止导入覆盖。");
                if (!version.TryGetInt32(out int number) || number < 1) throw new JsonException("配置版本无效。");
            }
            object? value = name switch
            {
                "settings.json" => JsonSerializer.Deserialize<AppSettings>(bytes),
                "shortcuts.json" => JsonSerializer.Deserialize<ShortcutConfig>(bytes),
                "widgets.json" => JsonSerializer.Deserialize<WidgetConfig>(bytes),
                "folders.json" => JsonSerializer.Deserialize<FolderConfig>(bytes),
                "monitors.json" => JsonSerializer.Deserialize<MonitorLayoutConfig>(bytes),
                "events.json" => JsonSerializer.Deserialize<EventConfig>(bytes),
                _ => JsonSerializer.Deserialize<JsonElement>(bytes)
            };
            if (value == null) throw new JsonException();
            if (value is FolderConfig folders && (folders.Items == null || folders.DesktopEntries == null ||
                folders.Items.Any(f => f == null || f.Items == null || f.Items.Any(i => !ValidShortcut(i))) ||
                folders.DesktopEntries.Any(e => e == null || !ValidShortcut(e.Item)))) throw new JsonException("文件夹数据无效。");
            if (value is ShortcutConfig shortcuts && (shortcuts.Items == null || shortcuts.Items.Any(i => !ValidShortcut(i)))) throw new JsonException("入口数据无效。");
            if (value is WidgetConfig widgets && (widgets.Items == null || widgets.Items.Any(w => w == null ||
                w.InstanceId == null || w.PluginId == null || w.Dates == null || w.Dates.Any(d => d == null) || w.Modules == null))) throw new JsonException("组件数据无效。");
            if(value is MonitorLayoutConfig monitors)
            {
                if(monitors.Layouts==null || monitors.Layouts.Any(p=>p.Value==null))throw new JsonException("显示器布局无效。");
                foreach(var layout in monitors.Layouts.Values)ValidateConfig("widgets.json",JsonSerializer.SerializeToUtf8Bytes(layout));
            }
            if(value is WidgetConfig pluginWidgets && pluginWidgets.Items.Any(w=>w.PluginId=="external-plugin"&&!PluginProtocol.ValidId(w.ExternalPluginId)))throw new JsonException("插件标识无效。");
            if(value is EventConfig events)
            {
                if(events.Items==null || events.DeliveredReminders==null || events.HolidayYears==null || events.Items.Any(e=>e==null||CalendarEventRules.Validate(e)!=null))throw new JsonException("日程数据无效。");
                foreach(var year in events.HolidayYears)
                {
                    if(year!=null&&year.SchemaVersion>1)throw new FutureSchemaException("节假日数据来自未来版本，禁止覆盖。");
                    if(year==null||HolidayRules.Validate(year)!=null)throw new JsonException("节假日数据无效。");
                }
            }
            if (name != "settings.json" && root.TryGetProperty("Items", out var items) &&
                (items.ValueKind != JsonValueKind.Array || items.EnumerateArray().Any(e => e.ValueKind != JsonValueKind.Object))) throw new JsonException("Items 无效。");
        }
        catch (JsonException ex) { throw new InvalidDataException($"配置 {name} 无效。", ex); }
    }
    private static bool ValidShortcut(ShortcutItem? item) => item != null && item.Id != null && item.Name != null &&
        item.SourcePath != null && item.TargetPath != null && item.Arguments != null && item.WorkingDirectory != null && item.IconPath != null && item.Group != null && item.ChildFolderId != null && item.LaunchMode != null && item.ExecutablePath != null;

    private static void ValidateCurrentProfile(string root)
    {
        var names=Directory.Exists(root)?Directory.EnumerateFiles(root,"*.json").Select(Path.GetFileName).Where(n=>n!=ConfigTransaction.PendingFileName).ToArray():Required;
        foreach (var name in names)
        {
            var path = Path.Combine(root, name!);
            if (!File.Exists(path)) continue;
            // Corrupt older files can be backed up and replaced; future files cannot.
            var bytes = ReadBounded(path);
            try { ValidateConfig(name!, bytes); } catch (InvalidDataException) { }
        }
    }

    private static ProfilePackageReport Report(Dictionary<string, byte[]> files)
    {
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Where(p => !p.Key.Contains('/') && p.Key.EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
            RewritePaths(JsonNode.Parse(file.Value)!, path =>
            {
                var portable = path.Replace('\\', '/');
                if (IsGenerated(portable) && files.ContainsKey(portable)) return path;
                if (!string.IsNullOrWhiteSpace(path) && !path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) &&
                    Path.IsPathFullyQualified(path) && !File.Exists(path) && !Directory.Exists(path)) missing.Add(path);
                return path;
            });
        return new(files.Keys.OrderBy(p => p).ToArray(), missing.OrderBy(p => p).ToArray(),
            ["用户原始文件仅保留路径，不随配置包复制。导入后不会自动运行脚本或入口。"]);
    }

    private static string ToPortable(string value, string root)
    {
        if (!Path.IsPathFullyQualified(value)) return value;
        var full = Path.GetFullPath(value);
        if (!IsWithin(full, root)) return value;
        var relative = Path.GetRelativePath(root, full).Replace('\\', '/');
        // A stale managed path remains absolute and is reported missing, rather than producing an unrestorable package.
        return IsGenerated(relative) && (File.Exists(full)||Directory.Exists(full)) ? relative : value;
    }
    private static string FromPortable(string value, string root, Dictionary<string, byte[]> files)
    {
        var relative = value.Replace('\\', '/');
        // Only generated assets are relocated. Plugin data and external user documents retain their paths.
        if (!IsGenerated(relative)) return value;
        ValidateName(relative);
        if (!files.ContainsKey(relative)) throw new InvalidDataException($"配置包缺少引用资源：{relative}");
        return Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
    }
    private static bool IsGenerated(string path) => GeneratedRoots.Any(root => path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase));
    private static bool IsWithin(string path, string root) => path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static bool AllowedFile(string path) => !path.Equals(ConfigTransaction.PendingFileName, StringComparison.OrdinalIgnoreCase) &&
        ((!path.Contains('/') && path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) || AssetRoots.Any(a => path.StartsWith(a + "/", StringComparison.OrdinalIgnoreCase)));
    private static void RewritePaths(JsonNode node, Func<string, string> rewrite)
    {
        if (node is JsonObject obj)
            foreach (var property in obj.ToArray())
            {
                if (PathProperties.Contains(property.Key) && property.Value is JsonValue value && value.TryGetValue<string>(out var path)) obj[property.Key] = rewrite(path);
                else if (property.Value != null) RewritePaths(property.Value, rewrite);
            }
        else if (node is JsonArray array) foreach (var child in array) if (child != null) RewritePaths(child, rewrite);
    }
    private static void ValidateName(string name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 240 || name.Contains('\\') || name.Contains(':') || name.StartsWith('/') || name.Contains('\0')) throw new InvalidDataException("配置包路径无效。");
        foreach (var segment in name.Split('/'))
        {
            var stem = segment.Split('.')[0];
            if (segment is "" or "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ') || segment.Any(c => c < 32 || "<>\"|?*".Contains(c)) ||
                new[] { "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9" }.Contains(stem, StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("配置包路径穿越或保留名称无效。");
        }
    }
    private static IEnumerable<string> EnumerateSafe(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("不导出符号链接资源目录。");
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("不导出符号链接资源。");
            if ((attributes & FileAttributes.Directory) != 0) { foreach (var file in EnumerateSafe(entry)) yield return file; }
            else yield return entry;
        }
    }
    private static byte[] ReadBounded(string path)
    {
        if (new FileInfo(path).Length > MaxEntryBytes) throw new InvalidDataException("配置资源超过大小限制。");
        return File.ReadAllBytes(path);
    }
    private static void AddFile(IDictionary<string, byte[]> files, string name, byte[] bytes)
    { ValidateName(name); if (bytes.LongLength > MaxEntryBytes || !files.TryAdd(name, bytes)) throw new InvalidDataException("配置资源超限或路径重复。"); }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static void Write(ZipArchive archive, string name, byte[] bytes)
    { using var output = archive.CreateEntry(name, CompressionLevel.Optimal).Open(); output.Write(bytes); }
    private static JsonNode DefaultConfig(string name) => JsonSerializer.SerializeToNode(name switch
    {
        "settings.json" => (object)new AppSettings(), "shortcuts.json" => new ShortcutConfig(),
        "widgets.json" => new WidgetConfig(), "folders.json" => new FolderConfig(), _ => throw new ArgumentException(name)
    })!;
}




