using System.Runtime.InteropServices;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace TrifoldDesk.Services;
public static class WindowDockService
{
    public static Forms.Screen SelectedScreen(string device) => Forms.Screen.AllScreens.FirstOrDefault(s => s.DeviceName == device) ?? Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
    public static void ApplyToolWindow(Window window)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        long style = GetWindowLongPtr(hwnd, -20).ToInt64();
        SetWindowLongPtr(hwnd, -20, new IntPtr((style | 0x80L) & ~0x40000L));
    }
    public static void Dock(Window panel, Window handle, string device)
    {
        var screen = SelectedScreen(device);
        var area = screen.WorkingArea;
        uint dpiX = 96, dpiY = 96;
        try
        {
            IntPtr monitor = MonitorFromPoint(new POINT(area.Left + area.Width / 2, area.Top + area.Height / 2), 2);
            if (GetDpiForMonitor(monitor, 0, out uint x, out uint y) == 0) { dpiX = x; dpiY = y; }
        }
        catch (DllNotFoundException) { }
        double sx = dpiX / 96d, sy = dpiY / 96d;
        var size = WorkspaceLayout.Expanded(area.Width, area.Height, dpiX);
        double height = area.Height / sy;
        double width = size.Width;
        panel.Width = width; panel.Height = height;
        handle.Width = 16; handle.Height = Math.Min(160, height * .35);
        // SetWindowPos uses physical pixels; WPF Left/Top conversion across mixed-DPI monitors is ambiguous.
        Position(panel, area.Left, area.Top, (int)Math.Round(width * sx), area.Height);
        Position(handle, area.Right - (int)Math.Round(16 * sx), area.Top + (area.Height - (int)Math.Round(handle.Height * sy)) / 2, (int)Math.Round(16 * sx), (int)Math.Round(handle.Height * sy));
    }
    private static void Position(Window window, int x, int y, int width, int height)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero) SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, 0x14); // NOZORDER | NOACTIVATE
    }
    public static void RaiseHandle(Window handle) { if(handle.Topmost)SetWindowPos(new WindowInteropHelper(handle).Handle, new IntPtr(-1),0,0,0,0,0x13);else KeepAtDesktop(handle); }
    public static void KeepAtDesktop(Window window)
    {
        if(window.Topmost)return;
        var hwnd=new WindowInteropHelper(window).Handle;if(hwnd==IntPtr.Zero)return;
        // An owned window stays above its owner: keep the desktop visible behind us.
        var desktop=GetShellWindow();if(desktop!=IntPtr.Zero)SetWindowLongPtr(hwnd,-8,desktop);
        SetWindowPos(hwnd,new IntPtr(1),0,0,0,0,0x13);
    }
    public static void ProtectDesktopOrder(IntPtr position)
    {
        if(position==IntPtr.Zero)return;
        var value=Marshal.PtrToStructure<WINDOWPOS>(position);
        if((value.Flags&4)!=0)return;
        value.After=new IntPtr(1);Marshal.StructureToPtr(value,position,false);
    }
    public static bool IsAbove(Window upper,Window lower)
    {
        var a=new WindowInteropHelper(upper).Handle;var b=new WindowInteropHelper(lower).Handle;
        for(var hwnd=GetTopWindow(IntPtr.Zero);hwnd!=IntPtr.Zero;hwnd=GetWindow(hwnd,2)) {if(hwnd==a)return true;if(hwnd==b)return false;}return false;
    }
    [StructLayout(LayoutKind.Sequential)] private struct WINDOWPOS { public IntPtr Hwnd,After;public int X,Y,Cx,Cy;public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd,uint command);
    public static bool FillsWorkspace(Window panel)
    {
        var hwnd = new WindowInteropHelper(panel).Handle; var area = Forms.Screen.FromHandle(hwnd).WorkingArea;
        return GetWindowRect(hwnd, out var rect) && Math.Abs(rect.Left - area.Left) <= 2 && Math.Abs(rect.Top - area.Top) <= 2 && Math.Abs(rect.Right - (area.Right - 24)) <= 2 && Math.Abs(rect.Bottom - area.Bottom) <= 2;
    }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [StructLayout(LayoutKind.Sequential)] private readonly struct POINT(int x, int y) { public readonly int X = x, Y = y; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint x, out uint y);
}
