using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
namespace TrifoldDesk.Services;

// One low-priority clock for all decorative rings. Visibility checks run at 2 Hz.
public static class RenderBudget
{
    private static readonly HashSet<OrganicMetricRing> Rings = [];
    private static readonly DispatcherTimer Clock = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(100) };
    private static DateTime _nextPolicy;
    private static readonly Dictionary<FrameworkElement,bool> Visibility = [];
    public static bool SavingPower => System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline;
    public static bool AnimationsAllowed => Application.Current.MainWindow is not MainWindow main || main.AnimationsAllowed;
    static RenderBudget()
    {
        Clock.Tick += (_,_) =>
        {
            if(DateTime.UtcNow >= _nextPolicy)
            {
                Visibility.Clear(); _nextPolicy=DateTime.UtcNow.AddMilliseconds(500);
                Clock.Interval=TimeSpan.FromMilliseconds(SavingPower?200:100);
            }
            bool active=false; foreach(var ring in Rings) { ring.Advance(Clock.Interval.TotalSeconds,AnimationsAllowed && CanDraw(ring)); active|=ring.TestAnimating; }
            Clock.Interval=TimeSpan.FromMilliseconds(active?(SavingPower?200:100):500);
        };
    }
    internal static void Register(OrganicMetricRing ring) { Rings.Add(ring); Clock.Start(); }
    internal static void Unregister(OrganicMetricRing ring) { Rings.Remove(ring); Visibility.Remove(ring); if(Rings.Count==0)Clock.Stop(); }
    public static bool CanDraw(FrameworkElement element)
    {
        if(DateTime.UtcNow>=_nextPolicy){Visibility.Clear();_nextPolicy=DateTime.UtcNow.AddMilliseconds(500);}
        if(!element.IsLoaded || !element.IsVisible || element.ActualWidth<=0 || element.ActualHeight<=0)return false;
        if(Visibility.TryGetValue(element,out var visible) && DateTime.UtcNow<_nextPolicy)return visible;
        visible=EvaluateVisibility(element); Visibility[element]=visible; return visible;
    }
    private static bool EvaluateVisibility(FrameworkElement element)
    {
        try
        {
            var window=Window.GetWindow(element); if(window==null || !window.IsVisible || window.WindowState==WindowState.Minimized)return false;
            for(DependencyObject? parent=VisualTreeHelper.GetParent(element);parent!=null;parent=VisualTreeHelper.GetParent(parent))
                if(parent is ScrollViewer scroll)
                {
                    var bounds=element.TransformToAncestor(scroll).TransformBounds(new Rect(element.RenderSize));
                    if(!bounds.IntersectsWith(new Rect(0,0,scroll.ActualWidth,scroll.ActualHeight)))return false;
                }
            var topLeft=element.PointToScreen(new Point()); var bottomRight=element.PointToScreen(new Point(element.ActualWidth,element.ActualHeight));
            var hwnd=new WindowInteropHelper(window).Handle;
            for(var above=GetWindow(hwnd,3);above!=IntPtr.Zero;above=GetWindow(above,3))
            {
                if(!IsWindowVisible(above)||IsIconic(above))continue;
                if(DwmGetWindowAttribute(above,14,out int cloaked,sizeof(int))==0 && cloaked!=0)continue;
                // Layered windows can be transparent; do not mistake them for opaque covers.
                if((GetWindowLong(above,-20)&0x80000)!=0)continue;
                if(GetWindowRect(above,out var rect) && rect.Left<=topLeft.X && rect.Top<=topLeft.Y && rect.Right>=bottomRight.X && rect.Bottom>=bottomRight.Y)return false;
            }
            return true;
        }
        catch(InvalidOperationException){return false;}
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect {public int Left,Top,Right,Bottom;}
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd,uint command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd,int attribute,out int value,int size);
}
