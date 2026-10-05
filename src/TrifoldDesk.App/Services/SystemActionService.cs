using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace TrifoldDesk.Services;
public sealed class SystemActionService : IDisposable
{
    // Invoked on the WPF UI thread so setting and clearing use the same thread.
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    public bool IsAwake { get; private set; }
    public void SetAwake(bool enabled)
    {
        if (Environment.CurrentManagedThreadId != _ownerThread) throw new InvalidOperationException("常亮设置必须在创建服务的线程执行。");
        uint flags = 0x80000000u | (enabled ? 3u : 0u);
        if (SetThreadExecutionState(flags) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        IsAwake = enabled;
    }
    public static void EmptyRecycleBin()
    {
        int result = SHEmptyRecycleBin(IntPtr.Zero, null, 7);
        if (result < 0) Marshal.ThrowExceptionForHR(result);
    }
    public static void SetAutoStart(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
        if (enabled)
        {
            string executable = Environment.ProcessPath ?? throw new InvalidOperationException("无法确定程序路径。");
            if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("请从发布目录的 TrifoldDesk.exe 启动后再启用开机自启。");
            string command=AutoStartCommand(executable);
            key.SetValue("TrifoldDesk", command);
            if(key.GetValue("TrifoldDesk") as string!=command)throw new IOException("自启动注册未成功，请重试。");
        }
        else key.DeleteValue("TrifoldDesk", false);
    }
    public static string AutoStartCommand(string executable)
    {
        string? root=Directory.GetParent(Path.GetDirectoryName(executable)!)?.Parent?.FullName;
        string? launcher=root==null?null:Path.Combine(root,"AutoStart.vbs");
        return launcher!=null&&File.Exists(launcher)&&File.Exists(Path.Combine(root!,"current-version.txt"))
            ? "\""+Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"wscript.exe")+"\" \""+launcher+"\""
            : "\""+executable+"\"";
    }
    public static bool AutoStartRegistered()
    {
        using var key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        return !string.IsNullOrWhiteSpace(key?.GetValue("TrifoldDesk") as string);
    }
    public void Dispose() { if (IsAwake) SetAwake(false); }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern uint SetThreadExecutionState(uint state);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? root, uint flags);
}
