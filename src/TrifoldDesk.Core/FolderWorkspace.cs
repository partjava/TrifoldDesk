using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TrifoldDesk.Core;

public sealed record FolderWorkspaceResult(FolderConfig Config, bool Changed, string? Error = null);

public static class FolderWorkspaceRules
{
    public static bool CanNest(FolderConfig config, string childId, string parentId)
    {
        if (string.IsNullOrWhiteSpace(childId) || string.IsNullOrWhiteSpace(parentId) || childId == parentId) return false;
        var folders = config.Items ?? [];
        if (folders.Count(f => f != null && f.Id == childId) != 1 || folders.Count(f => f != null && f.Id == parentId) != 1) return false;
        if (!string.IsNullOrWhiteSpace(folders.Single(f => f.Id == parentId).DirectoryPath)) return false;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(); pending.Push(childId);
        while (pending.TryPop(out var id))
        {
            if (id == parentId) return false;
            if (!visited.Add(id)) continue;
            foreach (var folder in folders.Where(f => f != null && f.Id == id))
                foreach (var item in folder.Items ?? [])
                    if (item != null && !string.IsNullOrWhiteSpace(item.ChildFolderId)) pending.Push(item.ChildFolderId);
            foreach (var folder in folders.Where(f => f != null && f.ParentFolderId == id)) pending.Push(folder.Id);
        }
        return true;
    }

    public static FolderWorkspaceResult Nest(FolderConfig config, string childId, string parentId, int slot = 0)
    {
        var next = FolderRules.Clone(config);
        if (!CanNest(config, childId, parentId)) return new(next, false, "无法嵌套：文件夹不存在、目标是映射目录，或操作会产生循环引用。");
        if (slot is < 0 or > 4095) return new(next, false, "格位必须在0到4095之间。");
        var child = next.Items.Single(f => f.Id == childId);
        var parent = next.Items.Single(f => f.Id == parentId);
        var link = next.Items.SelectMany(f => f.Items ?? []).FirstOrDefault(i => i != null && i.ChildFolderId == childId);
        RemoveReferences(next, childId);
        child.ParentFolderId = parentId;
        parent.Items ??= [];
        parent.Slots = FolderRules.Positions(parent);
        var occupied = parent.Items.FirstOrDefault(i => parent.Slots[i.Id] == slot);
        if (occupied != null)
        {
            int available = 0;
            while (available < 4096 && (available == slot || parent.Slots.Values.Contains(available))) available++;
            if (available == 4096) return new(FolderRules.Clone(config), false, "文件夹没有可用格位。");
            parent.Slots[occupied.Id] = available;
        }
        link ??= new ShortcutItem { Id = UniqueItemId(next, "folder-link:" + childId), Name = child.Name, ChildFolderId = childId };
        if (parent.Items.Any(i => i.Id == link.Id)) link.Id = UniqueItemId(next, "folder-link:" + childId);
        parent.Items.Add(link);
        parent.Slots[link.Id] = slot;
        return Finish(config, next);
    }

    public static FolderWorkspaceResult Detach(FolderConfig config, string childId)
    {
        var next = FolderRules.Clone(config);
        var matches = (next.Items ?? []).Where(f => f != null && f.Id == childId).ToList();
        if (matches.Count != 1) return new(next, false, "文件夹不存在或标识重复。");
        RemoveReferences(next, childId, removeDesktop: false);
        matches[0].ParentFolderId = "";
        return Finish(config, next);
    }

    /// <summary>Groups virtual desktop entries. Physical targets are only referenced and never modified.</summary>
    public static FolderWorkspaceResult StackDesktopIcons(FolderConfig config, string targetId, string draggedId, string folderId, string name)
    {
        var next = FolderRules.Clone(config);
        var entries = next.DesktopEntries ?? [];
        var targets = entries.Where(d => d?.Item?.Id == targetId).ToList();
        var draggedEntries = entries.Where(d => d?.Item?.Id == draggedId).ToList();
        if (targetId == draggedId || targets.Count != 1 || draggedEntries.Count != 1)
            return new(next, false, "请选择两个不同且有效的桌面入口。");
        if (string.IsNullOrWhiteSpace(folderId) || (next.Items ?? []).Any(f => f != null && f.Id == folderId))
            return new(next, false, "文件夹标识为空或已存在。");
        var target = targets[0]; var dragged = draggedEntries[0];
        if (target.PaneIndex != dragged.PaneIndex || target.PaneIndex is < 0 or > 2)
            return new(next, false, "只能将同一页面的入口组成文件夹。");
        var childIds = new[] { target.Item.ChildFolderId, dragged.Item.ChildFolderId }.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
        if (childIds.Distinct().Count() != childIds.Count || childIds.Any(id => (next.Items ?? []).Count(f => f != null && f.Id == id && string.IsNullOrWhiteSpace(f.ParentFolderId)) != 1))
            return new(next, false, "文件夹入口重复、已嵌套或关联数据不存在。");
        next.Items ??= [];
        next.Items.Add(new FolderData { Id = folderId, Name = string.IsNullOrWhiteSpace(name) ? "新建文件夹" : name, Items = [target.Item, dragged.Item], Slots = new() { [targetId] = 0, [draggedId] = 1 } });
        foreach (var id in childIds) next.Items.Single(f => f.Id == id).ParentFolderId = folderId;
        int index = entries.TakeWhile(e => !ReferenceEquals(e, target)).Count(e => !ReferenceEquals(e, dragged));
        entries.Remove(target); entries.Remove(dragged);
        entries.Insert(Math.Min(index, entries.Count), new DesktopEntry {
            Item = new() { Id = UniqueItemId(next, "folder-icon:" + folderId), Name = next.Items.Last().Name, ChildFolderId = folderId, ScreenIndex = target.Item.ScreenIndex, Order = target.Item.Order },
            X = target.X, Y = target.Y, PaneIndex = target.PaneIndex
        });
        next.DesktopEntries = entries;
        return Finish(config, next);
    }

