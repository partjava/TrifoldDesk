using System.IO.Compression;
using System.Text.Json;
using TrifoldDesk.Core;
public static class ProfilePackageTests
{
    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.Combine(Environment.CurrentDirectory, ".test-data"), "Trifold-package-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            var future = Path.Combine(root, "future"); Directory.CreateDirectory(future);
            var file = Path.Combine(future, "settings.json"); var bytes = "{\"SchemaVersion\":999,\"GlassOpacity\":0.5}"; File.WriteAllText(file, bytes);
            var config = new ConfigManager(future);
            check(config.Load<AppSettings>("settings.json").Warning != null, "future config load reports incompatibility");
            bool denied = false; try { new ConfigManager(future).Save("settings.json", new AppSettings()); } catch (IOException) { denied = true; }
            check(denied && File.ReadAllText(file) == bytes, "future schema blocks writes without prior load and preserves original");
            denied = false; try { new ConfigManager(future).Save("settings.json", new { CustomData = "bypass" }); } catch (IOException) { denied = true; }
            check(denied, "generic save cannot bypass a known future file schema");
            var futureArchive = Path.Combine(root, "future-raw.zip"); ProfilePackage.Export(future, futureArchive);
            var futureMonitorRoot=Path.Combine(root,"future-monitor-profile");Directory.CreateDirectory(futureMonitorRoot);
            var futureMonitorJson="{\"SchemaVersion\":1,\"Layouts\":{\"display\":{\"SchemaVersion\":99,\"Items\":[]}}}";
            File.WriteAllText(Path.Combine(futureMonitorRoot,"monitors.json"),futureMonitorJson);
            var futureMonitorStore=new ConfigManager(futureMonitorRoot);
            check(futureMonitorStore.Load<MonitorLayoutConfig>("monitors.json").Warning!=null && futureMonitorStore.ReadOnlyFiles.Contains("monitors.json"),"future nested monitor schema enters read-only mode on normal startup");
            denied=false;try{new ConfigManager(futureMonitorRoot).Save("monitors.json",new MonitorLayoutConfig());}catch(IOException){denied=true;}
            check(denied&&File.ReadAllText(Path.Combine(futureMonitorRoot,"monitors.json"))==futureMonitorJson,"future nested monitor schema blocks normal writes without prior load");
            using (var zip = ZipFile.OpenRead(futureArchive)) { using var reader = new StreamReader(zip.GetEntry("settings.json")!.Open()); check(reader.ReadToEnd() == bytes, "future schema export preserves raw configuration bytes"); }
            var hugeFuture = Path.Combine(root, "huge-future"); Directory.CreateDirectory(hugeFuture);
            File.WriteAllText(Path.Combine(hugeFuture, "settings.json"), "{\"SchemaVersion\":2147483648}");
            denied = false; try { new ConfigManager(hugeFuture).Save("settings.json", new AppSettings()); } catch (IOException) { denied = true; }
            check(denied, "future schema above int range cannot bypass write protection");
            var source = Path.Combine(root, "source"); Directory.CreateDirectory(Path.Combine(source, "app-links", "id"));
            Directory.CreateDirectory(Path.Combine(source, "plugins", "third-party"));
            Directory.CreateDirectory(Path.Combine(source, "assets"));
            var icon = Path.Combine(source, "assets", "custom.png"); File.WriteAllText(icon, "icon-bytes");
            File.WriteAllText(Path.Combine(source, "plugins", "third-party", "state.bin"), "plugin-data");
            var link = Path.Combine(source, "app-links", "id", "应用.lnk"); File.WriteAllText(link, "link-bytes");
            var external = Path.Combine(root, "original.txt"); File.WriteAllText(external, "private document");
            new ConfigManager(source).Save("shortcuts.json", new ShortcutConfig { Items = [new() { SourcePath = link, TargetPath = external, IconPath = icon, Arguments = "--keep" }, new() { SourcePath = Path.Combine(root, "missing.txt") }] });
            new ConfigManager(source).Save("plugin-approvals.json",new PluginApprovals{Hashes=new(){["third-party"]="trusted"}});
            new ConfigManager(source).Save("monitors.json",new MonitorLayoutConfig());
            var stalePath=Path.Combine(source,"app-links","missing-generated.lnk");
            new ConfigManager(source).Save("extra-reference.json",new{SourcePath=stalePath});
            var archive = Path.Combine(root, "profile.zip"); ProfilePackage.Export(source, archive);
            using(var zip=ZipFile.OpenRead(archive))check(zip.GetEntry("plugin-approvals.json")==null,"plugin execution approvals never travel with a profile export");
            var preview = ProfilePackage.Preview(archive);
            check(preview.MissingExternalPaths.Contains(stalePath),"stale managed links remain restorable with a missing-path warning");
            check(preview.MissingExternalPaths.Contains(Path.Combine(root, "missing.txt")), "package preview reports missing external documents");
            using (var zip = ZipFile.OpenRead(archive)) check(!zip.Entries.Any(e => e.Name == "original.txt"), "package never copies external documents");
            var target = Path.Combine(root, "target"); Directory.CreateDirectory(target); File.WriteAllText(Path.Combine(target, "arbitrary-plugin-state.bin"), "old-profile");
            var imported = ProfilePackage.Import(archive, target);
            check(!File.Exists(Path.Combine(target,"plugin-approvals.json")),"imported plugins require a new local execution approval");
            var newerMonitor=Path.Combine(root,"future-monitor.zip");File.Copy(archive,newerMonitor);
            ReplaceConfig(newerMonitor,"monitors.json","{\"SchemaVersion\":1,\"Layouts\":{\"display\":{\"SchemaVersion\":99,\"Items\":[]}}}");
            Reject(()=>ProfilePackage.Import(newerMonitor,target),check,"future nested monitor layout rejected before import mutation");
            var item = new ConfigManager(target).Load<ShortcutConfig>("shortcuts.json").Value.Items[0];
            check(item.SourcePath == Path.Combine(target, "app-links", "id", "应用.lnk") && File.Exists(item.SourcePath) && item.TargetPath == external && item.Arguments == "--keep", "generated links relocate while external path and arguments remain intact");
            check(item.IconPath == Path.Combine(target, "assets", "custom.png") && File.Exists(item.IconPath), "generated assets relocate with custom icon references");
            check(File.ReadAllText(Path.Combine(target, "plugins", "third-party", "state.bin")) == "plugin-data", "package includes opaque third party plugin data");
            check(File.ReadAllText(Path.Combine(imported.BackupDirectory!, "arbitrary-plugin-state.bin")) == "old-profile", "import backup retains arbitrary current profile data");
            var invalid = Path.Combine(root, "invalid.zip"); File.Copy(archive, invalid);
            using (var zip = ZipFile.Open(invalid, ZipArchiveMode.Update)) { using var writer = new StreamWriter(zip.CreateEntry("../escape.txt").Open()); writer.Write("escape"); }
            Reject(() => ProfilePackage.Import(invalid, target), check, "traversal rejected before profile mutation");
            check(File.Exists(item.SourcePath), "invalid import preserves current profile");
            var duplicate = Path.Combine(root, "duplicate.zip"); File.Copy(archive, duplicate);
            using (var zip = ZipFile.Open(duplicate, ZipArchiveMode.Update)) { using var writer = new StreamWriter(zip.CreateEntry("SETTINGS.JSON").Open()); writer.Write("{}"); }
            Reject(() => ProfilePackage.Preview(duplicate), check, "case insensitive duplicate paths rejected");
            var newer = Path.Combine(root, "newer.zip"); File.Copy(archive, newer);
            ReplaceConfig(newer, "settings.json", bytes);
            Reject(() => ProfilePackage.Import(newer, target), check, "future config package rejected before mutation");
            Reject(() => ProfilePackage.Import(archive, future), check, "import cannot overwrite existing future profile");
            var invalidItems = Path.Combine(root, "invalid-items.zip"); File.Copy(archive, invalidItems);
            ReplaceConfig(invalidItems, "shortcuts.json", "{\"SchemaVersion\":1,\"Items\":null}");
            Reject(() => ProfilePackage.Preview(invalidItems), check, "typed configuration validation rejects null collections");
            var invalidFolder = Path.Combine(root, "invalid-folder.zip"); File.Copy(archive, invalidFolder);
            ReplaceConfig(invalidFolder, "folders.json", "{\"SchemaVersion\":1,\"Items\":[{\"Id\":\"f\",\"Items\":null}]}");
            Reject(() => ProfilePackage.Preview(invalidFolder), check, "nested folder null collections rejected before commit");
            var missingAsset = Path.Combine(root, "missing-asset.zip"); File.Copy(archive, missingAsset);
            ReplaceConfig(missingAsset, "shortcuts.json", "{\"Items\":[{\"SourcePath\":\"app-links/missing.lnk\"}]}");
            Reject(() => ProfilePackage.Preview(missingAsset), check, "preview rejects missing generated resources");
            var oversized = Path.Combine(root, "oversized.zip"); File.Copy(archive, oversized);
            using (var zip = ZipFile.Open(oversized, ZipArchiveMode.Update)) { using var stream = zip.CreateEntry("plugins/huge.bin").Open(); var block = new byte[1024 * 1024]; for (int n=0;n<33;n++) stream.Write(block); }
            Reject(() => ProfilePackage.Preview(oversized), check, "compressed oversized resource rejected before extraction");
            // Force the first directory rename to fail with an open profile file on Windows.
            if (OperatingSystem.IsWindows())
            {
                using var locked = new FileStream(item.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                Reject(() => ProfilePackage.Import(archive, target), check, "commit failure preserves complete existing profile");
                check(File.Exists(Path.Combine(target, "plugins", "third-party", "state.bin")), "failed commit retains plugin state");
            }
        }
        finally { Directory.Delete(root, true); }
    }
    static void ReplaceConfig(string path, string name, string contents)
    {
        using var zip = ZipFile.Open(path, ZipArchiveMode.Update);
        ProfilePackageManifest manifest;
        using (var stream = zip.GetEntry("manifest.json")!.Open()) manifest = JsonSerializer.Deserialize<ProfilePackageManifest>(stream)!;
        var content = System.Text.Encoding.UTF8.GetBytes(contents);
        manifest.Files.RemoveAll(f => f.Path == name);
        manifest.Files.Add(new(name, content.Length, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content))));
        zip.GetEntry(name)!.Delete(); using (var stream = zip.CreateEntry(name).Open()) stream.Write(content);
        zip.GetEntry("manifest.json")!.Delete(); using (var stream = zip.CreateEntry("manifest.json").Open()) JsonSerializer.Serialize(stream, manifest);
    }
    static void Reject(Action action, Action<bool,string> check, string name) { bool rejected = false; try { action(); } catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException or IOException) { rejected = true; } check(rejected, name); }
}


