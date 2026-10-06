using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows.Threading;
namespace TrifoldDesk;
public sealed class ExternalPluginWidget : StackPanel, IProfileReplacementParticipant
{
 private readonly ConfigManager _store;private readonly string _id;private readonly TextBlock _status=new(){TextWrapping=TextWrapping.Wrap};private Process? _worker;private CancellationTokenSource? _cancel;private bool _active;private int _generation;
 private bool _retired;
 public ExternalPluginWidget(string id,ConfigManager store)
 {
  _id=id;_store=store;Children.Add(_status);_status.Text="插件未启用 · 右键管理";var menu=new ContextMenu();ContextMenu=menu;
  var approve=new MenuItem{Header="允许启动插件…"};menu.Items.Add(approve);approve.Click+=async(_,_)=>await Start(true);
  var stop=new MenuItem{Header="停止插件"};menu.Items.Add(stop);stop.Click+=(_,_)=>{Stop();_status.Text="插件已停止";};
  Loaded+=async(_,_)=>{if(_retired)return;_active=true;await Start(false);};Unloaded+=(_,_)=>{_active=false;Stop();};
 }
 private async Task Start(bool approve)
 {
  if(!_active)return;
  Stop();int generation=++_generation;
  try
  {
   if(!PluginProtocol.ValidId(_id))throw new InvalidDataException("插件标识无效");
   var directory=Path.GetFullPath(Path.Combine(_store.DirectoryPath,"plugins",_id));
   if(new FileInfo(Path.Combine(directory,"manifest.json")).Length>65536)throw new InvalidDataException("插件清单过大");
   var manifest=JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(Path.Combine(directory,"manifest.json")),PluginProtocol.Json)??throw new InvalidDataException("插件清单为空");
   if(manifest.Id!=_id)throw new InvalidDataException("插件标识不匹配");string executable=PluginProtocol.Validate(manifest,directory);
   if((File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0 ||(File.GetAttributes(executable)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("插件不能使用目录链接");
   string hash=await Task.Run(()=>PluginFiles.Hash(directory));
   if(!_active||generation!=_generation)return;
   var approved=_store.Load<PluginApprovals>("plugin-approvals.json").Value;
   if(!approved.Hashes.TryGetValue(_id,out var prior)||prior!=hash)
   {
    if(!approve){_status.Text="插件需要授权 · 右键允许启动";return;}
    if(MessageBox.Show(Window.GetWindow(this),$"启动 {manifest.Name} {manifest.Version}？\n此插件以独立进程运行，仍具有当前用户的本机权限。\n声明能力：{string.Join("、",manifest.Capabilities)}", "第三方插件",MessageBoxButton.OKCancel,MessageBoxImage.Information,MessageBoxResult.Cancel)!=MessageBoxResult.OK)return;
    approved.Hashes[_id]=hash;_store.Save("plugin-approvals.json",approved);
   }
   var utf8=new System.Text.UTF8Encoding(false,true);
   var info=new ProcessStartInfo(executable){WorkingDirectory=directory,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardInputEncoding=utf8,StandardOutputEncoding=utf8};foreach(var arg in manifest.Arguments)info.ArgumentList.Add(arg);
   var worker=new Process{StartInfo=info};if(!worker.Start())throw new IOException("插件进程未启动");_worker=worker;_cancel=new CancellationTokenSource();
   await worker.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new{apiVersion=1,type="init"}));await worker.StandardInput.FlushAsync();
   _status.Text="正在连接插件…";_ = ReadOutput(worker,_cancel.Token,generation);_ = DrainErrors(worker,_cancel.Token);
  }
  catch(Exception ex){App.Log(ex);_status.Text="插件不可用："+ex.Message;Stop();}
 }
 private async Task ReadOutput(Process worker,CancellationToken token,int generation)
 {
  try
  {
   while(!token.IsCancellationRequested)
   {
    // Read in bounded chunks: a malicious worker cannot allocate an unbounded line.
    var line=new System.Text.StringBuilder();var character=new char[1];
    using var deadline=CancellationTokenSource.CreateLinkedTokenSource(token);deadline.CancelAfter(TimeSpan.FromSeconds(45));
    int count;
    while((count=await worker.StandardOutput.ReadAsync(character.AsMemory(),deadline.Token))>0 && character[0]!='\n'){if(line.Length>=16384)throw new InvalidDataException("插件输出过长");if(character[0]!='\r')line.Append(character[0]);}
    if(count==0)throw new IOException("插件输出通道已关闭");
    if(line.Length==0)continue;
    var output=PluginProtocol.Parse(line.ToString());
    if(!_active||generation!=_generation)return;
    await Dispatcher.InvokeAsync(()=>{if(generation==_generation)_status.Text=output.Title+"\n"+string.Join("\n",output.Lines);});
   }
  }
  catch(Exception ex){if(token.IsCancellationRequested)return;await Dispatcher.InvokeAsync(()=>{if(generation==_generation){_status.Text="插件已停止："+ex.Message;Stop();}});}
 }
 private static async Task DrainErrors(Process worker,CancellationToken token)
 {try{var buffer=new char[1024];while(await worker.StandardError.ReadAsync(buffer.AsMemory(),token)>0){}}catch{} }
 private void Stop(){_generation++;_cancel?.Cancel();_cancel?.Dispose();_cancel=null;try{if(_worker!=null&&!_worker.HasExited)_worker.Kill(true);}catch(Exception ex){App.Log(ex);}_worker?.Dispose();_worker=null;}
 public void PrepareForProfileReplacement(){_retired=true;_active=false;Stop();}
}

