using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
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
        var previous = _foldOverlay?.Children.OfType<Image>().Select(image => { var group = (TransformGroup)image.RenderTransform; return (((ScaleTransform)group.Children[0]).ScaleX, ((SkewTransform)group.Children[1]).AngleY, ((TranslateTransform)group.Children[2]).X); }).ToArray();
        ClearFold(); PanelTranslation.X = 0; PanelRoot.UpdateLayout();
        int width = Math.Max(3, (int)Math.Ceiling(PanelRoot.ActualWidth)), height = Math.Max(1, (int)Math.Ceiling(PanelRoot.ActualHeight));
        var snapshot = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); snapshot.Render(PanelRoot); snapshot.Freeze();
        var overlay = new Canvas { IsHitTestVisible = false, ClipToBounds = true }; _foldOverlay = overlay; ((Grid)Content).Children.Add(overlay);
        for (int n = 0; n < 3; n++)
        {
            int left = width * n / 3, right = width * (n + 1) / 3;
            var image = new Image { Width = right - left, Height = height, Source = new CroppedBitmap(snapshot, new Int32Rect(left, 0, right-left, height)), RenderTransformOrigin = new Point(1,.5) };
            var scale = new ScaleTransform(); var skew = new SkewTransform(); var shift = new TranslateTransform();
            image.RenderTransform = new TransformGroup { Children = new TransformCollection { scale, skew, shift } }; Canvas.SetLeft(image,left); overlay.Children.Add(image);
            int order = collapsed ? n : 2-n; var delay = TimeSpan.FromMilliseconds(order * 55); double foldedX = width-left-image.Width;
            Animate(scale, ScaleTransform.ScaleXProperty, previous?[n].Item1 ?? (collapsed ? 1 : .02), collapsed ? .02 : 1, delay);
            Animate(skew, SkewTransform.AngleYProperty, previous?[n].Item2 ?? (collapsed ? 0 : (n%2==0 ? 12 : -12)), collapsed ? (n%2==0 ? 12 : -12) : 0, delay);
            Animate(shift, TranslateTransform.XProperty, previous?[n].Item3 ?? (collapsed ? 0 : foldedX), collapsed ? foldedX : 0, delay);
        }
        PanelRoot.Opacity = 0;
    }
    private static void Animate(Animatable target, DependencyProperty property, double from, double to, TimeSpan delay)
    {
        var animation = new DoubleAnimation(from,to,TimeSpan.FromMilliseconds(160)) { BeginTime = delay, EasingFunction = new CubicEase { EasingMode=EasingMode.EaseInOut } };
        target.SetValue(property, from); target.BeginAnimation(property, animation);
    }
}
