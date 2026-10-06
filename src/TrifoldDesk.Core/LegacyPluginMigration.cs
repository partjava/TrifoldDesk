using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TrifoldDesk.Core;

public sealed record LegacyPluginMigrationResult(WidgetConfig Widgets, FolderConfig Folders, bool Changed);

/// <summary>Converts mounted legacy widgets without touching shortcut management data or files.</summary>
public static class LegacyPluginMigration
{
    public static LegacyPluginMigrationResult Apply(WidgetConfig widgets, FolderConfig folders, ShortcutConfig shortcuts)
    {
        ArgumentNullException.ThrowIfNull(widgets);
        ArgumentNullException.ThrowIfNull(folders);
        ArgumentNullException.ThrowIfNull(shortcuts);
        var nextWidgets = PluginRules.Clone(widgets);
        var nextFolders = FolderRules.Clone(folders);
        var original = nextWidgets.Items ?? [];
        var summaries = original.Where(w => w != null && w.PluginId == "system-summary").ToList();
        var metrics = original.Where(w => w != null && SummaryRules.Available.Contains(w.PluginId)).ToList();
        bool mergeSummary = metrics.Count > 0 || summaries.Count > 1;
        var anchor = summaries.FirstOrDefault() ?? metrics.FirstOrDefault();
        if (mergeSummary && anchor != null)
        {
            var modules = summaries.SelectMany(SummaryRules.Modules).Concat(metrics.Select(w => w.PluginId)).ToHashSet(StringComparer.Ordinal);
            anchor.PluginId = "system-summary";
            anchor.Modules = SummaryRules.Available.Where(modules.Contains).ToList();
            if (summaries.Count == 0) anchor.Title = "系统概览";
        }

        // Do not use CanAdd/Normalize here: the add limit must never discard migrated groups.
        var usedIds = original.Where(w => w != null).Select(w => w.InstanceId).Concat((nextFolders.Items ?? []).Where(f => f != null).Select(f => f.Id)).ToHashSet(StringComparer.Ordinal);
        var result = new List<WidgetInstance>();
        bool changed = mergeSummary;
        foreach (var widget in original)
        {
            if (widget == null) { result.Add(widget!); continue; }
            if (mergeSummary && (widget.PluginId == "system-summary" || SummaryRules.Available.Contains(widget.PluginId)))
            {
                if (ReferenceEquals(widget, anchor)) result.Add(widget);
                continue;
            }
            if (widget.PluginId is not ("apps" or "projects")) { result.Add(widget); continue; }
            changed = true;
            int screen = widget.PluginId == "apps" ? 0 : 1;
            var groups = (shortcuts.Items ?? []).Where(i => i != null && i.ScreenIndex == screen)
                .OrderBy(i => i.Order)
                .GroupBy(i => string.IsNullOrWhiteSpace(i.Group) ? "未分组" : i.Group, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Name: g.Key, Items: g.ToList())).ToList();
            if (groups.Count == 0) groups.Add((screen == 0 ? "应用" : "资料", []));
            nextFolders.Items ??= [];
            int index = 0;
            foreach (var group in groups)
            {
                var folderWidget = Clone(widget);
                bool canReuseId = index == 0 && !string.IsNullOrWhiteSpace(widget.InstanceId)
                    && !nextFolders.Items.Any(f => f != null && f.Id == widget.InstanceId)
                    && original.Count(w => w != null && w.InstanceId == widget.InstanceId) == 1;
                folderWidget.InstanceId = canReuseId ? widget.InstanceId : AllocateId(widget, group.Name, index, usedIds);
                folderWidget.PluginId = "folder";
                folderWidget.Title = group.Name;
                // Retain the first group's exact placement. Additional groups remain in the same pane
                // and extend downwards; the desktop host provides scrolling for crowded layouts.
                if (index > 0 && double.IsFinite(widget.Y) && widget.Y >= 0)
                    folderWidget.Y = widget.Y + index * (double.IsFinite(widget.Height) && widget.Height > 0 ? widget.Height + 16 : 216);
                var folder = new FolderData { Id = folderWidget.InstanceId, Name = group.Name, Items = group.Items.Select(Clone).ToList() };
                for (int slot = 0; slot < folder.Items.Count; slot++)
                    if (folder.Items[slot].Id != null) folder.Slots.TryAdd(folder.Items[slot].Id, slot);
                nextFolders.Items.Add(folder);
                result.Add(folderWidget);
                index++;
            }
        }
        if (changed) nextWidgets.Items = result;
        return new(nextWidgets, nextFolders, changed);
    }

    private static string AllocateId(WidgetInstance widget, string group, int index, HashSet<string> used)
    {
        string seed = JsonSerializer.Serialize(new { widget.InstanceId, widget.PluginId, widget.PaneIndex, Group = group, Index = index });
        string id = "legacy-folder-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();
        string candidate = id;
        for (int suffix = 1; !used.Add(candidate); suffix++) candidate = id + "-" + suffix;
        return candidate;
    }

    private static T Clone<T>(T source) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(source))!;
}
