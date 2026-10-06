using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace TrifoldDesk.Services;

public sealed record DesktopHostStatus(bool IsAttached, bool IsFallback, string Message, IntPtr HostHandle);

/// <summary>Best-effort Explorer adapter. WorkerW discovery is an undocumented shell contract.</summary>
public static class DesktopHostService
{
    private const int StyleIndex = -16, ExtendedStyleIndex = -20, OwnerIndex = -8;
    private const long ChildStyle = 0x40000000L, PopupStyle = 0x80000000L, LayeredStyle = 0x80000L;
    private static readonly ConditionalWeakTable<Window, NativeState> States = new();
    private sealed class NativeState
    {
        public IntPtr Hwnd, Parent, Owner, Host;
        public long Style, ExtendedStyle;
        public RECT InitialRect;
    }

    public static DesktopHostStatus Attach(Window window)
    {
        window.Dispatcher.VerifyAccess();
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero || !IsWindow(hwnd)) return new(false, true, "窗口句柄尚不可用；保留底层窗口模式。", IntPtr.Zero);
        var state = States.GetValue(window, _ => Capture(hwnd));
        if (state.Hwnd != hwnd) { States.Remove(window); state = States.GetValue(window, _ => Capture(hwnd)); }
        if (state.Host != IntPtr.Zero && IsWindow(state.Host) && GetParent(hwnd) == state.Host)
            return new(true, false, "已挂载Explorer桌面宿主（内部接口）。", state.Host);
        try
        {
            Restore(state);
            var host = DiscoverWorker();
            if (host == IntPtr.Zero) return Fallback(state, "未找到兼容的Explorer桌面宿主");
            if (!DpiCompatible(hwnd, host)) return Fallback(state, "桌面宿主与应用的DPI模式不兼容");
            if ((state.ExtendedStyle & LayeredStyle) != 0 && !OperatingSystem.IsWindowsVersionAtLeast(6, 2)) return Fallback(state, "系统不支持透明的子窗口");
            if (!GetWindowRect(hwnd, out var rect)) throw new Win32Exception(Marshal.GetLastWin32Error());
            SetNativeLong(hwnd, StyleIndex, (state.Style | ChildStyle) & ~PopupStyle);
            ClearLastError(0); var previous = SetParent(hwnd, host); int error = Marshal.GetLastWin32Error();
            if (previous == IntPtr.Zero && error != 0 || GetParent(hwnd) != host) throw new Win32Exception(error == 0 ? 5 : error);
            var point = new POINT { X = rect.Left, Y = rect.Top }; MapWindowPoints(IntPtr.Zero, host, ref point, 1);
            if (!SetWindowPos(hwnd, new IntPtr(1), point.X, point.Y, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top), 0x10 | 0x20)) throw new Win32Exception(Marshal.GetLastWin32Error());
            state.Host = host;
            return new(true, false, "已挂载Explorer桌面宿主（内部接口；系统更新可能改变兼容性）。", host);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or EntryPointNotFoundException or DllNotFoundException)
        { return Fallback(state, "桌面宿主挂载失败：" + ex.Message); }
    }

    public static DesktopHostStatus EnsureAttached(Window window)
    {
        window.Dispatcher.VerifyAccess();
        if (States.TryGetValue(window, out var state) && IsWindow(state.Hwnd) && state.Host != IntPtr.Zero && IsWindow(state.Host) && GetParent(state.Hwnd) == state.Host)
            return new(true, false, "Explorer桌面宿主连接正常。", state.Host);
        // Call after Explorer's TaskbarCreated message or when an existing parent disappears.
        return Attach(window);
    }

    public static DesktopHostStatus Detach(Window window)
    {
        window.Dispatcher.VerifyAccess();
        if (!States.TryGetValue(window, out var state)) return new(false, false, "窗口未挂载桌面宿主。", IntPtr.Zero);
        try { Restore(state); States.Remove(window); return new(false, false, "已还原窗口的原始父级与样式。", IntPtr.Zero); }
        catch (Win32Exception ex) { return new(false, true, "窗口还原失败：" + ex.Message, state.Host); }
    }

    private static NativeState Capture(IntPtr hwnd)
    {
        long style = GetWindowLongPtr(hwnd, StyleIndex).ToInt64(); GetWindowRect(hwnd, out var rect);
        return new() { Hwnd = hwnd, Style = style, ExtendedStyle = GetWindowLongPtr(hwnd, ExtendedStyleIndex).ToInt64(), Parent = (style & ChildStyle) != 0 ? GetParent(hwnd) : IntPtr.Zero, Owner = GetWindow(hwnd, 4), InitialRect = rect };
    }
    private static void Restore(NativeState state)
    {
        if (!IsWindow(state.Hwnd)) { state.Host = IntPtr.Zero; return; }
        if (!GetWindowRect(state.Hwnd, out var rect)) rect = state.InitialRect;
        var parent = state.Parent != IntPtr.Zero && IsWindow(state.Parent) ? state.Parent : IntPtr.Zero;
        ClearLastError(0); var old = SetParent(state.Hwnd, parent); int error = Marshal.GetLastWin32Error();
        if (old == IntPtr.Zero && error != 0) throw new Win32Exception(error);
        SetNativeLong(state.Hwnd, StyleIndex, state.Style); SetNativeLong(state.Hwnd, ExtendedStyleIndex, state.ExtendedStyle);
        if ((state.Style & ChildStyle) == 0) SetNativeLong(state.Hwnd, OwnerIndex, state.Owner != IntPtr.Zero && IsWindow(state.Owner) ? state.Owner.ToInt64() : 0);
        var point = new POINT { X = rect.Left, Y = rect.Top }; if (parent != IntPtr.Zero) MapWindowPoints(IntPtr.Zero, parent, ref point, 1);
        if (!SetWindowPos(state.Hwnd, IntPtr.Zero, point.X, point.Y, Math.Max(1, rect.Right - rect.Left), Math.Max(1, rect.Bottom - rect.Top), 0x04 | 0x10 | 0x20)) throw new Win32Exception(Marshal.GetLastWin32Error());
        state.Host = IntPtr.Zero;
    }
    private static DesktopHostStatus Fallback(NativeState state, string reason)
    {
        try
        {
            Restore(state);
            if (IsWindow(state.Hwnd)) SetWindowPos(state.Hwnd, new IntPtr(1), 0, 0, 0, 0, 0x01 | 0x02 | 0x10);
            return new(false, true, reason + "；已回退到底层窗口模式。", IntPtr.Zero);
        }
        catch (Win32Exception ex) { return new(false, true, reason + "；窗口样式还原失败：" + ex.Message, state.Host); }
    }
    private static bool DpiCompatible(IntPtr hwnd, IntPtr host)
    {
        var a = GetWindowDpiAwarenessContext(hwnd); var b = GetWindowDpiAwarenessContext(host);
        return a != IntPtr.Zero && b != IntPtr.Zero && AreDpiAwarenessContextsEqual(a, b);
    }
    private static IntPtr DiscoverWorker()
    {
        var worker = FindWorker(); if (worker != IntPtr.Zero) return worker;
        var progman = FindWindow("Progman", null); if (progman == IntPtr.Zero) return IntPtr.Zero;
        // Private Explorer messages: bounded calls, followed by checking the actual resulting window tree.
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0x02, 500, out _);
        worker = FindWorker(); if (worker != IntPtr.Zero) return worker;
        SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), IntPtr.Zero, 0x02, 500, out _);
        SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(1), 0x02, 500, out _);
        return FindWorker();
    }
    private static IntPtr FindWorker()
    {
        IntPtr result = IntPtr.Zero;
        EnumWindows((hwnd, _) => {
            if (FindWindowEx(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null) == IntPtr.Zero) return true;
            var candidate = FindWindowEx(IntPtr.Zero, hwnd, "WorkerW", null);
            if (candidate == IntPtr.Zero || FindWindowEx(candidate, IntPtr.Zero, "SHELLDLL_DefView", null) != IntPtr.Zero || !IsWindowVisible(candidate)) return true;
            result = candidate; return false;
        }, IntPtr.Zero);
        return result;
    }
    private static void SetNativeLong(IntPtr hwnd, int index, long value)
    {
        ClearLastError(0); var previous = SetWindowLongPtr(hwnd, index, new IntPtr(value)); int error = Marshal.GetLastWin32Error();
        if (previous == IntPtr.Zero && error != 0) throw new Win32Exception(error);
    }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr parameter);
    [DllImport("kernel32.dll", EntryPoint = "SetLastError")] private static extern void ClearLastError(uint error);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetParent(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
    [DllImport("user32.dll", SetLastError = true)] private static extern IntPtr SetParent(IntPtr hwnd, IntPtr parent);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rectangle);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern int MapWindowPoints(IntPtr from, IntPtr to, ref POINT point, uint count);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] private static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string className, string? name);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr SendMessageTimeout(IntPtr hwnd, uint message, IntPtr wparam, IntPtr lparam, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] private static extern IntPtr GetWindowDpiAwarenessContext(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool AreDpiAwarenessContextsEqual(IntPtr first, IntPtr second);
}
