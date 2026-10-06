using TrifoldDesk.Services;
using System.Diagnostics;
namespace TrifoldDesk;
public sealed class WirelessControlWindow : Window
{
    private readonly WirelessControlService _service = new();
    private readonly StackPanel _radios = new(), _devices = new();
    private readonly TextBlock _status = new() { Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
    private bool _closed, _refreshing, _refreshAgain;
    public WirelessControlWindow()
    {
        Title = "无线设备与连接"; Width = 560; Height = 620; MinWidth = 440; MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = new SolidColorBrush(Color.FromRgb(24, 28, 34)); Foreground = Brushes.White;
        var root = new StackPanel { Margin = new Thickness(20) }; Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        root.Children.Add(new TextBlock { Text = "Wi-Fi 与蓝牙", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 16) });
        root.Children.Add(_radios); root.Children.Add(_status);
        var refresh = new Button { Content = "刷新设备与连接", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 6, 12, 6) };
        refresh.Click += async (_, _) => await RefreshAsync(); root.Children.Add(refresh);
        root.Children.Add(new TextBlock { Text = "连接与已配对设备", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 20, 0, 10) }); root.Children.Add(_devices);
        root.Children.Add(new TextBlock { Text = "飞行模式：此应用没有可靠的全局切换接口，请使用Windows设置。无线电关闭状态不会被当作飞行模式。", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.LightGray, Margin = new Thickness(0, 20, 0, 10) });
        var settings = new WrapPanel(); SettingsButton(settings, "Wi-Fi设置", "ms-settings:network-wifi"); SettingsButton(settings, "蓝牙设置", "ms-settings:bluetooth"); SettingsButton(settings, "飞行模式设置", "ms-settings:network-airplanemode"); root.Children.Add(settings);
        Loaded += async (_, _) => await RefreshAsync();
        Closed += (_, _) => { _closed = true; _service.Changed -= Changed; _service.Dispose(); };
        _service.Changed += Changed;
    }
    private void Changed()
    {
        if (_closed || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(new Action(async () => await RefreshAsync()));
    }
    private async Task RefreshAsync()
    {
        if (_closed) return;
        if (_refreshing) { _refreshAgain = true; return; }
        _refreshing = true;
        try
        {
            var snapshot = await _service.ReadAsync(); if (_closed) return;
            _radios.Children.Clear();
            foreach (var radio in snapshot.Radios.Where(r => r.Kind is "WiFi" or "Bluetooth"))
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 12) }; row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                row.Children.Add(new TextBlock { Text = radio.Name + " · " + radio.Kind + "\n" + StateText(radio.State), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center });
                var button = new Button { Content = radio.State == "On" ? "关闭" : "打开", IsEnabled = radio.CanToggle, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(12, 0, 0, 0) };
                button.Click += async (_, _) =>
                {
                    button.IsEnabled = false;
                    var result = await _service.SetRadioStateAsync(radio.Id, radio.State != "On");
                    if (_closed) return;
                    await RefreshAsync(); _status.Text = result.Message + " · " + StateText(result.ActualState);
                };
                Grid.SetColumn(button, 1); row.Children.Add(button); _radios.Children.Add(row);
            }
            _status.Text = string.Join("\n", snapshot.Warnings);
            _devices.Children.Clear();
            _devices.Children.Add(Label(snapshot.WifiConnections.Count == 0 ? "当前没有可读取的Wi-Fi连接" : "Wi-Fi：" + string.Join("；", snapshot.WifiConnections)));
            foreach (var device in snapshot.BluetoothDevices)
                _devices.Children.Add(Label(device.Name + " · 已配对 · " + (device.Connected == true ? "已连接" : device.Connected == false ? "未连接" : "连接状态未提供")));
            if (snapshot.BluetoothDevices.Count == 0) _devices.Children.Add(Label("系统没有提供可读取的已配对蓝牙设备"));
        }
        catch (Exception ex) { if (!_closed) _status.Text = "无线设备读取失败：" + ex.Message; }
        finally { _refreshing = false; if (_refreshAgain && !_closed) { _refreshAgain = false; _ = RefreshAsync(); } }
    }
    private static string StateText(string state) => state switch { "On" => "已打开", "Off" => "已关闭", "Disabled" => "硬件或系统策略已禁用", _ => "状态未提供" };
    private static TextBlock Label(string text) => new() { Text = text, Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private void SettingsButton(Panel panel, string title, string uri)
    {
        var button = new Button { Content = title, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 8, 8) };
        button.Click += (_, _) => { try { _service.ResetAccessStatus(); Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); } catch (Exception ex) { _status.Text = "设置无法打开：" + ex.Message; } };
        panel.Children.Add(button);
    }
}
