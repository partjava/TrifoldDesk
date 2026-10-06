using TrifoldDesk.Services;
namespace TrifoldDesk;

// The waveform is decorative; the adjacent number remains the measured utilization.
public sealed class OrganicMetricRing : FrameworkElement
{
    private readonly Pen[] _pens = new Pen[3];
    private readonly double _seed = Random.Shared.NextDouble() * 100;
    private double _time, _activity, _target, _last;
    private bool _loaded, _available;
    public Brush Accent { get; set; } = Brushes.Turquoise;
    internal bool TestAnimating { get; private set; }
    public OrganicMetricRing()
    {

        Loaded += (_, _) => { _loaded = true; RenderBudget.Register(this); UpdateTimer(); };
        Unloaded += (_, _) => { _loaded = false; RenderBudget.Unregister(this); UpdateTimer(); };
        IsVisibleChanged += (_, _) => UpdateTimer();
        ToolTip = "随负载变化的不规则波动；准确占用率见左侧数字";
    }
    public void Sample(double? value)
    {
        _available = value is double v && double.IsFinite(v);
        if (_available)
        {
            double next = Math.Clamp(value!.Value, 0, 100);
            _target = Math.Clamp(next / 150 + Math.Abs(next - _last) / 35, 0, 1);
            _last = next;
        }
        UpdateTimer(); InvalidateVisual();
    }
    private void UpdateTimer()
    {
        TestAnimating = _loaded && IsVisible && _available && RenderBudget.AnimationsAllowed;
    }
    internal void Advance(double elapsed, bool drawable)
    {
        TestAnimating = _loaded && IsVisible && _available && drawable;
        if(!TestAnimating)return;
        _time += elapsed; _activity += (_target-_activity)*.15; _target *= .98; InvalidateVisual();
    }
    protected override void OnRender(DrawingContext dc)
    {
        double radius = Math.Min(ActualWidth, ActualHeight) / 2 - 5;
        if (radius < 2) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        if (!_available) { dc.PushOpacity(.25); dc.DrawEllipse(null, new Pen(Accent, 1), center, radius, radius); dc.Pop(); return; }
        for (int layer = 2; layer >= 0; layer--)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                for (int n = 0; n < 64; n++)
                {
                    double angle = n * Math.PI * 2 / 64;
                    double wave = .5 * Math.Sin(5 * angle + _time * .83 + _seed)
                        + .3 * Math.Sin(8 * angle - _time * 1.17 + _seed * 2)
                        + .2 * Math.Sin(11 * angle + _time * .61 + _seed * 3);
                    double r = radius + layer * 1.2 + wave * (1 + _activity * 2.4);
                    var point = new Point(center.X + Math.Cos(angle) * r, center.Y + Math.Sin(angle) * r);
                    if (n == 0) context.BeginFigure(point, false, true); else context.LineTo(point, true, true);
                }
            }
            geometry.Freeze(); dc.PushOpacity(layer == 0 ? .9 : .12 / layer);
            dc.DrawGeometry(null, _pens[layer] ??= new Pen(Accent, layer == 0 ? 1.5 : .7) { LineJoin = PenLineJoin.Round }, geometry); dc.Pop();
        }
    }
}
