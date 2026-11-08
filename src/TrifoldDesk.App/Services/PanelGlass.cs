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
        double radius, double scale, bool lighterPanel = false, Drawing.Rectangle? widgetHostBounds = null)
    {
        var source = GlassCapturePolicy.Source;
        int revision = GlassCapturePolicy.Revision;
        if (source == GlassCaptureSource.Disabled || bounds.Width <= 0 || bounds.Height <= 0) return null;
        try
        {
            var frame = await Task.Run(() => DockGlassRenderer.Render(window, bounds, radius * scale, scale,
                launchpad: true, lighterPanel: lighterPanel, widgetHostBounds: widgetHostBounds));
            return revision == GlassCapturePolicy.Revision ? frame : null;
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
        var layoutDelay = TimeSpan.FromMilliseconds(180);
        var frameDelay = TimeSpan.FromMilliseconds(250);
        var retryDelay = TimeSpan.FromSeconds(3);
        Drawing.Rectangle requested = Drawing.Rectangle.Empty;
        var refresh = new DispatcherTimer(DispatcherPriority.Background);
        bool rendering = false;
        int revision = 0;

        void QueueRefresh(TimeSpan delay)
        {
            refresh.Stop();
            refresh.Interval = delay;
            refresh.Start();
        }

        void Reset()
        {
            refresh.Stop();
            requested = Drawing.Rectangle.Empty;
            revision++;
        }

        void Schedule()
        {
            if (!GlassCapturePolicy.Enabled || !surface.IsLoaded || !surface.IsVisible ||
                PresentationSource.FromVisual(surface) is null) return;
            var bounds = ScreenBounds(surface);
            if (bounds == requested) return;
            requested = bounds;
            revision++;
            if (bounds.Width <= 0 || bounds.Height <= 0) refresh.Stop();
            else if (!rendering) QueueRefresh(layoutDelay);
        }

        refresh.Tick += async (_, _) =>
        {
            refresh.Stop();
            if (!GlassCapturePolicy.Enabled || !surface.IsLoaded || !surface.IsVisible ||
                Window.GetWindow(surface) is not Window window) return;
            if (window.WindowState == WindowState.Minimized)
            {
                QueueRefresh(retryDelay);
                return;
            }
            var bounds = requested;
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == 0)
            {
                QueueRefresh(retryDelay);
                return;
            }
            double scale = VisualTreeHelper.GetDpi(surface).DpiScaleX;
            int capturedRevision = revision;
            BitmapSource? frame = null;
            rendering = true;
            try
            {
                frame = await CaptureAsync(hwnd, bounds, surface.CornerRadius.TopLeft, scale, lighterPanel: true,
                    widgetHostBounds: ScreenBounds(window));
                if (surface.IsLoaded && surface.IsVisible && capturedRevision == revision)
                    surface.Background = frame is null ? fallback : new ImageBrush(frame) { Stretch = Stretch.Fill };
            }
            finally
            {
                rendering = false;
                if (GlassCapturePolicy.Enabled && surface.IsLoaded && surface.IsVisible &&
                    requested.Width > 0 && requested.Height > 0)
                    QueueRefresh(capturedRevision != revision ? layoutDelay : frame is null ? retryDelay : frameDelay);
            }
        };
        void OnCaptureChanged()
        {
            surface.Background = fallback;
            Reset();
            Schedule();
        }

        surface.Loaded += (_, _) =>
        {
            GlassCapturePolicy.Changed -= OnCaptureChanged;
            GlassCapturePolicy.Changed += OnCaptureChanged;
            OnCaptureChanged();
        };
        surface.LayoutUpdated += (_, _) => Schedule();
        surface.IsVisibleChanged += (_, _) => { Reset(); Schedule(); };
        surface.Unloaded += (_, _) =>
        {
            GlassCapturePolicy.Changed -= OnCaptureChanged;
            Reset();
        };
    }
}
