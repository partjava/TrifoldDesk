using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Drawing = System.Drawing;

namespace TrifoldDesk.Services;

internal static class PanelGlass
{
    public static Drawing.Rectangle ScreenBounds(FrameworkElement element)
    {
        var start = element.PointToScreen(new Point());
        var end = element.PointToScreen(new Point(element.ActualWidth, element.ActualHeight));
        return Drawing.Rectangle.FromLTRB((int)Math.Round(start.X), (int)Math.Round(start.Y),
            (int)Math.Round(end.X), (int)Math.Round(end.Y));
    }

    public static async Task<BitmapSource?> CaptureAsync(nint window, Drawing.Rectangle bounds,
        double radius, double scale, bool lighterPanel = false)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;
        try
        {
            return await Task.Run(() => DockGlassRenderer.Render(window, bounds, radius * scale, scale,
                launchpad: true, lighterPanel: lighterPanel));
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception
            or System.Runtime.InteropServices.ExternalException or ArgumentException or OutOfMemoryException)
        {
            System.Diagnostics.Debug.WriteLine(error);
            return null;
        }
    }

    public static void AttachWidget(Border surface)
    {
        var fallback = surface.Background;
        Drawing.Rectangle requested = Drawing.Rectangle.Empty;
        var refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
        bool rendering = false;

        void Schedule()
        {
            if (!surface.IsVisible || PresentationSource.FromVisual(surface) is null) return;
            var bounds = ScreenBounds(surface);
            if (bounds == requested) return;
            requested = bounds;
            refresh.Stop();
            if (!rendering) refresh.Start();
        }

        refresh.Tick += async (_, _) =>
        {
            refresh.Stop();
            if (!surface.IsVisible || Window.GetWindow(surface) is not Window window) return;
            var bounds = requested;
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == 0) return;
            double scale = VisualTreeHelper.GetDpi(surface).DpiScaleX;
            rendering = true;
            try
            {
                var frame = await CaptureAsync(hwnd, bounds, surface.CornerRadius.TopLeft, scale, lighterPanel: true);
                if (surface.IsVisible && bounds == requested)
                    surface.Background = frame is null ? fallback : new ImageBrush(frame) { Stretch = Stretch.Fill };
            }
            finally
            {
                rendering = false;
                if (surface.IsVisible && bounds != requested) refresh.Start();
            }
        };
        surface.Loaded += (_, _) => { requested = Drawing.Rectangle.Empty; Schedule(); };
        surface.LayoutUpdated += (_, _) => Schedule();
        surface.IsVisibleChanged += (_, _) => { requested = Drawing.Rectangle.Empty; Schedule(); };
        surface.Unloaded += (_, _) => refresh.Stop();
    }
}
