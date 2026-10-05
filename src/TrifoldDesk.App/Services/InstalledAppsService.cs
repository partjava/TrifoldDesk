using System.Runtime.InteropServices;
namespace TrifoldDesk.Services;
public sealed record InstalledApp(string Name,string ShellPath)
{
    public string DisplayName {get;init;}=Name;
    public string TargetPath {get;init;}="";
    public string Arguments {get;init;}="";
    public string Details=>DisplayName+"\n"+(string.IsNullOrEmpty(TargetPath)?ShellPath:TargetPath)+(string.IsNullOrEmpty(Arguments)?"":"\n"+Arguments);
}
public static class InstalledAppsService
{
    public static IReadOnlyList<InstalledApp> Read()
    {
        var result=new List<InstalledApp>(); object? shell=null,folder=null,items=null;
        try
        {
            shell=Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            folder=((dynamic)shell!).NameSpace("shell:AppsFolder"); items=((dynamic)folder!).Items();
            foreach(object item in (dynamic)items)
            {
                try
                {
                    string name=((dynamic)item).Name,path=((dynamic)item).Path;
                    string Property(string key) {try {return Convert.ToString(((dynamic)item).ExtendedProperty(key))??"";}catch(COMException){return "";} }
                    if(!string.IsNullOrWhiteSpace(name)&&!string.IsNullOrWhiteSpace(path))result.Add(new(name,path){TargetPath=Property("System.Link.TargetParsingPath"),Arguments=Property("System.Link.Arguments")});
                }
                finally { if(Marshal.IsComObject(item))Marshal.FinalReleaseComObject(item); }
            }
        }
        finally { foreach(var value in new[]{items,folder,shell})if(value!=null&&Marshal.IsComObject(value))Marshal.FinalReleaseComObject(value); }
        return Disambiguate(result);
    }
    public static IReadOnlyList<InstalledApp> Disambiguate(IEnumerable<InstalledApp> source)
    {
        var result=new List<InstalledApp>();
        foreach(var group in source.DistinctBy(a=>a.ShellPath,StringComparer.OrdinalIgnoreCase).GroupBy(a=>a.Name,StringComparer.CurrentCultureIgnoreCase))
        {
            var apps=group.ToArray();
            if(apps.Length==1){result.Add(apps[0]);continue;}
            string Label(InstalledApp app)
            {
                var parts=app.ShellPath.Split('.');
                if(parts.Length>=4&&parts[0].Equals("anaconda",StringComparison.OrdinalIgnoreCase))return parts[^2];
                if(Path.IsPathFullyQualified(app.TargetPath))return Path.GetFileName(Path.GetDirectoryName(app.TargetPath))??app.ShellPath;
                return app.ShellPath;
            }
            var labels=apps.Select(Label).ToArray();
            for(int n=0;n<apps.Length;n++)
            {
                string label=labels.Count(s=>s.Equals(labels[n],StringComparison.OrdinalIgnoreCase))>1?apps[n].ShellPath:labels[n];
                result.Add(apps[n] with {DisplayName=apps[n].Name+"（"+label+"）"});
            }
        }
        return result.OrderBy(a=>a.DisplayName,StringComparer.CurrentCultureIgnoreCase).ToArray();
    }
}
