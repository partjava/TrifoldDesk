using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
namespace TrifoldDesk;
public partial class MainWindow
{
    private Canvas? _foldOverlay;
    private bool _foldStateInitialized;
    internal bool TestFoldClean => _foldOverlay == null && PanelRoot.Opacity == 1;
    internal int TestFoldFaces => _foldOverlay?.Children.Count ?? 0;
    private void ClearFold()
    {
        if (_foldOverlay != null) ((Grid)Content).Children.Remove(_foldOverlay);
        _foldOverlay = null; PanelRoot.Opacity = 1;
    }
    private void StartFold(bool collapsed)
    {
        ClearFold(); PanelTranslation.X = 0; PanelRoot.UpdateLayout();
        int width = Math.Max(3, (int)Math.Ceiling(PanelRoot.ActualWidth)), height = Math.Max(1, (int)Math.Ceiling(PanelRoot.ActualHeight));
        var snapshot = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); snapshot.Render(PanelRoot); snapshot.Freeze();
        var overlay = new Canvas { IsHitTestVisible = false, ClipToBounds = true }; _foldOverlay = overlay; ((Grid)Content).Children.Add(overlay);
        for (int n = 0; n < 3; n++)
        {
            int left = width * n / 3, right = width * (n + 1) / 3;
            var projection=FoldingVisual.Create(new CroppedBitmap(snapshot,new Int32Rect(left,0,right-left,height)),n%2==0);
            Canvas.SetLeft(projection.Viewport,left);overlay.Children.Add(projection.Viewport);
            int order=collapsed?n:2-n;Animate(projection.Rotation,AxisAngleRotation3D.AngleProperty,collapsed?0:85,collapsed?85:0,TimeSpan.FromMilliseconds(order*55));
        }
        PanelRoot.Opacity = 0;
    }
    private static void Animate(Animatable target, DependencyProperty property, double from, double to, TimeSpan delay)
    {
        var animation = new DoubleAnimation(from,to,TimeSpan.FromMilliseconds(160)) { BeginTime = delay, EasingFunction = new CubicEase { EasingMode=EasingMode.EaseInOut } };
        target.SetValue(property, from); target.BeginAnimation(property, animation);
    }
}
