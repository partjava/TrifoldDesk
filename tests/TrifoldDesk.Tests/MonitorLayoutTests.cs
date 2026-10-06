using System.Text.Json;
using TrifoldDesk.Core;

public static class MonitorLayoutTests
{
    public static void Run(Action<bool, string> check)
    {
        var sharedId = "same-widget-id";
        var first = new WidgetConfig { Items = [new() { InstanceId = sharedId, PluginId = "clock", X = 12, Y = 30, PaneIndex = 0, IsLocked = true }] };
        var second = new WidgetConfig { Items = [new() { InstanceId = sharedId, PluginId = "clock", X = 700, Y = 80, PaneIndex = 2, IsCollapsed = true }] };
        var empty = new MonitorLayoutConfig();
        var one = MonitorLayoutRules.SaveActive(empty, "DISPLAY1", first);
        var both = MonitorLayoutRules.SaveActive(one, "DISPLAY2", second);
        check(empty.Layouts.Count == 0 && one.Layouts.Count == 1 && both.Layouts.Count == 2, "saving monitor layouts clones configuration and retains other displays");
        var restoredFirst = MonitorLayoutRules.LoadActive(both, "display1", new());
        var restoredSecond = MonitorLayoutRules.LoadActive(both, "DISPLAY2", new());
        check(restoredFirst.Items.Single().X == 12 && restoredFirst.Items.Single().IsLocked && restoredSecond.Items.Single().X == 700 && restoredSecond.Items.Single().IsCollapsed && restoredFirst.Items.Single().InstanceId == restoredSecond.Items.Single().InstanceId, "same widget identities have independent per-monitor placement and state");
        restoredFirst.Items.Single().X = 50;
        check(both.Layouts["DISPLAY1"].Items.Single().X == 12 && first.Items.Single().X == 12, "loaded layout never shares mutable widgets with stored or source configuration");
        var updated = MonitorLayoutRules.SaveActive(both, "display1", restoredFirst);
        check(updated.Layouts.Count == 2 && updated.Layouts["DISPLAY1"].Items.Single().X == 50 && updated.Layouts["DISPLAY2"].Items.Single().X == 700, "case-insensitive device update preserves key and offline layout");
        check(MonitorLayoutRules.OfflineDevices(updated, ["display1"]).SequenceEqual(["DISPLAY2"]) && updated.Layouts.Count == 2, "disconnected monitor diagnostics never delete its stored configuration");
        var fallback = new WidgetConfig { Items = [new() { PluginId = "calendar", Modules = ["cpu"] }] };
        var newMonitor = MonitorLayoutRules.LoadActive(updated, "DISPLAY3", fallback);
        newMonitor.Items[0].Modules.Clear();
        check(fallback.Items[0].Modules.Count == 1 && updated.Layouts.Count == 2, "new monitor uses isolated fallback without silently creating or overwriting saved layouts");
        var roundtrip = JsonSerializer.Deserialize<MonitorLayoutConfig>(JsonSerializer.Serialize(updated))!;
        check(MonitorLayoutRules.LoadActive(roundtrip, "DISPLAY2", new()).Items.Single().X == 700, "offline monitor placement survives configuration roundtrip and reconnect");
        bool rejected = false; try { MonitorLayoutRules.SaveActive(updated, " ", first); } catch (ArgumentException) { rejected = true; }
        check(rejected && updated.Layouts.Count == 2, "invalid display identity cannot overwrite monitor layouts");
    }
}
