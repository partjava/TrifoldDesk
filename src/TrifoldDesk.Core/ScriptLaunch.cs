using System.Text;
namespace TrifoldDesk.Core;

public sealed record ScriptLaunchPlan(string FileName, bool UseShellExecute, string WorkingDirectory, IReadOnlyList<string> ArgumentList);
public static class ScriptLaunchRules
{
    // Building a plan only validates metadata. Import and migration never launch processes.
    public static ScriptLaunchPlan Create(ShortcutItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        string source=Environment.ExpandEnvironmentVariables(item.SourcePath ?? "");
        if(!File.Exists(source)&&!Directory.Exists(source))throw new FileNotFoundException("入口已失效，请重新定位。",source);
        string mode=string.IsNullOrWhiteSpace(item.LaunchMode)?"shell":item.LaunchMode;
        if(mode=="shell")return new(source,true,"",Array.Empty<string>());
        if(mode is not ("python" or "powershell" or "program"))throw new ArgumentException("不支持的启动方式。",nameof(item));
        string executable=Environment.ExpandEnvironmentVariables(item.ExecutablePath ?? "");
        if(!Path.IsPathFullyQualified(executable)||!File.Exists(executable))throw new FileNotFoundException("请指定存在的解释器或程序完整路径。",executable);
        if(mode=="python"&&!Path.GetExtension(source).Equals(".py",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Python 模式需要 .py 脚本。");
        if(mode=="powershell"&&!Path.GetExtension(source).Equals(".ps1",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("PowerShell 模式需要 .ps1 脚本。");
        string directory=Environment.ExpandEnvironmentVariables(item.WorkingDirectory ?? "");
        if(string.IsNullOrWhiteSpace(directory))directory=Path.GetDirectoryName(Path.GetFullPath(source)) ?? "";
        if(!Path.IsPathFullyQualified(directory)||!Directory.Exists(directory))throw new DirectoryNotFoundException("工作目录不存在，请重新选择。");
        var arguments=new List<string>();
        if(mode=="python")arguments.Add(Path.GetFullPath(source));
        if(mode=="powershell")arguments.AddRange(["-NoProfile","-File",Path.GetFullPath(source)]);
        arguments.AddRange(ParseArguments(item.Arguments ?? ""));
        return new(Path.GetFullPath(executable),false,Path.GetFullPath(directory),arguments.AsReadOnly());
    }
    // Windows double-quote/backslash rules. Characters such as &, | and $ stay literal argv.
    private static IReadOnlyList<string> ParseArguments(string input)
    {
        if(input.Contains('\0'))throw new ArgumentException("参数不能包含空字符。");
        var result=new List<string>();int index=0;
        while(index<input.Length)
        {
            while(index<input.Length&&char.IsWhiteSpace(input[index]))index++;
            if(index==input.Length)break;
            var value=new StringBuilder();bool quoted=false;
            while(index<input.Length&&(quoted||!char.IsWhiteSpace(input[index])))
            {
                int slashes=0;
                while(index<input.Length&&input[index]=='\\'){slashes++;index++;}
                if(index<input.Length&&input[index]=='"')
                {
                    value.Append('\\',slashes/2);
                    if(slashes%2==1)value.Append('"');else quoted=!quoted;
                    index++;
                }
                else
                {
                    value.Append('\\',slashes);
                    if(index<input.Length&&(quoted||!char.IsWhiteSpace(input[index])))value.Append(input[index++]);
                }
            }
            if(quoted)throw new ArgumentException("参数中的双引号未闭合。");
            result.Add(value.ToString());
        }
        return result;
    }
}
