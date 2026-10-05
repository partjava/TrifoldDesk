using System.Windows.Threading;
using TrifoldDesk.Services;
namespace TrifoldDesk;
public sealed class MetricPlot : FrameworkElement
{
    private readonly Queue<double> _samples = new();
    public Brush Accent { get; set; } = Brushes.Cyan;
    public bool Percent { get; set; } = true;
    public void Add(double value) { if (!double.IsFinite(value)) return; _samples.Enqueue(Math.Max(0, value)); while (_samples.Count > 60) _samples.Dequeue(); InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); double w = ActualWidth, h = ActualHeight; if (w < 2 || h < 2) return;
        for (int n = 1; n <= 3; n++) dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(20, 150, 170, 180)), 1), new Point(0, h * n / 4), new Point(w, h * n / 4));
        if (_samples.Count < 2) return;
        var points = _samples.ToArray(); double maximum = Percent ? 100 : Math.Max(1, points.Max() * 1.15);
        var line = new StreamGeometry(); using (var c = line.Open()) { c.BeginFigure(new Point(0, h - h * Math.Min(points[0], maximum) / maximum), false, false); for (int n = 1; n < points.Length; n++) c.LineTo(new Point(w * n / (points.Length - 1), h - h * Math.Min(points[n], maximum) / maximum), true, false); }
        dc.DrawGeometry(null, new Pen(Accent, 2), line);
    }
}
public sealed class DashboardWidget : Grid
{
    private readonly string _kind;
    private readonly bool _compact;
    private readonly MainViewModel _model;
    private readonly TextBlock _value = new(), _detail = new(), _extra = new();
    private readonly MetricPlot _plot = new();
    private readonly Border _track = new();
    private readonly DispatcherTimer _timer = new();
    private GpuMonitorService? _gpu;
    private bool _busy, _active;
    private int _generation;
    private readonly Brush _ink, _muted, _accent;
    public DashboardWidget(string kind, string style, MainViewModel model, bool compact = false)
    {
        _kind = kind; _model = model; _compact = compact;
        _ink = Brush(style == "paper" ? "#243D45" : "#F2F6F8"); _muted = Brush(style == "paper" ? "#587079" : "#A8BDC8");
        _accent = Brush(style == "neon" ? "#B395FF" : style == "paper" ? "#267E73" : "#74E1D1");
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new()); RowDefinitions.Add(new() { Height = GridLength.Auto });
        _value.FontSize = kind == "clock" ? 58 : kind == "network" ? 32 : 48; _value.FontWeight = FontWeights.Light; _value.Foreground = _ink; _value.Margin = new Thickness(4, 0, 4, 4); Children.Add(_value);
        _detail.FontSize = 11; _detail.Foreground = _muted; _detail.TextWrapping = TextWrapping.Wrap; _detail.Margin = new Thickness(6, 0, 6, 12); Grid.SetRow(_detail, 1); Children.Add(_detail);
        _plot.MinHeight = 30; _plot.Margin = new Thickness(6, 0, 6, 12); _plot.Accent = _accent; _plot.Percent = kind != "network"; Grid.SetRow(_plot, 2); Children.Add(_plot);
        _extra.FontSize = 11; _extra.Foreground = _accent; _extra.Margin = new Thickness(6, 0, 6, 6); _extra.TextWrapping = TextWrapping.Wrap; Grid.SetRow(_extra, 3); _extra.Visibility = Visibility.Collapsed;
        _track.Height = 3; _track.Background = _accent; _track.HorizontalAlignment = HorizontalAlignment.Left; Grid.SetRow(_track, 2);
        if (kind is "clock" or "battery") { _plot.Visibility = Visibility.Collapsed; Children.Add(_track); }
        _timer.Interval = TimeSpan.FromSeconds(kind == "gpu" ? 3 : kind == "battery" ? 10 : 1); _timer.Tick += Tick;
        Loaded += (_, _) => { _active = true; _generation++; _timer.Start(); Tick(this, EventArgs.Empty); };
        Unloaded += (_, _) => { _active = false; _generation++; _timer.Stop(); var old = _gpu; _gpu = null; if (old != null) _ = Task.Run(old.Dispose); };
        IsVisibleChanged += (_, _) => { if (_active && IsVisible) { _timer.Start(); Tick(this, EventArgs.Empty); } else _timer.Stop(); };
        Tick(this, EventArgs.Empty);
        if (compact)
        {
            _value.FontSize = kind == "network" ? 19 : 28; _value.Margin = new Thickness(0, 2, 0, 3);
            _detail.Margin = new Thickness(0, 0, 0, 4); _detail.FontSize = 10;
            _plot.MinHeight = 12; _plot.Margin = new Thickness(0, 0, 0, 3);
            _extra.FontSize = 9; _extra.Margin = new Thickness(0); _extra.Visibility = Visibility.Collapsed;
            SizeChanged += (_, _) => _extra.Visibility = ActualHeight >= 140 ? Visibility.Visible : Visibility.Collapsed;
            if (kind == "network") _detail.Text = _model.UploadText;
        }
    }
    private static Brush Brush(string color) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
    internal static string BatteryValue(int status, float fraction) => status == 255 ? "不可用" : (status & 128) != 0 ? "交流电" : !float.IsFinite(fraction) || fraction is < 0 or > 1 ? "不可用" : $"{fraction * 100:0}%";
    private async void Tick(object? sender, EventArgs e)
    {
        if (_busy) return;
        switch (_kind)
        {
            case "cpu": _value.Text = _model.CpuText; _detail.Text = ""; _extra.Text = "最近60次采样"; if (_model.CpuText.EndsWith('%')) _plot.Add(_model.CpuValue); break;
            case "memory": _value.Text = _model.MemoryPercent; _detail.Text = _model.MemoryText; _extra.Text = "物理内存 · 最近60次采样"; if (_model.MemoryPercent.EndsWith('%')) _plot.Add(_model.MemoryValue); break;
            case "network": _value.Text = _model.DownloadText; _detail.Text = _compact ? _model.UploadText : _model.NetworkName; _extra.Text = _model.UploadText + "  上传"; if (_model.DownloadRate is double rate) _plot.Add(rate); break;
            case "clock": _value.Text = DateTime.Now.ToString("HH:mm"); _detail.Text = DateTime.Now.ToString("MM月dd日  dddd"); _extra.Text = CalendarMonth.LunarText(DateTime.Today); _track.Width = Math.Max(1, ActualWidth - 12) * DateTime.Now.Second / 60d; break;
            case "battery":
                var power = System.Windows.Forms.SystemInformation.PowerStatus;
                bool missing = power.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.Unknown && power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery), unknown = power.BatteryChargeStatus == System.Windows.Forms.BatteryChargeStatus.Unknown || !float.IsFinite(power.BatteryLifePercent) || power.BatteryLifePercent is < 0 or > 1;
                _value.Text = BatteryValue((int)power.BatteryChargeStatus, power.BatteryLifePercent);
                _detail.Text = missing ? "此设备没有系统电池" : unknown ? "电源信息暂不可用" : power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? "已连接电源" : power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline ? "正在使用电池" : "供电状态未知";
                _extra.Text = !missing && power.BatteryLifeRemaining > 0 ? $"预计剩余 {TimeSpan.FromSeconds(power.BatteryLifeRemaining):h\\:mm}" : "Windows 系统电源状态"; _track.Width = missing || unknown ? 0 : Math.Max(1, ActualWidth - 12) * power.BatteryLifePercent; break;
            case "gpu":
                if (!_active) { _value.Text = "—"; _detail.Text = "GPU · 3D引擎"; _extra.Text = "等待组件启动"; break; }
                _busy = true; int generation = _generation; var provider = _gpu ??= new GpuMonitorService();
                try { var result = await Task.Run(provider.Read); if (!_active || generation != _generation) return; _value.Text = result.Value is double v ? $"{v:0}%" : "—"; _detail.Text = result.Value == null ? "不可用" : ""; _detail.ToolTip = result.Status; _extra.Text = result.Status; if (result.Value is double sample) _plot.Add(sample); }
                finally { _busy = false; } break;
        }
    }
    internal bool TestActive => _active && _timer.IsEnabled;
    internal string TestValue => _value.Text;
}
