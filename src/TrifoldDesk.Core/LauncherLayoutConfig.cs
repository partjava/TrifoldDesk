using System.IO;
using System.Text.Json;

namespace TrifoldDesk.Core;

public sealed class LauncherLayoutConfig
{
    public int SchemaVersion { get; set; } = 1;
    public List<string> OrderedShellPaths { get; set; } = [];

    public static string GetDefaultFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "TrifoldDesk");
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
        return Path.Combine(dir, "launchpad-order.json");
    }

    public static LauncherLayoutConfig Load(string? filePath = null)
    {
        filePath ??= GetDefaultFilePath();
        try
        {
            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<LauncherLayoutConfig>(json) ?? new LauncherLayoutConfig();
            }
        }
        catch
        {
            // Fallback to empty on read error
        }
        return new LauncherLayoutConfig();
    }

    public void Save(string? filePath = null)
    {
        filePath ??= GetDefaultFilePath();
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(filePath, json);
        }
        catch
        {
            // Ignore disk write failure during layout preview
        }
    }
}
