// Lens profile adapted from GlassDesk 1.4.0 and Kyant0/AndroidLiquidGlass.
// Copyright 2026 everlosttime and GlassDesk contributors. Copyright 2025 Kyant.
// Apache-2.0. See ThirdParty/DockGlass.
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace TrifoldDesk.Services;

internal static class DockGlassRenderer
{
    private static readonly object CaptureGate = new();
    private static Drawing.Bitmap? _widgetFrame;
    private static Drawing.Rectangle _widgetFrameBounds;
    private static nint _widgetFrameOwner;
    private static int _widgetFrameRevision;
    private static long _widgetFrameExpiresAt;
    private const uint ExcludeFromCapture = 0x11;
    private const uint PreviousWindow = 3;
    private static readonly uint ProcessId = (uint)Environment.ProcessId;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left, Top, Right, Bottom;
    }

    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(nint window, uint affinity);
    [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(nint window, out uint affinity);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out NativeRect rect);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint processId);
    [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(nint destination, int x, int y, int width, int height,
        nint source, int sourceX, int sourceY, uint operation);

    public static BitmapSource? Render(nint window, Drawing.Rectangle area, double radius, double scale,
        bool launchpad = false, bool lighterPanel = false, Drawing.Rectangle? widgetHostBounds = null)
    {
        if (!GlassCapturePolicy.Enabled) return null;
        const double lensStrength = 3;
        int step = Math.Max(1, (int)Math.Round(2 * scale));
        int padding = launchpad ? 0 : step + (int)Math.Ceiling(lensStrength * scale) + 1;
        var capture = area;
        capture.Inflate(padding, padding);
        var visible = Drawing.Rectangle.Intersect(capture, System.Windows.Forms.SystemInformation.VirtualScreen);
        if (visible.IsEmpty) return null;

        using var bitmap = new Drawing.Bitmap(capture.Width, capture.Height, DrawingPixelFormat.Format32bppRgb);
        lock (CaptureGate)
        {
            var source = GlassCapturePolicy.Source;
            if (source == GlassCaptureSource.Disabled) return null;
            var shared = widgetHostBounds is { } host
                ? Drawing.Rectangle.Intersect(host, System.Windows.Forms.SystemInformation.VirtualScreen)
                : Drawing.Rectangle.Empty;
            bool captured = source == GlassCaptureSource.ScreenComposite && shared.Contains(visible)
                ? CopySharedWidgetFrame(window, bitmap, capture, visible, shared)
                : CaptureBackdrop(source, window, bitmap, capture, visible);
            if (!captured) return null;
        }
        if (launchpad)
            return RenderPanel(bitmap, radius / scale, scale, lighterPanel);

        int stride = capture.Width * 4;
        var pixels = new byte[stride * capture.Height];
        var data = bitmap.LockBits(new Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppRgb);
        try
        {
            for (int y = 0; y < bitmap.Height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * stride, stride);
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        // Some capture drivers return a black region for excluded windows.
        bool hasVisiblePixels = false;
        for (int y = 0; y < area.Height && !hasVisiblePixels; y++)
        for (int x = 0; x < area.Width; x++)
        {
            int i = (y + padding) * stride + (x + padding) * 4;
            if ((pixels[i] | pixels[i + 1] | pixels[i + 2]) == 0) continue;
            hasVisiblePixels = true;
            break;
        }
        if (!hasVisiblePixels) return null;

        var result = new byte[area.Width * area.Height * 4];
        double halfWidth = area.Width / 2.0;
        double halfHeight = area.Height / 2.0;
        radius = Math.Min(radius, Math.Min(halfWidth, halfHeight));
        double band = 9 * scale;
        for (int y = 0; y < area.Height; y++)
        for (int x = 0; x < area.Width; x++)
        {
            double cx = x + .5 - halfWidth;
            double cy = y + .5 - halfHeight;
            double qx = Math.Abs(cx) - halfWidth + radius;
            double qy = Math.Abs(cy) - halfHeight + radius;
            double nx = Math.Max(qx, 0);
            double ny = Math.Max(qy, 0);
            double length = Math.Sqrt(nx * nx + ny * ny);
            double depth = radius - length - Math.Min(Math.Max(qx, qy), 0);
            double t = Math.Clamp(1 - depth / band, 0, 1);
            double offset = -(1 - Math.Sqrt(1 - t * t)) * (lensStrength * scale);
            double normalX = length > 0 ? nx / length : qx > qy ? 1 : 0;
            double normalY = length > 0 ? ny / length : qx > qy ? 0 : 1;
            int sx = x + padding + (int)Math.Round(normalX * Math.Sign(cx) * offset);
            int sy = y + padding + (int)Math.Round(normalY * Math.Sign(cy) * offset);
            int index = (y * area.Width + x) * 4;
            for (int channel = 0; channel < 3; channel++)
            {
                int center = sy * stride + sx * 4 + channel;
                int dx = step * 4;
                int dy = step * stride;
                int sum = 4 * pixels[center]
                    + 2 * (pixels[center - dx] + pixels[center + dx] + pixels[center - dy] + pixels[center + dy])
                    + pixels[center - dy - dx] + pixels[center - dy + dx]
                    + pixels[center + dy - dx] + pixels[center + dy + dx];
                result[index + channel] = (byte)(sum / 16);
            }
        }

        var frame = BitmapSource.Create(area.Width, area.Height, 96, 96, PixelFormats.Bgr32, null, result, area.Width * 4);
        frame.Freeze();
        return frame;
    }
    private static bool CaptureBackdrop(GlassCaptureSource source, nint window, Drawing.Bitmap bitmap,
        Drawing.Rectangle capture, Drawing.Rectangle visible) => source switch
    {
        GlassCaptureSource.DesktopOnly => DesktopGlassBackdrop.TryCapture(bitmap, capture, visible),
        GlassCaptureSource.ScreenComposite => CaptureComposite(window, bitmap, capture, visible),
        _ => false
    };

    // Widgets in the same desktop window can crop the same capture without excluding
    // their shared HWND once per card. This cache is only accessed inside CaptureGate.
    private static bool CopySharedWidgetFrame(nint window, Drawing.Bitmap bitmap, Drawing.Rectangle capture,
        Drawing.Rectangle visible, Drawing.Rectangle shared)
    {
        int revision = GlassCapturePolicy.Revision;
        if (_widgetFrameOwner != window || _widgetFrameBounds != shared ||
            _widgetFrameRevision != revision || Environment.TickCount64 >= _widgetFrameExpiresAt)
        {
            _widgetFrame?.Dispose();
            _widgetFrame = null;
            _widgetFrameOwner = window;
            _widgetFrameBounds = shared;
            _widgetFrameRevision = revision;
            var frame = new Drawing.Bitmap(shared.Width, shared.Height, DrawingPixelFormat.Format32bppRgb);
            try
            {
                if (GlassCapturePolicy.Source == GlassCaptureSource.ScreenComposite &&
                    CaptureComposite(window, frame, shared, shared))
                    _widgetFrame = frame;
            }
            finally
            {
                if (_widgetFrame != frame) frame.Dispose();
                _widgetFrameExpiresAt = Environment.TickCount64 + 250;
            }
        }
        if (_widgetFrame is null) return false;

        using var graphics = Drawing.Graphics.FromImage(bitmap);
        var input = new Drawing.Rectangle(visible.X - shared.X, visible.Y - shared.Y, visible.Width, visible.Height);
        var output = new Drawing.Rectangle(visible.X - capture.X, visible.Y - capture.Y, visible.Width, visible.Height);
        graphics.DrawImage(_widgetFrame, output, input, Drawing.GraphicsUnit.Pixel);
        return true;
    }

    private static bool CaptureComposite(nint window, Drawing.Bitmap bitmap, Drawing.Rectangle capture, Drawing.Rectangle visible)
    {
        if (window == 0 || GlassCapturePolicy.Source != GlassCaptureSource.ScreenComposite) return false;
        var exclusions = new List<(nint Window, uint Affinity)>();

        bool Exclude(nint hwnd)
        {
            if (!GetWindowDisplayAffinity(hwnd, out uint previous)) return false;
            if (previous == ExcludeFromCapture) return true;
            if (!SetWindowDisplayAffinity(hwnd, ExcludeFromCapture)) return false;
            exclusions.Add((hwnd, previous));
            return true;
        }

        try
        {
            if (!Exclude(window)) return false;

            // A foreground TrifoldDesk window cannot be part of the glass behind it.
            // Including it would feed that glass back into its own next frame.
            var visited = new HashSet<nint> { window };
            for (nint above = GetWindow(window, PreviousWindow); above != 0 && visited.Add(above);
                above = GetWindow(above, PreviousWindow))
            {
                if (!IsWindowVisible(above) || GetWindowThreadProcessId(above, out uint owner) == 0 ||
                    owner != ProcessId || !GetWindowRect(above, out var rect)) continue;
                var bounds = Drawing.Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                if (bounds.IntersectsWith(visible)) Exclude(above);
            }

            return DwmFlush() >= 0 && CopyDesktop(bitmap, capture, visible);
        }
        finally
        {
            for (int i = exclusions.Count - 1; i >= 0; i--)
                SetWindowDisplayAffinity(exclusions[i].Window, exclusions[i].Affinity);
        }
    }

    private static bool CopyDesktop(Drawing.Bitmap bitmap, Drawing.Rectangle capture, Drawing.Rectangle visible)
    {
        const uint sourceCopyWithLayeredWindows = 0x40CC0020;
        using var graphics = Drawing.Graphics.FromImage(bitmap);
        nint screen = GetDC(0);
        if (screen == 0) return false;
        try
        {
            nint target = graphics.GetHdc();
            try
            {
                return BitBlt(target, visible.X - capture.X, visible.Y - capture.Y, visible.Width, visible.Height,
                    screen, visible.X, visible.Y, sourceCopyWithLayeredWindows);
            }
            finally
            {
                graphics.ReleaseHdc(target);
            }
        }
        finally
        {
            ReleaseDC(0, screen);
        }
    }

    private static BitmapSource? RenderPanel(Drawing.Bitmap capture, double radius, double scale, bool lighterPanel)
    {
        // GlassDesk limits the sampling map so the first frame stays responsive on high-DPI screens.
        double ratio = Math.Min(1, Math.Sqrt(650_000.0 / ((double)capture.Width * capture.Height)));
        int width = Math.Max(1, (int)Math.Round(capture.Width * ratio));
        int height = Math.Max(1, (int)Math.Round(capture.Height * ratio));
        using var sample = new Drawing.Bitmap(width, height, DrawingPixelFormat.Format32bppRgb);
        using (var graphics = Drawing.Graphics.FromImage(sample))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
            graphics.DrawImage(capture, new Drawing.Rectangle(0, 0, width, height));
        }

        int stride = width * 4;
        var pixels = new byte[stride * height];
        var data = sample.LockBits(new Drawing.Rectangle(0, 0, width, height),
            ImageLockMode.ReadOnly, DrawingPixelFormat.Format32bppRgb);
        try
        {
            for (int y = 0; y < height; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * stride, stride);
        }
        finally
        {
            sample.UnlockBits(data);
        }
        bool hasScene = false;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            hasScene |= (pixels[i] | pixels[i + 1] | pixels[i + 2]) != 0;
            pixels[i + 3] = 255;
        }
        if (!hasScene) return null;

        var size = new Size(capture.Width / scale, capture.Height / scale);
        var glass = new GlassOptics.AndroidLiquidGlassRenderer();
        var rendered = glass.Render(pixels, width, height, size, new Rect(size), radius,
            squircle: true, dark: false, strength: lighterPanel ? .68 : 1, tintOpacity: lighterPanel ? .035 : .08,
            backgroundClarity: lighterPanel ? .71 : .62, launchpadOptics: true);
        var frame = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, rendered, stride);
        frame.Freeze();
        return frame;
    }
}
