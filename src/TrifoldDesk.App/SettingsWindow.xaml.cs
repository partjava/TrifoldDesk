using TrifoldDesk.Services;
using Forms = System.Windows.Forms;

namespace TrifoldDesk;
public partial class SettingsWindow : Window
{
    private readonly AppSettings _original;
    public AppSettings Result { get; private set; } = new();
    private sealed record Choice(string Id, string Name);
    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent(); _original = settings;
        OpacitySlider.Value = settings.GlassOpacity;
        IdleCheck.IsChecked = settings.TransparentIdle;
        DesktopCheck.IsChecked=settings.DesktopEmbedded;
        AnimationCheck.IsChecked = settings.AnimationsEnabled; StartupCheck.IsChecked = settings.StartWithWindows;
        var screens = new List<Choice> { new("", "自动 · 主显示器") };
        screens.AddRange(Forms.Screen.AllScreens.Select(s => new Choice(s.DeviceName, $"{s.DeviceName} · {s.Bounds.Width} × {s.Bounds.Height}")));
        MonitorCombo.ItemsSource = screens;
        MonitorCombo.SelectedValue = screens.Any(s => s.Id == settings.MonitorDevice) ? settings.MonitorDevice : "";
        var networks = new List<Choice> { new("", "自动 · 联网接口") };
        networks.AddRange(SystemMonitorService.GetNetworkChoices().Select(n => new Choice(n.Id, n.Name)));
        var gpus=new List<Choice>{new("","自动 · 最忙3D引擎")};gpus.AddRange(GpuMonitorService.Adapters().Select(a=>new Choice(a.Id,a.Name+" · "+a.Id)));GpuCombo.ItemsSource=gpus;GpuCombo.SelectedValue=gpus.Any(g=>g.Id==settings.SelectedGpuId)?settings.SelectedGpuId:"";
        NetworkCombo.ItemsSource = networks;
        NetworkCombo.SelectedValue = networks.Any(n => n.Id == settings.NetworkInterfaceId) ? settings.NetworkInterfaceId : "";
    }
    public string DiagnosticsReport { get; set; } = "";
    public bool ProfileImported { get; private set; }
    private void ProfileToolsClick(object sender,RoutedEventArgs e)
    {
        var host=Owner as MainWindow;
        var tools=new ProfileToolsWindow(App.DataDirectory, sender is Button button && button.Name=="ProfileImportButton",host==null?null:host.PrepareProfileReplacement,host==null?null:host.FinishProfileReplacement){Owner=this};tools.ShowDialog();
        if(tools.Imported){ProfileImported=true;DialogResult=false;}
    }
    private void DiagnosticsClick(object sender,RoutedEventArgs e)=>new StartupDiagnosticsWindow(DiagnosticsReport,App.DataDirectory){Owner=this}.ShowDialog();
    private void SaveClick(object sender, RoutedEventArgs e)
    {
        Result = new AppSettings
        {
            DesktopEmbedded=DesktopCheck.IsChecked==true,
            SelectedGpuId=GpuCombo.SelectedValue as string ?? "", SensorEnabled=_original.SensorEnabled,
            GlassOpacity = OpacitySlider.Value, IsCollapsed = _original.IsCollapsed,
            TransparentIdle = IdleCheck.IsChecked == true, HoverExpand = false,
            AutoCollapse = false, AlwaysOnTop = false,
            AnimationsEnabled = AnimationCheck.IsChecked == true, StartWithWindows = StartupCheck.IsChecked == true,
            MonitorDevice = MonitorCombo.SelectedValue as string ?? "", NetworkInterfaceId = NetworkCombo.SelectedValue as string ?? ""
        };
        DialogResult = true;
    }
}
