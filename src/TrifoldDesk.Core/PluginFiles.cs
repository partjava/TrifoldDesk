using System.Security.Cryptography;
using System.Text.Json;
namespace TrifoldDesk.Core;
public static class PluginFiles
{
 public static IReadOnlyList<string> Enumerate(string directory)
 {
  var paths=new List<string>();long size=0;
  void Scan(string folder,int depth=0)
  {
   if(depth>16)throw new InvalidDataException("插件目录层级过深。");
   if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("插件不能包含目录链接。");
   foreach(var path in Directory.EnumerateFileSystemEntries(folder))
   {
    var attributes=File.GetAttributes(path);if((attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("插件不能包含文件链接。");
    if((attributes&FileAttributes.Directory)!=0)Scan(path,depth+1);
    else{long length=new FileInfo(path).Length;size+=length;if(length>32*1024*1024||size>128*1024*1024||paths.Count>=512)throw new InvalidDataException("插件包过大。");paths.Add(path);}
   }
  }
  Scan(directory);return paths.OrderBy(p=>p,StringComparer.OrdinalIgnoreCase).ToArray();
 }
 public static string Hash(string directory)
 {
  using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
  foreach(var path in Enumerate(directory)){hash.AppendData(System.Text.Encoding.UTF8.GetBytes(Path.GetRelativePath(directory,path)));hash.AppendData(SHA256.HashData(File.ReadAllBytes(path)));}return Convert.ToHexString(hash.GetHashAndReset());
 }
 public static PluginManifest Install(string source,string profileDirectory)
 {
  if(new FileInfo(Path.Combine(source,"manifest.json")).Length>65536)throw new InvalidDataException("插件清单过大");
  var manifest=JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(Path.Combine(source,"manifest.json")),PluginProtocol.Json)??throw new InvalidDataException("插件清单为空");
  var executable=PluginProtocol.Validate(manifest,source);if(!File.Exists(executable))throw new FileNotFoundException("插件宿主不存在",executable);
  var files=Enumerate(source);string parent=Path.Combine(profileDirectory,"plugins");Directory.CreateDirectory(parent);string target=Path.Combine(parent,manifest.Id);if(Directory.Exists(target))throw new InvalidOperationException("此插件已安装，请先停止并移除旧插件目录。");
  string stage=Path.Combine(parent,".install-"+Guid.NewGuid().ToString("N"));
  try{Directory.CreateDirectory(stage);foreach(var path in files){string destination=Path.Combine(stage,Path.GetRelativePath(source,path));Directory.CreateDirectory(Path.GetDirectoryName(destination)!);File.Copy(path,destination);}Directory.Move(stage,target);return manifest;}
  finally{if(Directory.Exists(stage))Directory.Delete(stage,true);}
 }
}
