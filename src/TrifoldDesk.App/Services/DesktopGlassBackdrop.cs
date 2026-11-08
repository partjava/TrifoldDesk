using System.Runtime.InteropServices;
using System.Text;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace TrifoldDesk.Services;

internal static class DesktopGlassBackdrop
{
    private const uint WmPrint = 0x0317;
    private const uint PrintClientAndBackground = 0x0004 | 0x0008;
    private const uint RenderFullContent = 0x0002;
    private const uint AbortIfHung = 0x0002;
    private const uint ParentAncestor = 1;
    private static readonly object Gate = new();
    private static readonly Dictionary<Source, Frame> Frames = [];

    private readonly record struct Source(nint Window, Drawing.Rectangle Bounds, bool LiveWallpaper);

    private sealed class Frame
    {
        public Drawing.Bitmap? Image;
        public Task<Drawing.Bitmap?>? Pending;
        public long LastRequest = -1000;
        public long LastFrame;
        public long LastUse;
        public WallpaperWindowCapture? GpuCapture;
    }

    public static bool TryCapture(Drawing.Bitmap destination, Drawing.Rectangle capture, Drawing.Rectangle visible)
    {
        lock (Gate)
        {
            var shell = GetShellWindow();
            if (shell == 0 || GetWindowThreadProcessId(shell, out uint shellProcess) == 0) return false;
            var hosts = FindWallpaperHosts(shell, shellProcess);
            var screens = Forms.Screen.AllScreens;
            long now = Environment.TickCount64;
            RemoveUnusedFrames(now);
            using var graphics = Drawing.Graphics.FromImage(destination);
            bool drewBackdrop = false;
            foreach (var screen in screens)
            {
                var slice = Drawing.Rectangle.Intersect(screen.Bounds, visible);
                if (slice.Width <= 0 || slice.Height <= 0) continue;

                var provider = FindLiveWallpaper(hosts, screen.Bounds, shellProcess);
                var target = new Drawing.Rectangle(slice.X - capture.X, slice.Y - capture.Y, slice.Width, slice.Height);
                if (provider is Source live)
                {
                    // Explorer may still be painting the old wallpaper behind an animated wallpaper.
                    if (!DrawSource(graphics, target, slice, live, now)) return false;
                }
                else
                {
                    bool found = false;
                    foreach (var host in hosts)
                    {
                        if (!TryGetClientBounds(host, out var bounds) || !bounds.Contains(slice)) continue;
                        if (!DrawSource(graphics, target, slice, new Source(host, bounds, false), now)) continue;
                        found = true;
                        break;
                    }
                    if (!found) return false;
                }
                drewBackdrop = true;
            }
            return drewBackdrop;
        }
    }

    private static List<nint> FindWallpaperHosts(nint shell, uint shellProcess)
    {
        var hosts = new List<nint>();
        bool AddWorker(nint candidate, nint unused)
        {
            if (candidate != shell && IsWindowVisible(candidate)
                && GetWindowThreadProcessId(candidate, out uint process) != 0
                && process == shellProcess && HasClass(candidate, "WorkerW"))
                hosts.Add(candidate);
            return true;
        }
        EnumWindows(AddWorker, 0);
        EnumChildWindows(shell, AddWorker, 0);
        hosts.Add(shell);
        return hosts;
    }

    private static Source? FindLiveWallpaper(List<nint> hosts, Drawing.Rectangle screen, uint shellProcess)
    {
        foreach (var host in hosts)
        {
            Source? match = null;
            EnumChildWindows(host, (child, _) =>
            {
                if (GetAncestor(child, ParentAncestor) != host || !IsWindowVisible(child)
                    || GetWindowThreadProcessId(child, out uint process) == 0
                    || process == shellProcess || process == (uint)Environment.ProcessId
                    || !GetWindowRect(child, out var rect) || !rect.Bounds.Contains(screen))
                    return true;
                // WGC accepts top-level windows only. Capturing Explorer's root
                // returns its wallpaper surface, without ordinary overlaid apps.
                nint root = GetAncestor(child, 2);
                if (root == 0 || !GetWindowRect(root, out var rootRect)) return true;
                match = new Source(root, rootRect.Bounds, true);
                return false;
            }, 0);
            if (match is not null) return match;
        }
        return null;
    }

