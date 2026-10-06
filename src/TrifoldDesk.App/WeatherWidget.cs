using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;
using TrifoldDesk.Services;

namespace TrifoldDesk;

public sealed class WeatherWidget : StackPanel, IProfileReplacementParticipant
{
    private readonly string _instanceId;
    private readonly ConfigManager _store;
    private readonly OpenMeteoClient _client = new();
    private WeatherData _data = new();
    private readonly TextBlock _city = new() { Foreground = Brushes.WhiteSmoke, FontSize = 14, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _temperature = new() { Foreground = Brushes.WhiteSmoke, FontSize = 34, Margin = new Thickness(0, 8, 0, 2) };
    private readonly TextBlock _detail = new() { Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock _status = new() { Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, FontSize = 10, Margin = new Thickness(0, 10, 0, 0) };
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMinutes(5) };
    private CancellationTokenSource? _lifetime;
    private bool _busy;
    private bool _retired;
    private string? _readWarning;
    public WeatherWidget(string instanceId, ConfigManager store)
    {
        _instanceId = instanceId; _store = store; Margin = new Thickness(6);
        try { var loaded = store.Load<WeatherConfig>("optional-weather.json"); if (loaded.Value.Widgets?.TryGetValue(instanceId, out var saved) == true) _data = saved; _readWarning = loaded.Warning; _status.Text = loaded.Warning ?? ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { _status.Text = "读取失败：" + ex.Message; }
        Children.Add(_city); Children.Add(_temperature); Children.Add(_detail); Children.Add(_status);
        var attribution = new TextBlock { Text = "Open-Meteo · GeoNames", Foreground = Brushes.LightGray, FontSize = 10, Opacity = .65, Margin = new Thickness(0, 4, 0, 0), ToolTip = "天气：open-meteo.com · 城市：GeoNames" }; Children.Add(attribution);
        var menu = new ContextMenu(); Menu(menu, "选择城市…", async () => await ChooseCity()); Menu(menu, "刷新（30分钟缓存）", async () => await Refresh()); ContextMenu = menu;
        _clock.Tick += async (_, _) => await Refresh();
        Loaded += async (_, _) => { if(_retired)return;_lifetime?.Dispose(); _lifetime = new(); _clock.Start(); await Refresh(); };
        Unloaded += (_, _) => { _clock.Stop(); _lifetime?.Cancel(); };
        Render();
    }
    private static void Menu(ContextMenu menu, string title, Func<Task> action)
    {
        var item = new MenuItem { Header = title }; item.Click += async (_, _) => await action(); menu.Items.Add(item);
    }
    private async Task ChooseCity()
    {
        var owner = Window.GetWindow(this); if (owner == null || _lifetime == null || _busy) return;
        var query = TextInputDialog.Ask(owner, "搜索城市（例如：北京 / Paris, France）", _data.City?.Name ?? ""); if (query == null) return;
        _busy = true;
        try
        {
            _status.Text = "搜索城市…"; var cities = await _client.SearchAsync(query, _lifetime.Token);
            if (!IsLoaded) return;
            if (cities.Count == 0) { _status.Text = "未找到城市，请更换名称或加上国家 / 地区。"; return; }
            var picker = new Window { Owner = owner, Title = "选择城市", Width = 420, Height = 320, WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false };
            var panel = new DockPanel { Margin = new Thickness(14) }; picker.Content = panel;
            var choose = new Button { Content = "使用所选城市", Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(choose, Dock.Bottom); panel.Children.Add(choose);
            var list = new ListBox { ItemsSource = cities, SelectedIndex = 0 }; panel.Children.Add(list); choose.Click += (_, _) => picker.DialogResult = true;
            if (picker.ShowDialog() != true || list.SelectedItem is not WeatherCity selected) return;
            var next = WeatherRules.SelectCity(_data, selected); Save(next); _data = next; Render();
        }
        catch (OperationCanceledException) { if (IsLoaded) _status.Text = "城市搜索已取消或超时。"; }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or JsonException or ArgumentException) { _status.Text = "城市选择失败：" + ex.Message; }
        finally { _busy = false; }
        await Refresh();
    }
    private async Task Refresh()
    {
        if (_retired || _busy || _lifetime == null || _lifetime.IsCancellationRequested || !WeatherRules.NeedsRefresh(_data, DateTime.UtcNow)) return;
        _busy = true;
        try
        {
            _data.LastAttemptUtc = DateTime.UtcNow; Save(_data);
            var observation = await _client.ForecastAsync(_data.City!, _lifetime.Token);
            if (_retired || _lifetime.IsCancellationRequested || !IsLoaded) return;
            _data.Cache = observation; Save(_data); _status.Text = ""; Render();
        }
        catch (OperationCanceledException) { if (IsLoaded) { Render(); _status.Text += "\n请求已取消或超时 · 保留上次数据"; } }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or JsonException or ArgumentException) { Render(); _status.Text += "\n离线 / 更新失败 · 保留上次数据\n" + ex.Message; }
        finally { _busy = false; }
    }
    private void Save(WeatherData state)
    {
        if(_retired)return;
        var config = _store.Load<WeatherConfig>("optional-weather.json").Value; config.Widgets ??= [];
        config.Widgets[_instanceId] = state; _store.Save("optional-weather.json", config);
    }
    private void Render()
    {
        if (!WeatherRules.ValidCity(_data.City)) { _city.Text = "尚未选择城市"; _temperature.Text = "—"; _detail.Text = "右键选择城市以查看天气"; return; }
        _city.Text = _data.City!.ToString(); var cache = _data.Cache;
        _temperature.Text = cache?.TemperatureC is { } temperature ? temperature.ToString("0.#") + "°C" : "—";
        if (cache == null) { _detail.Text = "暂无天气数据"; return; }
        _detail.Text = WeatherRules.Description(cache.WeatherCode) + "\n湿度 " + (cache.RelativeHumidity?.ToString("0") ?? "不可用") + (cache.RelativeHumidity != null ? "%" : "") + " · 风速 " + (cache.WindKmh?.ToString("0.#") ?? "不可用") + (cache.WindKmh != null ? " km/h" : "");
        _status.Text = "数据时间 " + cache.DataLocal.ToString("MM-dd HH:mm") + " " + cache.Timezone + "\n获取时间 " + cache.RetrievedUtc.ToLocalTime().ToString("MM-dd HH:mm") + (_readWarning == null ? "" : "\n" + _readWarning);
    }
    public void PrepareForProfileReplacement(){_retired=true;_clock.Stop();_lifetime?.Cancel();}
}
