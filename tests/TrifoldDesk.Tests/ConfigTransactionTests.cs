using System.Reflection;
using System.Text.Json;
using System.Security.Cryptography;
using TrifoldDesk.Core;
public static class ConfigTransactionTests
{
    public static void Run(Action<bool,string> check)
    {
        var type = typeof(ConfigManager).Assembly.GetType("TrifoldDesk.Core.ConfigTransaction");
        check(type != null, "configuration transaction service exists");
        void Commit(string dir, Dictionary<string,object> data) => Invoke("Commit", dir, data);
        void Recover(string dir) => Invoke("Recover", dir);
        void Invoke(string method, params object[] args) { try { type!.GetMethod(method)!.Invoke(null, args); } catch(TargetInvocationException ex) { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException!).Throw(); } }
        var root=Path.Combine(Environment.CurrentDirectory,".test-data","Transaction-tests-"+Guid.NewGuid()); Directory.CreateDirectory(root);
        try
        {
            Commit(root,new() { ["folders.json"]=new FolderConfig { Items=[new(){Id="folder"}] }, ["widgets.json"]=new WidgetConfig { Items=[new(){PluginId="folder",InstanceId="folder"}] } });
            check(new ConfigManager(root).Load<FolderConfig>("folders.json").Value.Items.Single().Id=="folder" && new ConfigManager(root).Load<WidgetConfig>("widgets.json").Value.Items.Single().InstanceId=="folder", "transaction commits linked folder and widget state together");
            check(!File.Exists(Path.Combine(root,"config-transaction.pending.json")), "successful transaction clears pending journal");
            var old=File.ReadAllText(Path.Combine(root,"folders.json"));
            if(OperatingSystem.IsWindows())
            {
                using var file=new FileStream(Path.Combine(root,"widgets.json"),FileMode.Open,FileAccess.Read,FileShare.Read);
                bool failed=false;try { Commit(root,new(){["folders.json"]=new FolderConfig(),["widgets.json"]=new WidgetConfig()}); }catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){failed=true;}
                check(failed && File.ReadAllText(Path.Combine(root,"folders.json"))==old,"failed second write restores the first config byte for byte");
            }
            File.WriteAllText(Path.Combine(root,"widgets.json"),"{\"SchemaVersion\":999}");
            bool futureBlocked=false;try { Commit(root,new(){["folders.json"]=new FolderConfig(),["widgets.json"]=new WidgetConfig()}); }catch(FutureSchemaException){futureBlocked=true;}
            check(futureBlocked&&File.ReadAllText(Path.Combine(root,"folders.json"))==old,"future schema blocks entire transaction before mutation");
            File.WriteAllText(Path.Combine(root,"widgets.json"),"{}");
            var staging="config-transaction-"+Guid.NewGuid().ToString("N"); Directory.CreateDirectory(Path.Combine(root,staging,"new"));
            var bytes=JsonSerializer.SerializeToUtf8Bytes(new FolderConfig {Items=[new(){Id="replayed"}]}); File.WriteAllBytes(Path.Combine(root,staging,"new","folders.json"),bytes);
            var journal=new {Version=1,StagingDirectory=staging,Files=new[]{new{Name="folders.json",Sha256=Convert.ToHexString(SHA256.HashData(bytes)),Existed=true}}};
            File.WriteAllText(Path.Combine(root,"config-transaction.pending.json"),JsonSerializer.Serialize(journal));
            Recover(root);
            check(new ConfigManager(root).Load<FolderConfig>("folders.json").Value.Items.Single().Id=="replayed","startup recovery replays durable prepared transaction");
            check(!Directory.Exists(Path.Combine(root,staging)),"replayed transaction clears staging directory");
            var recovered=File.ReadAllText(Path.Combine(root,"folders.json"));
            File.WriteAllText(Path.Combine(root,"config-transaction.pending.json"), "{\"Version\":1,\"StagingDirectory\":null,\"Files\":[]}");
            bool malformed=false;try {Recover(root);}catch(InvalidDataException){malformed=true;}
            check(malformed&&File.ReadAllText(Path.Combine(root,"folders.json"))==recovered,"malformed journal is preserved and rejected before mutation");
            File.Delete(Path.Combine(root,"config-transaction.pending.json"));
            Directory.CreateDirectory(Path.Combine(root,staging,"new"));
            File.WriteAllBytes(Path.Combine(root,staging,"new","folders.json"),bytes);
            File.WriteAllText(Path.Combine(root,"config-transaction.pending.json"),JsonSerializer.Serialize(new {Version=1,StagingDirectory=staging,Files=new[]{new{Name="folders.json",Sha256="bad",Existed=true}}}));
            bool integrity=false;try {Recover(root);}catch(InvalidDataException){integrity=true;}
            check(integrity&&File.Exists(Path.Combine(root,"config-transaction.pending.json"))&&File.ReadAllText(Path.Combine(root,"folders.json"))==recovered,"recovery integrity failure keeps original config and journal intact");
        }
        finally {Directory.Delete(root,true);}
    }
}


