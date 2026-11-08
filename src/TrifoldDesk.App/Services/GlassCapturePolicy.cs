using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace TrifoldDesk.Services;

internal enum GlassCaptureSource { Disabled, ScreenComposite, DesktopOnly }

internal static class GlassCapturePolicy
{
    private const int RemoteSession = 0x1000;
    private const int RemoteControl = 0x2001;
    private static int _source = (int)GlassCaptureSource.ScreenComposite;
    private static int _revision;
    private static GlassBackdropMode _mode;
    private static bool _manualPause;
    private static DispatcherTimer? _sessionTimer;

    public static GlassCaptureSource Source => (GlassCaptureSource)Volatile.Read(ref _source);
    public static int Revision => Volatile.Read(ref _revision);
    public static bool Enabled => Source != GlassCaptureSource.Disabled;
    public static event Action? Changed;

    public static void Configure(GlassBackdropMode mode, bool manualPause)
    {
        _mode = mode;
        _manualPause = manualPause;
        UpdateSource();
        if (_sessionTimer is not null) return;

        _sessionTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _sessionTimer.Tick += (_, _) => UpdateSource();
        _sessionTimer.Start();
    }

    private static void UpdateSource()
    {
        bool remote = GetSystemMetrics(RemoteSession) != 0 || GetSystemMetrics(RemoteControl) != 0;
        var next = _manualPause || remote ? GlassCaptureSource.Disabled :
            _mode == GlassBackdropMode.DesktopOnly ? GlassCaptureSource.DesktopOnly : GlassCaptureSource.ScreenComposite;
        if ((GlassCaptureSource)Interlocked.Exchange(ref _source, (int)next) != next)
        {
            Interlocked.Increment(ref _revision);
            DesktopGlassBackdrop.Reset();
            Changed?.Invoke();
        }
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
