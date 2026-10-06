using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Threading;
using TrifoldDesk;
using TrifoldDesk.Core;
using TrifoldDesk.Services;
class Program
{
 [DllImport("user32.dll")]static extern IntPtr GetParent(IntPtr hwnd);
 [STAThread]static void Main()
 {
  var app=new Application();var root=Path.Combine(Environment.CurrentDirectory,".test-data","runtime-probe","data-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  typeof(TrifoldDesk.App).GetProperty("DataDirectory")!.SetValue(null,root);
  var store=new ConfigManager(root);var panel=new StackPanel();var window=new Window{Width=320,Height=180,Content=panel,ShowInTaskbar=false};window.Show();
  var handle=new WindowInteropHelper(window).Handle;var parent=GetParent(handle);
  var status=DesktopHostService.Attach(window);Console.WriteLine("Desktop status: "+status.Message);
  Check(status.IsAttached?GetParent(handle)==status.HostHandle:status.IsFallback&&GetParent(handle)==parent,"native desktop attach verifies parent or restores safe fallback");
  var detached=DesktopHostService.Detach(window);Check(!detached.IsFallback&&GetParent(handle)==parent,"desktop detach restores original native parent");
  string source=Path.Combine(Environment.CurrentDirectory,"tests","TrifoldDesk.PluginProbeWorker","bin","Debug","net8.0");
  File.WriteAllText(Path.Combine(source,"manifest.json"),JsonSerializer.Serialize(new PluginManifest{Id="probe-worker",Name="测试插件",Executable="TrifoldDeskProbeWorker.exe"}));
  PluginFiles.Install(source,root);var pluginRoot=Path.Combine(root,"plugins","probe-worker");
  var untrusted=new ExternalPluginWidget("probe-worker",store);panel.Children.Add(untrusted);Pump(()=>Text(untrusted).Contains("需要授权"));Check(Worker(untrusted)==null,"unapproved plugin does not spawn a process");panel.Children.Clear();
  store.Save("plugin-approvals.json",new PluginApprovals{Hashes=new(){["probe-worker"]=PluginFiles.Hash(pluginRoot)}});
  var trusted=new ExternalPluginWidget("probe-worker",store);panel.Children.Add(trusted);try{Pump(()=>Text(trusted).Contains("真实进程输出"));}catch{Console.WriteLine("Worker status: "+Text(trusted));throw;}int pid=Worker(trusted)!.Id;Check(pid>0,"authorized plugin renders bounded data from actual independent worker");panel.Children.Clear();Pump(()=>Gone(pid));Check(Gone(pid),"unloading a plugin terminates its worker process");
  var manifest=JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(Path.Combine(pluginRoot,"manifest.json")))!;manifest.Arguments=["invalid"];File.WriteAllText(Path.Combine(pluginRoot,"manifest.json"),JsonSerializer.Serialize(manifest));store.Save("plugin-approvals.json",new PluginApprovals{Hashes=new(){["probe-worker"]=PluginFiles.Hash(pluginRoot)}});
  var invalid=new ExternalPluginWidget("probe-worker",store);panel.Children.Add(invalid);Pump(()=>Text(invalid).Contains("输出过长"));Check(Worker(invalid)==null,"oversized plugin output stops and releases its worker");panel.Children.Clear();
  var escape=new ExternalPluginWidget("../escape",store);panel.Children.Add(escape);Pump(()=>Text(escape).Contains("标识无效"));Check(Worker(escape)==null,"invalid plugin identity is rejected before startup");
  panel.Children.Clear();manifest.Arguments=["eof"];File.WriteAllText(Path.Combine(pluginRoot,"manifest.json"),JsonSerializer.Serialize(manifest));store.Save("plugin-approvals.json",new PluginApprovals{Hashes=new(){["probe-worker"]=PluginFiles.Hash(pluginRoot)}});
  var closed=new ExternalPluginWidget("probe-worker",store);panel.Children.Add(closed);Pump(()=>Text(closed).Contains("输出通道已关闭"));Check(Worker(closed)==null,"closing stdout while worker remains alive stops worker without blocking dispatcher");panel.Children.Clear();
  var oldNotes=new ProductivityWidget("notes",store);var oldWeather=new WeatherWidget("weather",store);panel.Children.Add(oldNotes);panel.Children.Add(oldWeather);Pump(()=>oldNotes.IsLoaded&&oldWeather.IsLoaded);
  var notes=(TextBox)typeof(ProductivityWidget).GetField("_notes",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(oldNotes)!;notes.Text="旧便签待保存";
  store.Save("optional-workbench.json",new ProductivityConfig());
  using(var locked=new FileStream(Path.Combine(root,"optional-workbench.json"),FileMode.Open,FileAccess.Read,FileShare.Read))
  {
   bool blocked=false;try{oldNotes.FlushBeforeProfileReplacement();}catch(IOException){blocked=true;}
   Check(blocked&&oldNotes.IsLoaded&&notes.Text=="旧便签待保存" && !(bool)typeof(ProductivityWidget).GetField("_retired",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(oldNotes)!,"failed pre-import note flush keeps live unsaved text and does not retire the component");
  }
  oldNotes.PrepareForProfileReplacement();oldWeather.PrepareForProfileReplacement();Check(store.Load<ProductivityConfig>("optional-workbench.json").Value.Widgets["notes"].Notes=="旧便签待保存","profile retirement flushes pending notes into original profile before replacement");
  string incoming=root+"-incoming";var incomingStore=new ConfigManager(incoming);incomingStore.Save("optional-workbench.json",new ProductivityConfig{Widgets=new(){["notes"]=new(){Notes="导入便签"}}});incomingStore.Save("optional-weather.json",new WeatherConfig{Widgets=new(){["weather"]=new(){City=new(){Name="导入城市"}}}});
  string archive=root+".zip";ProfilePackage.Export(incoming,archive);ProfilePackage.Import(archive,root);
  typeof(WeatherWidget).GetMethod("Save",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(oldWeather,[new WeatherData{City=new(){Name="旧城市"}}]);notes.Text="迟到的旧文本";panel.Children.Clear();Pump(()=>!oldNotes.IsLoaded);
  Check(store.Load<WeatherConfig>("optional-weather.json").Value.Widgets["weather"].City!.Name=="导入城市","retired weather callback cannot overwrite imported city");Check(store.Load<ProductivityConfig>("optional-workbench.json").Value.Widgets["notes"].Notes=="导入便签","old notes unload cannot overwrite imported note content");
  window.Close();app.Shutdown();
 }
 static Process? Worker(ExternalPluginWidget widget)=>(Process?)typeof(ExternalPluginWidget).GetField("_worker",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(widget);
 static string Text(ExternalPluginWidget widget)=>((TextBlock)widget.Children[0]).Text;
 static bool Gone(int pid){try{using var p=Process.GetProcessById(pid);return p.HasExited;}catch(ArgumentException){return true;}}
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS: "+name);}
 static void Pump(Func<bool> done){var until=DateTime.UtcNow.AddSeconds(15);while(!done()&&DateTime.UtcNow<until){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);Thread.Sleep(15);}if(!done())throw new TimeoutException("Runtime probe condition timed out");}
}
