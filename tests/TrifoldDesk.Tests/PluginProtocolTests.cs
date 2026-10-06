using TrifoldDesk.Core;
public static class PluginProtocolTests
{
 public static void Run(Action<bool,string> check)
 {
  check(!PluginProtocol.ValidId(null)&&!PluginProtocol.ValidId("../outside")&&PluginProtocol.ValidId("example-worker"),"plugin identity is validated before file access");
  var sample=PluginProtocol.Parse("{\"title\":\"例子\",\"lines\":[\"hello\"]}");check(sample.Title=="例子"&&sample.Lines.Single()=="hello","plugin protocol accepts data without executing markup");
  bool rejected=false;try{PluginProtocol.Parse(new string('x',17000));}catch(InvalidDataException){rejected=true;}check(rejected,"plugin output has bounded message size");
  rejected=false;try{PluginProtocol.Validate(new PluginManifest{Id="../escape",Name="bad",Executable="worker.exe"},Environment.CurrentDirectory);}catch(InvalidDataException){rejected=true;}check(rejected,"plugin identity cannot escape install directory");
  rejected=false;try{PluginProtocol.Validate(new PluginManifest{Id="example",Name="bad",Executable="../worker.exe"},Environment.CurrentDirectory);}catch(InvalidDataException){rejected=true;}check(rejected,"plugin executable must remain within its package");
 }
}