    private static void RemoveReferences(FolderConfig config, string childId, bool removeDesktop = true)
    {
        foreach (var folder in config.Items ?? [])
        {
            if (folder == null) continue;
            var items = folder.Items;
            if (items == null) continue;
            foreach (var item in items.Where(i => i != null && i.ChildFolderId == childId).ToList())
            { items.Remove(item); folder.Slots?.Remove(item.Id); }
        }
        if (removeDesktop) config.DesktopEntries?.RemoveAll(d => d?.Item?.ChildFolderId == childId);
    }

    private static string UniqueItemId(FolderConfig config, string basis)
    {
        var used = (config.Items ?? []).Where(f => f != null).SelectMany(f => f.Items ?? []).Where(i => i != null).Select(i => i.Id)
            .Concat((config.DesktopEntries ?? []).Where(d => d?.Item != null).Select(d => d.Item.Id)).ToHashSet(StringComparer.Ordinal);
        string id = basis;
        for (int suffix = 1; used.Contains(id); suffix++) id = basis + ":" + suffix;
        return id;
    }

    private static FolderWorkspaceResult Finish(FolderConfig before, FolderConfig after) =>
        new(after, JsonSerializer.Serialize(before) != JsonSerializer.Serialize(after));
}

/// <summary>In-memory configuration history; snapshots do not claim to undo filesystem operations.</summary>
public sealed class ConfigurationHistory<T> where T : class
{
    private readonly List<string> _undo = [];
    private readonly List<string> _redo = [];
    private string _current;
    private readonly int _capacity;
    public ConfigurationHistory(T initial, int capacity = 20)
    {
        ArgumentNullException.ThrowIfNull(initial);
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _current = JsonSerializer.Serialize(initial); _capacity = capacity;
    }
    public T Current => Read(_current);
    public int UndoCount => _undo.Count;
    public int RedoCount => _redo.Count;
    public bool Commit(T state)
    {
        ArgumentNullException.ThrowIfNull(state);
        string serialized = JsonSerializer.Serialize(state);
        if (serialized == _current) return false;
        _undo.Add(_current);
        if (_undo.Count > _capacity) _undo.RemoveAt(0);
        _current = serialized; _redo.Clear(); return true;
    }
    public bool TryUndo(out T state)
    {
        if (_undo.Count == 0) { state = Current; return false; }
        _redo.Add(_current); _current = _undo[^1]; _undo.RemoveAt(_undo.Count - 1); state = Current; return true;
    }
    public bool TryRedo(out T state)
    {
        if (_redo.Count == 0) { state = Current; return false; }
        _undo.Add(_current); _current = _redo[^1]; _redo.RemoveAt(_redo.Count - 1); state = Current; return true;
    }
    public void Reset(T state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _current = JsonSerializer.Serialize(state); _undo.Clear(); _redo.Clear();
    }
    private static T Read(string value) => JsonSerializer.Deserialize<T>(value)!;
}

public sealed record MappedDirectoryEntry(string Path, string Name, bool IsDirectory, bool IsReparsePoint);
public sealed record MappedDirectorySnapshot(string Path, IReadOnlyList<MappedDirectoryEntry> Entries, string? Error, bool IsTruncated);

public static class MappedDirectoryScanner
{
    /// <summary>Enumerates one directory level only, including links without following them.</summary>
    public static MappedDirectorySnapshot Scan(string path, int maxEntries = 10000, CancellationToken cancellationToken = default)
    {
        if (maxEntries < 1) throw new ArgumentOutOfRangeException(nameof(maxEntries));
        cancellationToken.ThrowIfCancellationRequested();
        var entries = new List<MappedDirectoryEntry>();
        string resolved = path;
        bool truncated = false;
        try
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("映射目录路径为空。");
            resolved = System.IO.Path.GetFullPath(path);
            foreach (var entry in Directory.EnumerateFileSystemEntries(resolved))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (entries.Count == maxEntries) { truncated = true; break; }
                var attributes = File.GetAttributes(entry);
                entries.Add(new(entry, System.IO.Path.GetFileName(entry), attributes.HasFlag(FileAttributes.Directory), attributes.HasFlag(FileAttributes.ReparsePoint)));
            }
            return new(resolved, Sort(entries), null, truncated);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or System.Security.SecurityException)
        { return new(resolved, Sort(entries), ex.Message, truncated); }
    }

    public static ShortcutItem CreateShortcut(MappedDirectoryEntry entry, int screenIndex, int order = 0)
    {
        string normalized = OperatingSystem.IsWindows() ? entry.Path.ToUpperInvariant() : entry.Path;
        return new ShortcutItem { Id = "mapped-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant(), Name = entry.Name, SourcePath = entry.Path, TargetPath = entry.Path, ScreenIndex = screenIndex, Order = order };
    }

    private static IReadOnlyList<MappedDirectoryEntry> Sort(IEnumerable<MappedDirectoryEntry> entries) => entries
        .OrderByDescending(e => e.IsDirectory).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.Name, StringComparer.Ordinal).ToArray();
}