    private static bool DrawSource(Drawing.Graphics graphics, Drawing.Rectangle target, Drawing.Rectangle slice,
        Source source, long now)
    {
        if ((long)source.Bounds.Width * source.Bounds.Height > 34_000_000) return false;
        if (!Frames.TryGetValue(source, out var frame))
        {
            if (Frames.Count >= 8) return false;
            frame = new Frame();
            Frames.Add(source, frame);
        }
        frame.LastUse = now;
        if (source.LiveWallpaper)
        {
            try
            {
                if (frame.GpuCapture is null)
                {
                    if (now - frame.LastRequest < 1000) return false;
                    frame.LastRequest = now;
                    frame.GpuCapture = new WallpaperWindowCapture(source.Window);
                }
                using var image = frame.GpuCapture.CopyLatest();
                if (image is null) return false;
                var input = new Drawing.Rectangle(slice.X - source.Bounds.X, slice.Y - source.Bounds.Y,
                    slice.Width, slice.Height);
                graphics.DrawImage(image, target, input, Drawing.GraphicsUnit.Pixel);
                return true;
            }
            catch (Exception error) when (error is ExternalException or InvalidOperationException or NotSupportedException or ArgumentException)
            {
                System.Diagnostics.Debug.WriteLine(error);
                return false;
            }
        }
        FinishCapture(frame);
        if (frame.Pending is null && now - frame.LastRequest >= (frame.Image is null ? 1000 : 250))
        {
            frame.LastRequest = now;
            // PrintWindow is synchronous, so only the producer waits on the other process.
            frame.Pending = Task.Run(() => CaptureSource(source));
            FinishCapture(frame);
        }

        if (frame.Image is null || now - frame.LastFrame > 1000) return false;
        var crop = new Drawing.Rectangle(slice.X - source.Bounds.X, slice.Y - source.Bounds.Y, slice.Width, slice.Height);
        graphics.DrawImage(frame.Image, target, crop, Drawing.GraphicsUnit.Pixel);
        return true;
    }

    private static void FinishCapture(Frame frame)
    {
        if (frame.Pending is not { IsCompleted: true } completed) return;
        frame.Image?.Dispose();
        frame.Image = completed.IsCompletedSuccessfully ? completed.Result : null;
        frame.LastFrame = Environment.TickCount64;
        frame.Pending = null;
    }

    private static void RemoveUnusedFrames(long now)
    {
        foreach (var (source, frame) in Frames.ToArray())
        {
            if (now - frame.LastUse <= 3000 || frame.Pending is { IsCompleted: false }) continue;
            FinishCapture(frame);
            frame.Image?.Dispose();
            frame.GpuCapture?.Dispose();
            Frames.Remove(source);
        }
    }

    public static void Reset()
    {
        lock (Gate)
        {
            foreach (var frame in Frames.Values)
            {
                frame.GpuCapture?.Dispose();
                frame.Image?.Dispose();
                if (frame.Pending is { } pending)
                    _ = pending.ContinueWith(task => { if (task.IsCompletedSuccessfully) task.Result?.Dispose(); }, TaskScheduler.Default);
            }
            Frames.Clear();
        }
    }

    private static Drawing.Bitmap? CaptureSource(Source source)
    {
        Drawing.Bitmap? bitmap = null;
        try
        {
            if (GlassCapturePolicy.Source != GlassCaptureSource.DesktopOnly) return null;
            bitmap = new Drawing.Bitmap(source.Bounds.Width, source.Bounds.Height,
                Drawing.Imaging.PixelFormat.Format32bppRgb);
            bool printed;
            using (var graphics = Drawing.Graphics.FromImage(bitmap))
            {
                var dc = graphics.GetHdc();
                try
                {
                    // Do not print Explorer's child windows: they can include TrifoldDesk itself.
                    printed = source.LiveWallpaper
                        ? PrintWindow(source.Window, dc, RenderFullContent)
                        : SendMessageTimeout(source.Window, WmPrint, (nuint)dc, (nint)PrintClientAndBackground,
                            AbortIfHung, 100, out _) != 0;
                }
                finally { graphics.ReleaseHdc(dc); }
            }
            if (!printed || !HasImage(bitmap)) return null;
            var result = bitmap;
            bitmap = null;
            return result;
        }
        catch (Exception error) when (error is ArgumentException or ExternalException or OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine(error);
            return null;
        }
        finally { bitmap?.Dispose(); }
    }

    private static bool HasImage(Drawing.Bitmap bitmap)
    {
        int first = bitmap.GetPixel(0, 0).ToArgb();
        for (int row = 0; row < 8; row++)
        for (int column = 0; column < 8; column++)
        {
            int x = (int)((column + .5) * bitmap.Width / 8);
            int y = (int)((row + .5) * bitmap.Height / 8);
            if (bitmap.GetPixel(x, y).ToArgb() != first) return true;
        }
        return false;
    }

    private static bool TryGetClientBounds(nint window, out Drawing.Rectangle bounds)
    {
        var origin = new NativePoint();
        bounds = Drawing.Rectangle.Empty;
        if (!GetClientRect(window, out var client) || !ClientToScreen(window, ref origin)) return false;
        bounds = new Drawing.Rectangle(origin.X, origin.Y, client.Right, client.Bottom);
        return bounds.Width > 0 && bounds.Height > 0;
    }

    private static bool HasClass(nint window, string expected)
    {
        var name = new StringBuilder(32);
        return GetClassName(window, name, name.Capacity) > 0 && name.ToString() == expected;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
        public readonly Drawing.Rectangle Bounds => Drawing.Rectangle.FromLTRB(Left, Top, Right, Bottom);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X, Y; }

    private delegate bool EnumWindowProc(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern nint GetShellWindow();
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowProc callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(nint window, EnumWindowProc callback, nint parameter);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint window, ref NativePoint point);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder text, int size);
    [DllImport("user32.dll")] private static extern bool PrintWindow(nint window, nint dc, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint SendMessageTimeout(nint window, uint message,
        nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);
}
