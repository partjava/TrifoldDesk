using System.Text.Json;
using TrifoldDesk.Core;

internal static class LegacyMigrationTests
{
    public static void Run(Action<bool, string> check)
    {
        var widgets = new WidgetConfig { Items = [
            new() { InstanceId = "cpu-old", PluginId = "cpu", X = 10, Y = 20 },
            new() { InstanceId = "summary", PluginId = "system-summary", PaneIndex = 1, X = 80, Y = 90, Width = 340, Height = 260, Modules = ["network"], IsLocked = true },
            new() { InstanceId = "gpu-old", PluginId = "gpu" },
            new() { InstanceId = "summary-extra", PluginId = "system-summary", Modules = ["memory"] },
            new() { InstanceId = "apps-old", PluginId = "apps", PaneIndex = 2, X = 25, Y = 60, Style = "paper", IsCollapsed = true },
            new() { InstanceId = "calendar", PluginId = "calendar" }
        ] };
        var shortcuts = new ShortcutConfig { Items = [
            new() { Id = "second", Name = "中文", Group = "开发", Order = 9, SourcePath = @"C:\源\run.lnk", TargetPath = @"C:\程序\run.exe", Arguments = "--name \"中文 参数\"", WorkingDirectory = @"C:\项目", IconPath = @"C:\图标.ico", IconIndex = 3 },
            new() { Id = "first", Group = "开发", Order = 2 },
            new() { Id = "other", Group = "学习", Order = 4 },
            new() { Id = "archived-project", ScreenIndex = 1, Group = "资料" }
        ] };
        var folders = new FolderConfig { Items = [new() { Id = "archived", Name = "归档", Items = [new() { Id = "archived-item", Order = 22 }], Slots = new() { ["archived-item"] = 7 } }], DesktopEntries = [new() { Item = new() { Id = "desktop" }, X = 71, Y = 92, PaneIndex = 1 }] };
        var before = JsonSerializer.Serialize(new { widgets, folders, shortcuts });
        var migrated = LegacyPluginMigration.Apply(widgets, folders, shortcuts);
        check(migrated.Changed && migrated.Widgets.Items.All(w => w.PluginId is not ("cpu" or "gpu" or "apps")), "legacy migration replaces active metrics and libraries");
        var summary = migrated.Widgets.Items.Single(w => w.PluginId == "system-summary");
        check(summary.InstanceId == "summary" && summary.X == 80 && summary.Y == 90 && summary.PaneIndex == 1 && summary.IsLocked && summary.Width == 340 && summary.Height == 260, "existing summary retains placement and state");
        check(summary.Modules.ToHashSet().SetEquals(["cpu", "gpu", "memory", "network"]), "summary merges explicit selections and legacy metrics without duplicates");
        var created = migrated.Widgets.Items.Where(w => w.PluginId == "folder").ToList();
        check(created.Count == 2 && created.All(w => w.PaneIndex == 2 && w.IsCollapsed && w.Style == "paper") && created[0].X == 25 && created[0].Y == 60, "active application groups become folders on original pane");
        var development = migrated.Folders.Items.Single(f => f.Name == "开发");
        check(development.Items.Select(i => i.Id).SequenceEqual(["first", "second"]) && JsonSerializer.Serialize(development.Items[1]) == JsonSerializer.Serialize(shortcuts.Items[0]), "group migration preserves identities metadata and stored order");
        check(!migrated.Folders.Items.Any(f => f.Items.Any(i => i.Id == "archived-project")) && JsonSerializer.Serialize(migrated.Folders.Items[0]) == JsonSerializer.Serialize(folders.Items[0]) && JsonSerializer.Serialize(migrated.Folders.DesktopEntries) == JsonSerializer.Serialize(folders.DesktopEntries), "inactive library archived folders and desktop entries stay untouched");
        check(before == JsonSerializer.Serialize(new { widgets, folders, shortcuts }), "migration never mutates source configurations");
        var repeated = LegacyPluginMigration.Apply(migrated.Widgets, migrated.Folders, shortcuts);
        check(!repeated.Changed && JsonSerializer.Serialize(repeated.Widgets) == JsonSerializer.Serialize(migrated.Widgets) && JsonSerializer.Serialize(repeated.Folders) == JsonSerializer.Serialize(migrated.Folders), "migration is idempotent");
        var again = LegacyPluginMigration.Apply(widgets, folders, shortcuts);
        check(JsonSerializer.Serialize(again.Widgets) == JsonSerializer.Serialize(migrated.Widgets) && JsonSerializer.Serialize(again.Folders) == JsonSerializer.Serialize(migrated.Folders), "migration output identities and layout are deterministic");
        repeated.Folders.Items[0].Items[0].Name = "changed";
        repeated.Widgets.Items[0].Modules.Add("battery");
        check(migrated.Folders.Items[0].Items[0].Name != "changed" && !summary.Modules.Contains("battery"), "no-op migration still returns deep isolated clones");

        var metrics = LegacyPluginMigration.Apply(new WidgetConfig { Items = [new() { InstanceId = "first-metric", PluginId = "battery", PaneIndex = 0, X = 100, Y = 200 }, new() { PluginId = "disks" }, new() { PluginId = "battery" }] }, new(), new());
        check(metrics.Widgets.Items.Count == 1 && metrics.Widgets.Items[0].InstanceId == "first-metric" && metrics.Widgets.Items[0].X == 100 && metrics.Widgets.Items[0].Modules.SequenceEqual(["battery", "disks"]), "metric-only migration anchors first metric and selects only present modules");
        var defaultSummary = LegacyPluginMigration.Apply(new WidgetConfig { Items = [new() { PluginId = "system-summary" }, new() { PluginId = "cpu" }] }, new(), new());
        check(defaultSummary.Widgets.Items.Single().Modules.ToHashSet().SetEquals(SummaryRules.Available), "implicit all-modules summary selection remains all modules");

        var crowdedShortcuts = new ShortcutConfig { Items = Enumerable.Range(0, 40).Select(i => new ShortcutItem { Id = "entry-" + i, Group = "group-" + i }).ToList() };
        var crowded = LegacyPluginMigration.Apply(new WidgetConfig { Items = [new() { InstanceId = "legacy", PluginId = "apps" }] }, new FolderConfig { Items = [new() { Id = "legacy", Name = "existing archive", Items = [new() { Id = "keep" }] }] }, crowdedShortcuts);
        check(crowded.Widgets.Items.Count == 40 && crowded.Folders.Items.Sum(f => f.Items.Count) == 41 && crowded.Folders.Items[0].Name == "existing archive", "migration exceeds add limit without losing groups or overwriting colliding archived folder");
        check(crowded.Widgets.Items.Select(w => w.InstanceId).Distinct().Count() == 40 && crowded.Widgets.Items.All(w => crowded.Folders.Items.Count(f => f.Id == w.InstanceId) == 1), "every migrated group has a unique matching folder identity");

        var projects = LegacyPluginMigration.Apply(new WidgetConfig { Items = [new() { PluginId = "projects", PaneIndex = 0 }] }, new(), shortcuts);
        check(projects.Folders.Items.Single().Items.Single().Id == "archived-project" && projects.Widgets.Items.Single().PaneIndex == 0, "projects source membership is independent of mounted pane");
        var empty = LegacyPluginMigration.Apply(new WidgetConfig { Items = [new() { InstanceId = "empty-apps", PluginId = "apps" }] }, new(), new());
        check(empty.Widgets.Items.Single().PluginId == "folder" && empty.Folders.Items.Single().Items.Count == 0, "empty mounted library becomes an empty folder");
        var malformed = LegacyPluginMigration.Apply(new WidgetConfig { Items = [new() { InstanceId = "partial", PluginId = "apps" }] }, new FolderConfig { Items = null! }, new ShortcutConfig { Items = [null!, new() { Id = null!, Group = null! }, new() { Id = "space", Group = " " }] });
        check(malformed.Folders.Items.Single().Name == "未分组" && malformed.Folders.Items.Single().Items.Count == 2 && malformed.Folders.Items.Single().Items[0].Id == null, "partial legacy data preserves entries with missing identifiers and groups");
        var duplicateLibraries = LegacyPluginMigration.Apply(new WidgetConfig { Items = [new() { InstanceId = "duplicate", PluginId = "apps", PaneIndex = 0 }, new() { InstanceId = "duplicate", PluginId = "apps", PaneIndex = 2 }] }, new(), new ShortcutConfig { Items = [new() { Id = "shared", Group = "same" }] });
        check(duplicateLibraries.Widgets.Items.Select(w => w.InstanceId).Distinct().Count() == 2 && duplicateLibraries.Folders.Items.Count == 2 && duplicateLibraries.Folders.Items.All(f => f.Items.Single().Id == "shared"), "duplicate legacy mounts receive separate folders without losing memberships");
    }
}
