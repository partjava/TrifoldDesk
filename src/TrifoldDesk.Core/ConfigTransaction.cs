using System.Security.Cryptography;
using System.Text.Json;

namespace TrifoldDesk.Core;

/// <summary>Durable multi-file config commits. Recover before any startup config reads.</summary>
public static class ConfigTransaction
{
    public const string PendingFileName = "config-transaction.pending.json";
    private static readonly object Gate = new();
    private static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase)
        { "settings.json", "shortcuts.json", "widgets.json", "folders.json", "panes.json", "monitors.json", "events.json", "plugin-approvals.json", "optional-workbench.json", "optional-weather.json", "optional-projects.json" };
    private sealed class Journal
    {
        public int Version { get; set; } = 1;
        public string StagingDirectory { get; set; } = "";
        public List<JournalFile> Files { get; set; } = [];
    }
    private sealed record JournalFile(string Name, string Sha256, bool Existed);

    public static void Commit(string directory, Dictionary<string, object> configurations)
    {
        lock (Gate)
        {
            Recover(directory);
            if (configurations.Count == 0) return;
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var config in configurations)
            {
                if (!Allowed.Contains(config.Key) || !unique.Add(config.Key) || config.Value == null) throw new InvalidDataException("事务配置名称无效或重复。");
                CheckCurrent(Path.Combine(directory, config.Key));
            }
            Directory.CreateDirectory(directory);
            var journal = new Journal { StagingDirectory = "config-transaction-" + Guid.NewGuid().ToString("N") };
            var stage = Path.Combine(directory, journal.StagingDirectory);
            var pending = Path.Combine(directory, PendingFileName);
            var applied = new List<JournalFile>();
            bool durable = false;
            try
            {
                Directory.CreateDirectory(Path.Combine(stage, "new"));
                Directory.CreateDirectory(Path.Combine(stage, "old"));
                foreach (var config in configurations)
                {
                    var bytes = JsonSerializer.SerializeToUtf8Bytes(config.Value, new JsonSerializerOptions { WriteIndented = true });
                    if (bytes.LongLength > ProfilePackage.MaxEntryBytes) throw new InvalidDataException("事务配置超过大小限制。");
                    Validate(config.Key, bytes);
                    WriteDurable(Path.Combine(stage, "new", config.Key), bytes);
                    var original = Path.Combine(directory, config.Key);
                    bool existed = File.Exists(original);
                    if (existed) WriteDurable(Path.Combine(stage, "old", config.Key), ReadBounded(original));
                    journal.Files.Add(new(config.Key, Hash(bytes), existed));
                }
                // A flushed journal is the point at which restart recovery will finish this commit.
                WriteAtomic(pending, JsonSerializer.SerializeToUtf8Bytes(journal));
                durable = true;
                foreach (var file in journal.Files)
                {
                    CheckCurrent(Path.Combine(directory, file.Name));
                    WriteAtomic(Path.Combine(directory, file.Name), ReadBounded(Path.Combine(stage, "new", file.Name)));
                    applied.Add(file);
                }
                File.Delete(pending);
                durable = false;
            }
            catch
            {
                bool rollbackSucceeded = true;
                foreach (var file in applied.AsEnumerable().Reverse())
                {
                    try
                    {
                        var target = Path.Combine(directory, file.Name);
                        if (file.Existed) WriteAtomic(target, ReadBounded(Path.Combine(stage, "old", file.Name)));
                        else File.Delete(target);
                    }
                    catch { rollbackSucceeded = false; }
                }
                if (rollbackSucceeded && durable) { File.Delete(pending); durable = false; }
                // If rollback cannot finish, retain journal + staged data for startup replay.
                throw;
            }
            finally { if (!durable && Directory.Exists(stage)) Directory.Delete(stage, true); }
        }
    }

    public static bool Recover(string directory)
    {
        lock (Gate)
        {
            var pending = Path.Combine(directory, PendingFileName);
            if (!File.Exists(pending)) return false;
            Journal journal;
            try { journal = JsonSerializer.Deserialize<Journal>(ReadBounded(pending)) ?? throw new JsonException(); }
            catch (JsonException ex) { throw new InvalidDataException("配置事务日志无效，已保留原文件。", ex); }
            const string prefix = "config-transaction-";
            if (journal.Version != 1 || journal.StagingDirectory == null || !journal.StagingDirectory.StartsWith(prefix, StringComparison.Ordinal) ||
                !Guid.TryParseExact(journal.StagingDirectory[prefix.Length..], "N", out _) || journal.Files == null || journal.Files.Count is < 1 or > 5)
                throw new InvalidDataException("配置事务日志版本或暂存路径无效。");
            var stage = Path.Combine(directory, journal.StagingDirectory);
            RejectLink(stage); RejectLink(Path.Combine(stage, "new"));
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in journal.Files)
            {
                if (file == null || !Allowed.Contains(file.Name) || files.ContainsKey(file.Name)) throw new InvalidDataException("配置事务日志名称无效或重复。");
                var staged = Path.Combine(stage, "new", file.Name); RejectLink(staged);
                var bytes = ReadBounded(staged);
                if (Hash(bytes) != file.Sha256) throw new InvalidDataException("配置事务数据完整性验证失败。");
                Validate(file.Name, bytes);
                CheckCurrent(Path.Combine(directory, file.Name));
                files.Add(file.Name, bytes);
            }
            // All payloads and current versions are checked before replay writes anything.
            foreach (var file in files) WriteAtomic(Path.Combine(directory, file.Key), file.Value);
            File.Delete(pending);
            Directory.Delete(stage, true);
            return true;
        }
    }

    private static void CheckCurrent(string path)
    {
        if (!File.Exists(path)) return;
        RejectLink(path);
        try { ConfigManager.CheckSchema<object>(path); }
        catch (JsonException) { /* Invalid older primary is preserved in the transaction backup. */ }
    }
    private static void Validate(string name, byte[] bytes)
    {
        if (name != "panes.json") ProfilePackage.ValidateConfig(name, bytes);
        else
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new InvalidDataException("折叠配置无效。");
        }
    }
    private static byte[] ReadBounded(string path)
    {
        if (new FileInfo(path).Length > ProfilePackage.MaxEntryBytes) throw new InvalidDataException("配置事务文件超过大小限制。");
        return File.ReadAllBytes(path);
    }
    private static void RejectLink(string path)
    { if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("配置事务不支持符号链接文件。"); }
    private static void WriteAtomic(string path, byte[] bytes)
    {
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try { WriteDurable(temp, bytes); File.Move(temp, path, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private static void WriteDurable(string path, byte[] bytes)
    { using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None); stream.Write(bytes); stream.Flush(true); }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}

