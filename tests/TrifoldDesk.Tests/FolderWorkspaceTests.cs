using System.Text.Json;
using TrifoldDesk.Core;

public static class FolderWorkspaceTests
{
    public static void Run(Action<bool, string> check)
    {
        var initial = new FolderConfig { Items = [
            new() { Id = "parent", Name = "parent", Items = [new() { Id = "ordinary", Name = "keep", Arguments = "--中文", Order = 9 }], Slots = new() { ["ordinary"] = 0 } },
            new() { Id = "child", Name = "child", Items = [new() { Id = "child-entry" }] },
            new() { Id = "other", Name = "other" }
        ] };
        var before = JsonSerializer.Serialize(initial);
        var nested = FolderWorkspaceRules.Nest(initial, "child", "parent", 0);
        check(nested.Changed && nested.Error == null && nested.Config.Items.Single(f => f.Id == "child").ParentFolderId == "parent", "nested folder stores parent association");
        var parent = nested.Config.Items.Single(f => f.Id == "parent");
        var reference = parent.Items.Single(i => i.ChildFolderId == "child");
        check(parent.Slots[reference.Id] == 0 && parent.Slots["ordinary"] != 0 && parent.Items.Single(i => i.Id == "ordinary").Arguments == "--中文", "nested reference occupies selected slot without losing shortcut metadata");
        check(JsonSerializer.Serialize(initial) == before && initial.Items[1].ParentFolderId == "", "nesting is pure and clones all configuration data");
        check(!FolderWorkspaceRules.CanNest(nested.Config, "parent", "child") && !FolderWorkspaceRules.CanNest(initial, "parent", "parent") && !FolderWorkspaceRules.CanNest(initial, "missing", "parent"), "nesting blocks self ancestor and missing-folder references");
        var cycle = FolderWorkspaceRules.Nest(nested.Config, "parent", "child");
        check(!cycle.Changed && cycle.Error != null && JsonSerializer.Serialize(cycle.Config) == JsonSerializer.Serialize(nested.Config), "rejected cycle leaves configuration untouched");
        var moved = FolderWorkspaceRules.Nest(nested.Config, "child", "other", 2);
        check(moved.Changed && moved.Config.Items.Single(f => f.Id == "parent").Items.All(i => i.ChildFolderId != "child") && moved.Config.Items.Single(f => f.Id == "other").Items.Single().ChildFolderId == "child", "reparenting removes prior folder reference and preserves contents");
        var detached = FolderWorkspaceRules.Detach(moved.Config, "child");
        check(detached.Changed && detached.Config.Items.Single(f => f.Id == "child").ParentFolderId == "" && detached.Config.Items.All(f => f.Items.All(i => i.ChildFolderId != "child")), "detaching restores top-level folder without deleting its contents");
        check(!FolderWorkspaceRules.Detach(detached.Config, "child").Changed, "detaching an already top-level folder is a no-op");
        var mixedCycle = FolderRules.Clone(initial);
        mixedCycle.Items[1].Items.Add(new() { Id = "edge", ChildFolderId = "parent" });
        check(!FolderWorkspaceRules.CanNest(mixedCycle, "child", "parent"), "cycle validation follows shortcut folder references even when parent metadata is missing");
        var mappedParent = FolderRules.Clone(initial); mappedParent.Items[0].DirectoryPath = @"C:\mapped";
        check(!FolderWorkspaceRules.Nest(mappedParent, "child", "parent").Changed, "mapped physical directory cannot receive virtual nesting writes");

        var desktop = new FolderConfig { DesktopEntries = [
            new() { Item = new() { Id = "target", Name = "target", Arguments = "--keep", Order = 11, SourcePath = "target.txt" }, X = 80, Y = 120, PaneIndex = 1 },
            new() { Item = new() { Id = "dragged", Name = "dragged", Order = 3, SourcePath = "dragged.txt" }, X = 220, Y = 400, PaneIndex = 1 },
            new() { Item = new() { Id = "unrelated" }, X = 4, Y = 8, PaneIndex = 0 }
        ] };
        var stacked = FolderWorkspaceRules.StackDesktopIcons(desktop, "target", "dragged", "stack", "new group");
        var stack = stacked.Config.Items.Single();
        var stackIcon = stacked.Config.DesktopEntries.Single(d => d.Item.ChildFolderId == "stack");
        check(stacked.Changed && stack.Items.Select(i => i.Id).SequenceEqual(["target", "dragged"]) && stack.Items[0].Arguments == "--keep" && stack.Items[0].Order == 11 && stack.Slots["target"] == 0 && stack.Slots["dragged"] == 1, "icon stacking preserves both identities metadata order and slots");
        check(stackIcon.X == 80 && stackIcon.Y == 120 && stackIcon.PaneIndex == 1 && stacked.Config.DesktopEntries.Count == 2 && desktop.Items.Count == 0 && desktop.DesktopEntries.Count == 3, "stacked folder uses target location and leaves original configuration isolated");
        check(!FolderWorkspaceRules.StackDesktopIcons(desktop, "target", "unrelated", "cross-pane", "bad").Changed && !FolderWorkspaceRules.StackDesktopIcons(desktop, "target", "target", "self", "bad").Changed, "stacking rejects cross-pane and identical icons");
        check(!FolderWorkspaceRules.StackDesktopIcons(stacked.Config, "unrelated", stackIcon.Item.Id, "stack", "duplicate").Changed, "stacking never overwrites existing folder identity");
        var reversed = FolderRules.Clone(desktop);
        reversed.DesktopEntries = [reversed.DesktopEntries[1], reversed.DesktopEntries[2], reversed.DesktopEntries[0], new() { Item = new() { Id = "after-target" }, PaneIndex = 1 }];
        var reorderedStack = FolderWorkspaceRules.StackDesktopIcons(reversed, "target", "dragged", "reverse-stack", "group");
        check(reorderedStack.Config.DesktopEntries.Select(d => d.Item.ChildFolderId == "reverse-stack" ? "group" : d.Item.Id).SequenceEqual(["unrelated", "group", "after-target"]), "stacking preserves unrelated desktop order when dragged entry precedes target");
        var folderIcons = new FolderConfig { Items = [new() { Id = "a" }, new() { Id = "b" }], DesktopEntries = [new() { Item = new() { Id = "a-icon", ChildFolderId = "a" } }, new() { Item = new() { Id = "b-icon", ChildFolderId = "b" } }] };
        var groupedFolders = FolderWorkspaceRules.StackDesktopIcons(folderIcons, "a-icon", "b-icon", "outer", "outer");
        check(groupedFolders.Changed && groupedFolders.Config.Items.Where(f => f.Id != "outer").All(f => f.ParentFolderId == "outer") && !FolderWorkspaceRules.CanNest(groupedFolders.Config, "outer", "a"), "stacking folder icons preserves nested hierarchy and blocks subsequent cycles");

        var history = new ConfigurationHistory<FolderConfig>(initial, 2);
        initial.Items[0].Name = "external mutation";
        check(history.Current.Items[0].Name == "parent", "history snapshots clone caller data");
        history.Commit(nested.Config); history.Commit(moved.Config); history.Commit(detached.Config);
        bool bounded = history.UndoCount == 2;
        bool undid = history.TryUndo(out var undone);
        check(bounded && undid && undone.Items.Single(f => f.Id == "child").ParentFolderId == "other", "history retains bounded undo snapshots and restores last accepted state");
        undone.Items.Clear();
        check(history.Current.Items.Count == 3 && history.TryUndo(out _) && !history.TryUndo(out _), "undo output mutations do not corrupt stored state and capacity evicts oldest snapshot");
        check(history.TryRedo(out var redone) && redone.Items.Single(f => f.Id == "child").ParentFolderId == "other", "redo restores exact folder hierarchy");
        check(!history.Commit(history.Current) && history.RedoCount == 1, "no-op history commits retain redo branch");
        history.Commit(stacked.Config);
        check(history.RedoCount == 0 && !history.TryRedo(out _), "new accepted edit clears redo branch");
        history.Reset(detached.Config);
        check(history.UndoCount == 0 && history.RedoCount == 0 && history.Current.Items.Count == detached.Config.Items.Count, "history reset after profile restoration clears stale snapshots");

        var scanRoot = Path.Combine(Environment.CurrentDirectory, ".test-data", "mapped-folder-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scanRoot);
        try
        {
            Directory.CreateDirectory(Path.Combine(scanRoot, "资料"));
            File.WriteAllText(Path.Combine(scanRoot, "中文.txt"), "unchanged source bytes");
            File.WriteAllText(Path.Combine(scanRoot, "Alpha.txt"), "second source");
            var scanned = MappedDirectoryScanner.Scan(scanRoot);
            check(scanned.Error == null && !scanned.IsTruncated && scanned.Entries.Count == 3 && scanned.Entries[0].IsDirectory && scanned.Entries[0].Name == "资料", "mapped directory scan reads immediate children and sorts directories first");
            var entry = scanned.Entries.Single(e => e.Name == "中文.txt");
            var shortcut = MappedDirectoryScanner.CreateShortcut(entry, 2, 7);
            check(shortcut.SourcePath == Path.Combine(scanRoot, "中文.txt") && shortcut.TargetPath == shortcut.SourcePath && shortcut.ScreenIndex == 2 && shortcut.Order == 7 && shortcut.Id == MappedDirectoryScanner.CreateShortcut(entry, 2, 7).Id, "dragging mapped entry creates stable reference metadata without moving file");
            check(File.ReadAllText(shortcut.SourcePath) == "unchanged source bytes" && Directory.GetFileSystemEntries(scanRoot).Length == 3 && MappedDirectoryScanner.Scan(Path.Combine(scanRoot, "资料")).Entries.Count == 0, "mapping and reference creation preserve all physical files and browse child directory separately");
            var truncated = MappedDirectoryScanner.Scan(scanRoot, 1);
            check(truncated.IsTruncated && truncated.Entries.Count == 1, "large mapped directory scan limits work and reports truncation");
            check(MappedDirectoryScanner.Scan(Path.Combine(scanRoot, "missing")).Error != null && MappedDirectoryScanner.Scan("\0").Error != null, "missing and invalid mapped paths return explicit errors");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            bool canceled = false; try { MappedDirectoryScanner.Scan(scanRoot, cancellationToken: cancellation.Token); } catch (OperationCanceledException) { canceled = true; }
            check(canceled, "mapped directory scan observes cancellation");
        }
        finally
        {
            var safeRoot = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, ".test-data")) + Path.DirectorySeparatorChar;
            if (Path.GetFullPath(scanRoot).StartsWith(safeRoot, StringComparison.OrdinalIgnoreCase)) Directory.Delete(scanRoot, true);
        }
    }
}
