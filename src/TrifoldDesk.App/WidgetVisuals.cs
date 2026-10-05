namespace TrifoldDesk;

public sealed class AnalogClock : FrameworkElement
{
    public Brush Ink { get; set; } = Brushes.WhiteSmoke;
    public Brush Accent { get; set; } = Brushes.Turquoise;
    protected override void OnRender(DrawingContext dc)
    {
        double radius = Math.Max(0, Math.Min(ActualWidth, ActualHeight) / 2 - 6);
        if (radius < 8) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        Point At(double angle, double length) => new(center.X + Math.Sin(angle) * length, center.Y - Math.Cos(angle) * length);
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(45, 210, 220, 225)), 1), center, radius, radius);
        for (int n = 0; n < 60; n++)
        {
            double angle = n * Math.PI / 30;
            dc.DrawLine(new Pen(Ink, n % 5 == 0 ? 1.5 : .6), At(angle, radius * (n % 5 == 0 ? .83 : .92)), At(angle, radius * .97));
        }
        var now = DateTime.Now;
        void Hand(double angle, double length, Brush brush, double thickness) => dc.DrawLine(new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, center, At(angle, radius * length));
        Hand((now.Hour % 12 + now.Minute / 60d) * Math.PI / 6, .5, Ink, 3);
        Hand((now.Minute + now.Second / 60d) * Math.PI / 30, .76, Ink, 2);
        Hand(now.Second * Math.PI / 30, .85, Accent, 1);
        dc.DrawEllipse(Accent, null, center, 2.5, 2.5);
    }
}

public sealed class CapacityGauge : FrameworkElement
{
    public double Value { get; set; }
    public bool Segmented { get; set; }
    public Brush Accent { get; set; } = Brushes.Turquoise;
    protected override void OnRender(DrawingContext dc)
    {
        var track = new SolidColorBrush(Color.FromArgb(40, 190, 200, 205));
        double value = Math.Clamp(double.IsFinite(Value) ? Value : 0, 0, 100) / 100;
        if (Segmented)
        {
            double step = ActualWidth / 12;
            for (int n = 0; n < 12; n++)
            {
                var rect = new Rect(n * step, 0, Math.Max(0, step - 2), ActualHeight);
                dc.DrawRoundedRectangle(track, null, rect, 1.5, 1.5);
                double filled = Math.Clamp(value * 12 - n, 0, 1);
                if (filled > 0) dc.DrawRoundedRectangle(Accent, null, new Rect(rect.X, 0, rect.Width * filled, rect.Height), 1.5, 1.5);
            }
        }
        else
        {
            dc.DrawRoundedRectangle(track, null, new Rect(0, 0, ActualWidth, ActualHeight), 2, 2);
            if (value > 0) dc.DrawRoundedRectangle(Accent, null, new Rect(0, 0, ActualWidth * value, ActualHeight), 2, 2);
        }
    }
}

public sealed class HardwareGlyph : FrameworkElement
{
    public string Kind { get; set; } = "cpu";
    public double BatteryPercent { get; set; }
    public bool Charging { get; set; }
    public Brush Ink { get; set; } = Brushes.Turquoise;
    protected override void OnRender(DrawingContext dc)
    {
        double scale = Math.Min(ActualWidth, ActualHeight) / 24;
        if (scale <= 0) return;
        dc.PushTransform(new ScaleTransform(scale, scale));
        var pen = new Pen(Ink, 1);
        if (Kind == "battery") { dc.DrawRoundedRectangle(null, pen, new Rect(2, 7, 18, 10), 2, 2); dc.DrawRectangle(Ink, null, new Rect(21, 10, 2, 4)); dc.PushOpacity(.35);dc.DrawRoundedRectangle(Ink,null,new Rect(4,9,14*Math.Clamp(BatteryPercent,0,100)/100,6),1,1);dc.Pop(); if(Charging){ dc.DrawLine(pen, new Point(12, 8), new Point(9, 12)); dc.DrawLine(pen, new Point(9, 12), new Point(14, 12)); dc.DrawLine(pen, new Point(14, 12), new Point(11, 16)); } }
        else if (Kind == "memory") { dc.DrawRoundedRectangle(null, pen, new Rect(1, 6, 22, 11), 1, 1); for(int n=0;n<4;n++) { dc.DrawRectangle(null,pen,new Rect(3+n*5,8,3,6)); dc.DrawLine(pen,new Point(4+n*5,18),new Point(4+n*5,20)); } }
        else if (Kind == "disks") { dc.DrawRoundedRectangle(null,pen,new Rect(3,2,18,20),2,2); dc.DrawEllipse(null,pen,new Point(12,10),6,6); dc.DrawEllipse(Ink,null,new Point(12,10),1.5,1.5); dc.DrawLine(pen,new Point(12,10),new Point(18,18)); }
        else if (Kind == "network") { for(int n=0;n<4;n++) dc.DrawRoundedRectangle(Ink,null,new Rect(3+n*5,17-n*4,3,4+n*4),1,1); }
        else { dc.DrawRoundedRectangle(null,pen,new Rect(5,5,14,14),2,2); dc.DrawRectangle(null,pen,new Rect(8,8,8,8)); for(int n=0;n<4;n++){double p=7+n*3;dc.DrawLine(pen,new Point(p,2),new Point(p,5));dc.DrawLine(pen,new Point(p,19),new Point(p,22));dc.DrawLine(pen,new Point(2,p),new Point(5,p));dc.DrawLine(pen,new Point(19,p),new Point(22,p));} }
        dc.Pop();
    }
}
