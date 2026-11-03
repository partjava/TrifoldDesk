using TrifoldDesk.Core;

public static class DockTests
{
    public static void Run(Action<bool, string> check)
    {
        var config = DockRules.CreateDefault();
        check(config.Items.Count == 4, "default dock has 4 initial items");
        check(config.Items[0].Name == "文件资源管理器" && config.Items[0].Order == 0, "default dock first item is file explorer");

        var item = new DockItem { Name = "微信", TargetPath = @"C:\WeChat\WeChat.exe" };
        var next = DockRules.AddItem(config, item);
        check(next.Items.Count == 5 && next.Items.Last().Name == "微信" && next.Items.Last().Order == 4, "add item appends with correct order");

        var reordered = DockRules.ReorderItem(next, next.Items.Last().Id, -1);
        check(reordered.Items[3].Name == "微信" && reordered.Items[3].Order == 3, "reorder moves item to previous index");

        var removed = DockRules.RemoveItem(reordered, reordered.Items[3].Id);
        check(removed.Items.Count == 4 && !removed.Items.Any(i => i.Name == "微信"), "remove item unpins from dock");
        check(removed.Items.Select(i => i.Order).SequenceEqual(new[] { 0, 1, 2, 3 }), "dock items orders remain contiguous after removal");

        var cloned = DockRules.Clone(removed);
        check(cloned.Items.Count == 4 && cloned != removed, "dock config clone produces deep independent copy");
    }
}
