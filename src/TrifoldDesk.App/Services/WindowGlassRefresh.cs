using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TrifoldDesk.Services;

internal static class WindowGlassRefresh
{
    public static void Attach(Window window, Border backdrop, Border glass,
        Action<BitmapSource?> applyFrame)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        bool rendering = false;
        bool closed = false;
        int revision = 0;

        bool CanRefresh() => !closed && window.IsVisible && window.WindowState != WindowState.Minimized;

        void Schedule()
        {
            revision++;
            timer.Stop();
            timer.Interval = TimeSpan.FromMilliseconds(250);
            if (CanRefresh() && !rendering) timer.Start();
        }

        timer.Tick += async (_, _) =>
        {
            timer.Stop();
            if (!CanRefresh()) return;

            // The initial frame is captured separately while the window is transparent.
            if (window.Opacity <= 0 || PresentationSource.FromVisual(backdrop) is null)
            {
                timer.Start();
                return;
            }

            int frameRevision = revision;
            var bounds = PanelGlass.ScreenBounds(backdrop);
            var dpi = VisualTreeHelper.GetDpi(backdrop);
            var hwnd = new WindowInteropHelper(window).Handle;
            rendering = true;
            try
            {
                var frame = await PanelGlass.CaptureAsync(hwnd, bounds, glass.CornerRadius.TopLeft, dpi.DpiScaleX);
                if (CanRefresh() && frameRevision == revision && PanelGlass.ScreenBounds(backdrop) == bounds)
                    applyFrame(frame);
                timer.Interval = TimeSpan.FromMilliseconds(frame is null ? 1000 : 250);
            }
            finally
            {
                rendering = false;
                if (CanRefresh()) timer.Start();
            }
        };

        window.IsVisibleChanged += (_, _) => Schedule();
        window.StateChanged += (_, _) => Schedule();
        window.Closed += (_, _) =>
        {
            closed = true;
            Schedule();
        };
    }
}
