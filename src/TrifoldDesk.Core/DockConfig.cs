using System.IO;
using System.Text.Json;

namespace TrifoldDesk.Core;

public sealed class DockItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string TargetPath { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string IconPath { get; set; } = "";
    public int IconIndex { get; set; }
    public bool RunAsAdmin { get; set; }
    public int Order { get; set; }
}

public sealed class DockConfig
{
    public int SchemaVersion { get; set; } = 1;
    public bool Enabled { get; set; } = true;
    public double IconSize { get; set; } = 42;
    public bool AutoHide { get; set; } = true;
    public bool AlwaysOnTop { get; set; } = false;
    public List<DockItem> Items { get; set; } = [];
}

public static class DockRules
{
    public static DockConfig Clone(DockConfig source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(source);
        return JsonSerializer.Deserialize<DockConfig>(bytes) ?? new DockConfig();
    }

    public static DockConfig AddItem(DockConfig current, DockItem item)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(item);
        var next = Clone(current);

        string normTarget = (item.TargetPath ?? "").Trim();
        if (!string.IsNullOrEmpty(normTarget))
        {
            var existing = next.Items.FirstOrDefault(i =>
                string.Equals(i.TargetPath?.Trim(), normTarget, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(item.Name) && string.Equals(i.Name?.Trim(), item.Name.Trim(), StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(i.TargetPath) && string.Equals(Path.GetFileName(i.TargetPath), Path.GetFileName(normTarget), StringComparison.OrdinalIgnoreCase)));

            if (existing != null)
            {
                existing.Name = item.Name;
                if (!string.IsNullOrEmpty(item.Arguments)) existing.Arguments = item.Arguments;
                return NormalizeOrders(next);
            }
        }

        int maxOrder = next.Items.Count > 0 ? next.Items.Max(i => i.Order) : -1;
        item.Order = maxOrder + 1;
        if (string.IsNullOrWhiteSpace(item.Id)) item.Id = Guid.NewGuid().ToString("N");
        next.Items.Add(item);
        return NormalizeOrders(next);
    }

    public static DockConfig RemoveItem(DockConfig current, string itemId)
    {
        ArgumentNullException.ThrowIfNull(current);
        var next = Clone(current);
        next.Items.RemoveAll(i => string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
        return NormalizeOrders(next);
    }

    public static DockConfig ReorderItem(DockConfig current, string itemId, int direction)
    {
        ArgumentNullException.ThrowIfNull(current);
        var next = Clone(current);
        var list = next.Items.OrderBy(i => i.Order).ToList();
        int idx = list.FindIndex(i => string.Equals(i.Id, itemId, StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return next;
        int targetIdx = idx + direction;
        if (targetIdx < 0 || targetIdx >= list.Count) return next;
        (list[idx], list[targetIdx]) = (list[targetIdx], list[idx]);
        for (int i = 0; i < list.Count; i++) list[i].Order = i;
        next.Items = list;
        return next;
    }

    public static DockConfig Deduplicate(DockConfig current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var next = Clone(current);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unique = new List<DockItem>();
        foreach (var item in next.Items.OrderBy(i => i.Order))
        {
            string key = !string.IsNullOrWhiteSpace(item.TargetPath)
                ? Path.GetFileName(item.TargetPath).ToLowerInvariant()
                : item.Name.ToLowerInvariant();
            if (seen.Add(key))
            {
                unique.Add(item);
            }
        }
        next.Items = unique;
        return NormalizeOrders(next);
    }

    public static DockConfig NormalizeOrders(DockConfig current)
    {
        ArgumentNullException.ThrowIfNull(current);
        var list = current.Items.OrderBy(i => i.Order).ToList();
        for (int i = 0; i < list.Count; i++) list[i].Order = i;
        current.Items = list;
        return current;
    }

    public static DockConfig CreateDefault()
    {
        var config = new DockConfig();
        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string sysDir = Environment.SystemDirectory;
        var defaults = new List<DockItem>
        {
            new() { Name = "文件资源管理器", TargetPath = Path.Combine(winDir, "explorer.exe"), Order = 0 },
            new() { Name = "Microsoft Edge", TargetPath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", Order = 1 },
            new() { Name = "任务管理器", TargetPath = Path.Combine(sysDir, "taskmgr.exe"), Order = 2 },
            new() { Name = "系统设置", TargetPath = "ms-settings:", Order = 3 }
        };
        config.Items = defaults;
        return config;
    }
}
