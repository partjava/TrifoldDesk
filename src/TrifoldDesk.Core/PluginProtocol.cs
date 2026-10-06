using System.Text.Json;
using System.Text.RegularExpressions;
namespace TrifoldDesk.Core;
public sealed class PluginManifest
{
 public int ApiVersion {get;set;}=1;public string Id{get;set;}="";public string Name{get;set;}="";public string Version{get;set;}="1.0";public string Executable{get;set;}="";public List<string> Arguments{get;set;}=[];public List<string> Capabilities{get;set;}=[];
}
public sealed class PluginOutput {public string Title{get;set;}="";public List<string> Lines{get;set;}=[];}
public sealed class PluginApprovals{public int SchemaVersion{get;set;}=1;public Dictionary<string,string> Hashes{get;set;}=[];}
public static class PluginProtocol
{
 public static readonly JsonSerializerOptions Json=new(){PropertyNameCaseInsensitive=true};
 public static bool ValidId(string? id) => id != null && Regex.IsMatch(id,"^[a-zA-Z][a-zA-Z0-9-]{0,63}$");
 public static PluginOutput Parse(string line)
 {
  if(line.Length>16384)throw new InvalidDataException("插件消息超过大小限制。");
  var output=JsonSerializer.Deserialize<PluginOutput>(line,Json)??throw new InvalidDataException("插件消息为空。");
  if(output.Title==null||output.Title.Length>200||output.Lines==null||output.Lines.Count>32||output.Lines.Any(s=>s==null||s.Length>1000))throw new InvalidDataException("插件消息格式无效。");return output;
 }
 public static string Validate(PluginManifest manifest,string directory)
 {
  if(manifest.ApiVersion!=1||!ValidId(manifest.Id)||string.IsNullOrWhiteSpace(manifest.Name)||manifest.Name.Length>100||!System.Version.TryParse(manifest.Version,out _))throw new InvalidDataException("插件标识、版本或API不兼容。");
  if(string.IsNullOrWhiteSpace(manifest.Executable)||Path.IsPathRooted(manifest.Executable)||manifest.Executable.Split('\\','/').Any(s=>s is "." or "..")||!manifest.Executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("插件必须指定包内的EXE宿主。");
  if(manifest.Arguments==null||manifest.Arguments.Count>32||manifest.Arguments.Any(a=>a==null||a.Length>1000||a.Contains('\0'))||manifest.Capabilities==null||manifest.Capabilities.Count>20)throw new InvalidDataException("插件参数无效。");
  string root=Path.GetFullPath(directory),file=Path.GetFullPath(Path.Combine(root,manifest.Executable));if(!file.StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("插件路径越界。");return file;
 }
}
