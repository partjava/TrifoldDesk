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
        IdleCheck.IsChecked = settings.TransparentIdle; HoverCheck.IsChecked = settings.HoverExpand;
        AutoCheck.IsChecked = settings.AutoCollapse; TopCheck.IsChecked = settings.AlwaysOnTop;
        AnimationCheck.IsChecked = settings.AnimationsEnabled; StartupCheck.IsChecked = settings.StartWithWindows;
        var screens = new List<Choice> { new("", "自动 · 主显示器") };
        screens.AddRange(Forms.Screen.AllScreens.Select(s => new Choice(s.DeviceName, $"{s.DeviceName} · {s.Bounds.Width} × {s.Bounds.Height}")));
        MonitorCombo.ItemsSource = screens;
        MonitorCombo.SelectedValue = screens.Any(s => s.Id == settings.MonitorDevice) ? settings.MonitorDevice : "";
        var networks = new List<Choice> { new("", "自动 · 联网接口") };
        networks.AddRange(SystemMonitorService.GetNetworkChoices().Select(n => new Choice(n.Id, n.Name)));
        NetworkCombo.ItemsSource = networks;
        NetworkCombo.SelectedValue = networks.Any(n => n.Id == settings.NetworkInterfaceId) ? settings.NetworkInterfaceId : "";
    }
    private void SaveClick(object sender, RoutedEventArgs e)
    {
        Result = new AppSettings
        {
            GlassOpacity = OpacitySlider.Value, IsCollapsed = _original.IsCollapsed,
            TransparentIdle = IdleCheck.IsChecked == true, HoverExpand = HoverCheck.IsChecked == true,
            AutoCollapse = AutoCheck.IsChecked == true, AlwaysOnTop = TopCheck.IsChecked == true,
            AnimationsEnabled = AnimationCheck.IsChecked == true, StartWithWindows = StartupCheck.IsChecked == true,
            MonitorDevice = MonitorCombo.SelectedValue as string ?? "", NetworkInterfaceId = NetworkCombo.SelectedValue as string ?? ""
        };
        DialogResult = true;
    }
}
