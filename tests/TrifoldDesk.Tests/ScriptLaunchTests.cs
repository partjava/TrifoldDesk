using TrifoldDesk.Core;
public static class ScriptLaunchTests
{
    public static void Run(Action<bool,string> check)
    {
        string root=Path.Combine(Environment.CurrentDirectory,".test-data","script-launch-"+Guid.NewGuid());Directory.CreateDirectory(root);
        try
        {
            string interpreter=Path.Combine(root,"解释器 with space.exe"),python=Path.Combine(root,"脚本 with space.py"),powershell=Path.Combine(root,"脚本 with space.ps1");
            foreach(string file in new[]{interpreter,python,powershell})File.WriteAllText(file,"test fixture, never executed");
            var item=new ShortcutItem{SourcePath=python,LaunchMode="python",ExecutablePath=interpreter,Arguments="--name \"中文 空格\" \"\" & literal",WorkingDirectory=root};
            var plan=ScriptLaunchRules.Create(item);
            check(!plan.UseShellExecute&&plan.FileName==interpreter&&plan.WorkingDirectory==root,"script launch uses explicit interpreter and working directory");
            check(plan.ArgumentList.SequenceEqual(new[]{python,"--name","中文 空格","","&","literal"}),"quoted Unicode, empty arguments and shell metacharacters remain distinct literal arguments");
            item.LaunchMode="powershell";item.SourcePath=powershell;item.Arguments="-Command literal";
            plan=ScriptLaunchRules.Create(item);
            check(plan.ArgumentList.SequenceEqual(new[]{"-NoProfile","-File",powershell,"-Command","literal"}),"PowerShell always selects script file mode without policy bypass");
            item.LaunchMode="program";item.Arguments="\"a\\\"b\" C:\\资料\\file.txt";
            plan=ScriptLaunchRules.Create(item);
            check(plan.ArgumentList.SequenceEqual(new[]{"a\"b",@"C:\资料\file.txt"}),"specified program preserves escaped quotes and backslashes");
            item.LaunchMode="shell";item.SourcePath=python;item.Arguments="ignored";item.ExecutablePath="missing.exe";
            plan=ScriptLaunchRules.Create(item);check(plan.UseShellExecute&&plan.FileName==python&&plan.ArgumentList.Count==0,"default Shell launch preserves source ownership of shortcut arguments");
            item.LaunchMode="python";bool denied=false;try{ScriptLaunchRules.Create(item);}catch(IOException){denied=true;}check(denied,"missing explicit interpreter is rejected");
            item.ExecutablePath=interpreter;item.Arguments="\"unterminated";denied=false;try{ScriptLaunchRules.Create(item);}catch(ArgumentException){denied=true;}check(denied,"unclosed quoted arguments are rejected");
            item.Arguments="";item.WorkingDirectory=Path.Combine(root,"missing");denied=false;try{ScriptLaunchRules.Create(item);}catch(IOException){denied=true;}check(denied,"missing working directory is rejected");
            item.WorkingDirectory=root;item.LaunchMode="unknown";denied=false;try{ScriptLaunchRules.Create(item);}catch(ArgumentException){denied=true;}check(denied,"unknown launch modes do not silently execute");
            item.LaunchMode="python";item.SourcePath=powershell;denied=false;try{ScriptLaunchRules.Create(item);}catch(ArgumentException){denied=true;}check(denied,"Python mode requires an explicit Python script source");
        }
        finally{Directory.Delete(root,true);}
    }
}
