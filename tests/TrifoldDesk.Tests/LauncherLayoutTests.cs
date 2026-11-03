using System.IO;
using TrifoldDesk.Core;

public static class LauncherLayoutTests
{
    public static void Run(Action<bool, string> check)
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"launchpad-order-test-{Guid.NewGuid():N}.json");
        try
        {
            var config = new LauncherLayoutConfig
            {
                OrderedShellPaths = ["app1.shell", "app2.shell", "app3.shell"]
            };
            config.Save(tempFile);
            check(File.Exists(tempFile), "launchpad order saves to json file");

            var loaded = LauncherLayoutConfig.Load(tempFile);
            check(loaded.OrderedShellPaths.Count == 3, "loaded order has 3 items");
            check(loaded.OrderedShellPaths[0] == "app1.shell" && loaded.OrderedShellPaths[2] == "app3.shell", "order roundtrips accurately");

            var missing = LauncherLayoutConfig.Load(tempFile + ".nonexistent");
            check(missing.OrderedShellPaths.Count == 0, "missing file returns clean empty config");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
