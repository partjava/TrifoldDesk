using System.Windows.Threading;
using TrifoldDesk.Services;
namespace TrifoldDesk;
public sealed class MetricPlot : FrameworkElement
{
    private readonly Queue<double> _samples = new();
    private readonly Queue<double> _secondary = new();
    public Brush Accent { get; set; } = Brushes.Cyan;
    public bool Percent { get; set; } = true;
    public void Add(double value, double? secondary = null) { if (!double.IsFinite(value)) return; _samples.Enqueue(Math.Max(0, value)); _secondary.Enqueue(secondary is double v && double.IsFinite(v) ? Math.Max(0,v) : 0); while (_samples.Count > 60) { _samples.Dequeue(); _secondary.Dequeue(); } if(RenderBudget.CanDraw(this)) InvalidateVisual(); }
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc); double w = ActualWidth, h = ActualHeight; if (w < 2 || h < 2) return;
        for (int n = 1; n <= 3; n++) dc.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(20, 150, 170, 180)), 1), new Point(0, h * n / 4), new Point(w, h * n / 4));
        if (_samples.Count < 2) return;
        var points = _samples.ToArray(); double maximum = Percent ? 100 : Math.Max(1, Math.Max(points.Max(),_secondary.Max()) * 1.15);
        var line = new StreamGeometry(); using (var c = line.Open()) { c.BeginFigure(new Point(0, h - h * Math.Min(points[0], maximum) / maximum), false, false); for (int n = 1; n < points.Length; n++) c.LineTo(new Point(w * n / (points.Length - 1), h - h * Math.Min(points[n], maximum) / maximum), true, false); }
        var fill = new StreamGeometry(); using(var c=fill.Open()) { c.BeginFigure(new Point(0,h),true,true); for(int n=0;n<points.Length;n++) c.LineTo(new Point(w*n/(points.Length-1),h-h*Math.Min(points[n],maximum)/maximum),true,false); c.LineTo(new Point(w,h),true,false); }
        var color = Accent is SolidColorBrush solid ? solid.Color : Colors.Turquoise;
        dc.DrawGeometry(new LinearGradientBrush(Color.FromArgb(50,color.R,color.G,color.B),Color.FromArgb(0,color.R,color.G,color.B),90),null,fill);
        dc.DrawGeometry(null, new Pen(Accent, 1.5), line);
        dc.DrawEllipse(Accent,null,new Point(w,h-h*Math.Min(points[^1],maximum)/maximum),2,2);
        if(!Percent && _secondary.Any(v=>v>0)) { var up=_secondary.ToArray(); var path=new StreamGeometry(); using(var c=path.Open()){c.BeginFigure(new Point(0,h-h*up[0]/maximum),false,false);for(int n=1;n<up.Length;n++)c.LineTo(new Point(w*n/(up.Length-1),h-h*up[n]/maximum),true,false);} dc.DrawGeometry(null,new Pen(new SolidColorBrush(Color.FromArgb(110,color.R,color.G,color.B)),1),path); }
    }
}
public sealed class DashboardWidget : Grid
{
    private readonly string _kind;
    private readonly bool _compact;
    private readonly MainViewModel _model;
    private readonly TextBlock _value = new(), _detail = new(), _extra = new();
    private readonly MetricPlot _plot = new();
    public event Action<double,bool>? BatteryUpdated;
    public event Action<double?>? UtilizationUpdated;
    private readonly CapacityGauge _gauge = new() { Height=7, Margin=new Thickness(0,8,0,8), VerticalAlignment=VerticalAlignment.Bottom };
    private AnalogClock? _analog;
    private HardwareInfo? _hardware;
    private readonly DispatcherTimer _timer = new();
    private GpuMonitorService? _gpu;
    private bool _busy, _active, _wasDrawable;
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
        Children.Add(_extra);
        if (kind is "memory" or "battery") { _plot.Visibility = Visibility.Collapsed; _gauge.Accent=new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#38BAC1"),(Color)ColorConverter.ConvertFromString(style=="paper"?"#267E73":"#74E1D1"),0); _gauge.Segmented=kind=="memory"; Grid.SetRow(_gauge,2); Children.Add(_gauge); }
        if(kind=="clock")
        {
            ColumnDefinitions.Add(new(){Width=new GridLength(1.7,GridUnitType.Star)}); ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});
            _plot.Visibility=Visibility.Collapsed; _extra.Visibility=Visibility.Visible; _extra.Foreground=_muted;
            _analog=new AnalogClock{Ink=_ink,Accent=_accent,Margin=new Thickness(8,4,0,4)}; Grid.SetColumn(_analog,1); Grid.SetRowSpan(_analog,4); Children.Add(_analog);
            VerticalAlignment=VerticalAlignment.Center;
            SizeChanged+=(_,_)=>_value.FontSize=Math.Clamp(ActualWidth*.14,24,66);
        }
        _timer.Interval = TimeSpan.FromSeconds(kind == "gpu" ? 5 : kind == "battery" ? 10 : kind == "clock" ? 1 : 2); _timer.Tick += Tick;
        Loaded += (_, _) => { _active = true; _generation++; _timer.Start(); Tick(this, EventArgs.Empty); };
        Unloaded += (_, _) => { _active = false; _generation++; _timer.Stop(); var old = _gpu; _gpu = null; if (old != null) _ = Task.Run(old.Dispose); };
        IsVisibleChanged += (_, _) => { if (_active && IsVisible) { _timer.Start(); Tick(this, EventArgs.Empty); } else _timer.Stop(); };
        Tick(this, EventArgs.Empty);
        if(kind is "cpu" or "gpu") Loaded+=async (_,_)=>{ _hardware=await HardwareInfoService.ReadAsync(); if(_active) Tick(this,EventArgs.Empty); };
        if (compact)
        {
            _value.FontSize = kind == "network" ? 19 : 28; _value.Margin = new Thickness(0, 2, 0, 3);
            _detail.Margin = new Thickness(0, 0, 0, 4); _detail.FontSize = 10; _detail.TextWrapping=TextWrapping.NoWrap; _detail.TextTrimming=TextTrimming.CharacterEllipsis;
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
        bool drawable=RenderBudget.CanDraw(this); if(drawable && !_wasDrawable){_plot.InvalidateVisual();_gauge.InvalidateVisual();_analog?.InvalidateVisual();} _wasDrawable=drawable;
        switch (_kind)
        {
            case "cpu": _value.Text = _model.CpuText; _detail.Text = _hardware?.Cpu ?? "读取处理器型号…"; _detail.ToolTip=_detail.Text; _extra.Text = "最近60次采样"; if (_model.CpuText.EndsWith('%')) _plot.Add(_model.CpuValue); UtilizationUpdated?.Invoke(_model.CpuText.EndsWith('%') ? _model.CpuValue : null); break;
            case "memory": _value.Text = _model.MemoryPercent; _detail.Text = _model.MemoryText; _extra.Text = "物理内存"; UtilizationUpdated?.Invoke(_model.MemoryPercent.EndsWith('%' ) ? _model.MemoryValue : null); _gauge.UpdateValue(_model.MemoryValue); break;
            case "network": _value.Text = _model.DownloadText; _detail.Text = _compact ? _model.UploadText : _model.NetworkName; _detail.ToolTip=_model.NetworkName+" · 亮线下载，暗线上传，共用刻度"; _extra.Text = _model.UploadText + "  上传"; if (_model.DownloadRate is double rate) _plot.Add(rate,_model.UploadRate); break;
            case "clock": _value.Text = DateTime.Now.ToString("HH:mm"); _detail.Text = DateTime.Now.ToString("MM月dd日  dddd"); _extra.Text = CalendarMonth.LunarText(DateTime.Today); if(_analog!=null && RenderBudget.CanDraw(_analog))_analog.InvalidateVisual(); break;
            case "battery":
                var power = System.Windows.Forms.SystemInformation.PowerStatus;
                bool missing = power.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.Unknown && power.BatteryChargeStatus.HasFlag(System.Windows.Forms.BatteryChargeStatus.NoSystemBattery), unknown = power.BatteryChargeStatus == System.Windows.Forms.BatteryChargeStatus.Unknown || !float.IsFinite(power.BatteryLifePercent) || power.BatteryLifePercent is < 0 or > 1;
                _value.Text = BatteryValue((int)power.BatteryChargeStatus, power.BatteryLifePercent);
                _detail.Text = missing ? "此设备没有系统电池" : unknown ? "电源信息暂不可用" : power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online ? "已连接电源" : power.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Offline ? "正在使用电池" : "供电状态未知";
                _extra.Text = !missing && power.BatteryLifeRemaining > 0 ? $"预计剩余 {TimeSpan.FromSeconds(power.BatteryLifeRemaining):h\\:mm}" : "Windows 系统电源状态"; _gauge.UpdateValue(missing||unknown?0:power.BatteryLifePercent*100); BatteryUpdated?.Invoke(_gauge.Value,!missing&&!unknown&&power.PowerLineStatus==System.Windows.Forms.PowerLineStatus.Online); break;
            case "gpu":
                if (!_active) { _value.Text = "—"; _detail.Text = "GPU · 3D引擎"; _extra.Text = "等待组件启动"; break; }
                _busy = true; int generation = _generation; var provider = _gpu ??= new GpuMonitorService();
                try { var selected=(Application.Current.MainWindow as MainWindow)?.SelectedGpuId??""; var result = await Task.Run(()=>provider.Read(selected)); if (!_active || generation != _generation) return; _value.Text = result.Value is double v ? $"{v:0}%" : "—"; _detail.Text = string.IsNullOrEmpty(result.AdapterName)?(_hardware?.Gpu??"显卡不可用"):result.AdapterName.Replace("NVIDIA GeForce ",""); _detail.ToolTip=_detail.Text+"\n"+result.Status; _extra.Text = result.DedicatedMemoryUsedBytes is double memory?$"显存 {memory/1073741824d:0.0} / {(result.DedicatedMemoryTotalBytes??0)/1073741824d:0.0} GB":result.Status; UtilizationUpdated?.Invoke(result.Value); if (result.Value is double sample) _plot.Add(sample); }
                finally { _busy = false; } break;
        }
    }
    internal bool TestActive => _active && _timer.IsEnabled;
    internal string TestValue => _value.Text;
}
