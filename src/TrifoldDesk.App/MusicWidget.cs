using Windows.Media.Control;
using System.Windows.Media.Imaging;

namespace TrifoldDesk;
public sealed class MusicWidget : Border, IProfileReplacementParticipant
{
    private readonly TextBlock _title = new() { Text = "音乐", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _artist = new() { Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
    private readonly TextBlock _status = new() { Foreground = Brushes.Gray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Image _cover = new() { Width = 92, Height = 92, Stretch = Stretch.UniformToFill };
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _session;
    private static readonly Dictionary<string, string?> PreferredSources = new();
    private readonly string _instanceId;
    private string? _preferredSource;
    private bool _active, _queued, _refreshAgain;
    private bool _retired;
    private int _generation;
    public MusicWidget(string instanceId, ConfigManager store, string style = "glass")
    {
        ArgumentNullException.ThrowIfNull(store);
        _instanceId = instanceId;
        PreferredSources.TryGetValue(instanceId, out _preferredSource);
        Tag = instanceId; Background = Brushes.Transparent; Padding = new Thickness(10);
        var root = new Grid(); root.ColumnDefinitions.Add(new() { Width = new GridLength(104) }); root.ColumnDefinitions.Add(new());
        var art = new Grid { Width = 92, Height = 92, VerticalAlignment = VerticalAlignment.Top };
        art.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(35, 120, 160, 180)), CornerRadius = new CornerRadius(8) });
        art.Children.Add(new TextBlock { Text = "♪", FontSize = 42, Foreground = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }); art.Children.Add(_cover); root.Children.Add(art);
        bool darkText = TextContrast.IsDarkTextStyle(style);
        _title.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(darkText ? "#1E353D" : "#FFFFFF"));
        _artist.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(darkText ? "#48606A" : "#E2EEF5"));
        _status.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(darkText ? "#5A727C" : "#B8CBD6"));
        if (style == "glass")
        {
            _title.Effect = TextContrast.Shadow;
            _artist.Effect = TextContrast.Shadow;
            _status.Effect = TextContrast.Shadow;
        }
        var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        Button ControlButton(string symbol, string tooltip) => new Button
        {
            Content = new TextBlock { Text = symbol, FontSize = 12, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(darkText ? "#1E353D" : "#F0F7FA")) },
            ToolTip = tooltip,
            Width = 32,
            Height = 24,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 6, 0),
            Background = new SolidColorBrush(darkText ? Color.FromArgb(28, 30, 50, 60) : Color.FromArgb(28, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(darkText ? Color.FromArgb(75, 30, 50, 60) : Color.FromArgb(65, 255, 255, 255))
        };
        var prevBtn = ControlButton("⏮", "上一首");
        var playBtn = ControlButton("⏯", "播放 / 暂停");
        var nextBtn = ControlButton("⏭", "下一首");
        prevBtn.Click += async (_, _) => { var s = _session; if (s != null) try { await s.TrySkipPreviousAsync(); QueueRefresh(); } catch { } };
        playBtn.Click += async (_, _) => { var s = _session; if (s != null) try { await s.TryTogglePlayPauseAsync(); QueueRefresh(); } catch { } };
        nextBtn.Click += async (_, _) => { var s = _session; if (s != null) try { await s.TrySkipNextAsync(); QueueRefresh(); } catch { } };
        controls.Children.Add(prevBtn);
        controls.Children.Add(playBtn);
        controls.Children.Add(nextBtn);
        var text = new StackPanel(); text.Children.Add(_title); text.Children.Add(_artist); text.Children.Add(controls); text.Children.Add(_status); Grid.SetColumn(text, 1); root.Children.Add(text); Child = root;
        ContextMenu = new ContextMenu(); ContextMenu.Opened += (_, _) => BuildMenu();
        Loaded += OnLoaded; Unloaded += OnUnloaded; Empty("暂无系统媒体会话");
    }
    private async void OnLoaded(object sender, RoutedEventArgs args)
    {
        if (_retired || _active) return; _active = true; int generation = ++_generation;
        try
        {
            var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (!_active || generation != _generation) return;
            _manager = manager; manager.CurrentSessionChanged += ManagerChanged; manager.SessionsChanged += SessionsChanged;
            ChooseSession();
        }
        catch (Exception ex) { if (_active && generation == _generation) Empty("媒体会话不可用：" + ex.Message); }
    }
    private void OnUnloaded(object sender, RoutedEventArgs args)
    {
        _active = false; _generation++; _queued = false;
        if (_manager != null) { _manager.CurrentSessionChanged -= ManagerChanged; _manager.SessionsChanged -= SessionsChanged; _manager = null; }
        BindSession(null);
    }
    public void PrepareForProfileReplacement(){_retired=true;OnUnloaded(this,new RoutedEventArgs());}
    private void ManagerChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args) => QueueChoose();
    private void SessionsChanged(GlobalSystemMediaTransportControlsSessionManager sender, SessionsChangedEventArgs args) => QueueChoose();
    private void QueueChoose()
    {
        if (!_active || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(new Action(() => { if (_active) ChooseSession(); }));
    }
    private void ChooseSession()
    {
        try
        {
            var session = _preferredSource == null ? _manager?.GetCurrentSession() ?? _manager?.GetSessions().FirstOrDefault()
                : _manager?.GetSessions().FirstOrDefault(s => s.SourceAppUserModelId == _preferredSource);
            BindSession(session);
            if (session == null) Empty(_preferredSource == null ? "暂无系统媒体会话" : "所选播放器会话已结束");
            else QueueRefresh();
        }
        catch (Exception ex) { BindSession(null); Empty("媒体会话读取失败：" + ex.Message); }
    }
    private void BindSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (ReferenceEquals(_session, session)) return;
        _generation++;
        if (_session != null) { _session.MediaPropertiesChanged -= MediaChanged; _session.PlaybackInfoChanged -= PlaybackChanged; }
        _session = session;
        if (session != null) { session.MediaPropertiesChanged += MediaChanged; session.PlaybackInfoChanged += PlaybackChanged; }
    }
    private void MediaChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args) => QueueRefresh();
    private void PlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args) => QueueRefresh();
    private void QueueRefresh()
    {
        if (!_active || Dispatcher.HasShutdownStarted) return;
        _ = Dispatcher.BeginInvoke(new Action(() => { if (!_active) return; if (_queued) { _refreshAgain = true; return; } _queued = true; _ = RefreshAsync(); }));
    }
    private async Task RefreshAsync()
    {
        var session = _session; int generation = ++_generation;
        if (session == null) { _queued = false; return; }
        try
        {
            var media = await session.TryGetMediaPropertiesAsync();
            var playback = session.GetPlaybackInfo();
            if (media == null || playback == null)
            {
                if (_active && generation == _generation && ReferenceEquals(session, _session)) Empty("媒体信息暂不可用");
                return;
            }
            BitmapImage? cover = null;
            string? coverWarning = null;
            if (media.Thumbnail != null)
            {
                try
                {
                    using var random = await media.Thumbnail.OpenReadAsync();
                    if (random.Size > 16 * 1024 * 1024) throw new InvalidDataException("封面超过大小限制");
                    using var input = random.AsStreamForRead(); using var memory = new MemoryStream();
                    await input.CopyToAsync(memory); memory.Position = 0;
                    cover = new BitmapImage(); cover.BeginInit(); cover.CacheOption = BitmapCacheOption.OnLoad; cover.DecodePixelWidth = 256; cover.StreamSource = memory; cover.EndInit(); cover.Freeze();
                }
                catch (Exception ex) { coverWarning = "封面不可用：" + ex.Message; }
            }
            if (!_active || generation != _generation || !ReferenceEquals(session, _session)) return;
            _title.Text = string.IsNullOrWhiteSpace(media.Title) ? "播放器未提供曲名" : media.Title;
            _artist.Text = string.Join(" · ", new[] { media.Artist, media.AlbumTitle }.Where(s => !string.IsNullOrWhiteSpace(s)));
            _status.Text = PlaybackText(playback.PlaybackStatus) + " · " + session.SourceAppUserModelId + (coverWarning == null ? "" : "\n" + coverWarning);
            _cover.Source = cover;
        }
        catch (Exception ex) { if (_active && generation == _generation) Empty("媒体信息读取失败：" + ex.Message); }
        finally { _queued = false; if (_active && _refreshAgain) { _refreshAgain = false; QueueRefresh(); } }
    }
    private static string PlaybackText(GlobalSystemMediaTransportControlsSessionPlaybackStatus status) => status switch
    { GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => "播放中", GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => "已暂停", GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped => "已停止", GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed => "会话已关闭", _ => "播放器未提供播放状态" };
    private void Empty(string status) { _title.Text = "音乐"; _artist.Text = "在支持系统媒体会话的播放器开始播放"; _status.Text = status; _cover.Source = null; }
    private void BuildMenu()
    {
        ContextMenu!.Items.Clear();
        var session = _session;
        try
        {
            var controls = session?.GetPlaybackInfo()?.Controls;
            ActionMenu("播放 / 暂停", controls?.IsPlayPauseToggleEnabled == true, s => s.TryTogglePlayPauseAsync().AsTask());
            ActionMenu("上一首", controls?.IsPreviousEnabled == true, s => s.TrySkipPreviousAsync().AsTask());
            ActionMenu("下一首", controls?.IsNextEnabled == true, s => s.TrySkipNextAsync().AsTask());
            ContextMenu.Items.Add(new Separator());
            var current = new MenuItem { Header = "跟随系统当前会话", IsCheckable = true, IsChecked = _preferredSource == null };
            current.Click += (_, _) => { _preferredSource = null; PreferredSources.Remove(_instanceId); ChooseSession(); }; ContextMenu.Items.Add(current);
            foreach (var source in _manager?.GetSessions().Select(s => s.SourceAppUserModelId).Distinct().Take(20) ?? [])
            {
                var option = new MenuItem { Header = source, IsCheckable = true, IsChecked = _preferredSource == source };
                option.Click += (_, _) => { _preferredSource = source; PreferredSources[_instanceId] = source; ChooseSession(); }; ContextMenu.Items.Add(option);
            }
            var refresh = new MenuItem { Header = "刷新媒体信息" }; refresh.Click += (_, _) => ChooseSession(); ContextMenu.Items.Add(refresh);
        }
        catch (Exception ex) { _status.Text = "媒体控制不可用：" + ex.Message; }
    }
    private void ActionMenu(string name, bool enabled, Func<GlobalSystemMediaTransportControlsSession, Task<bool>> command)
    {
        var item = new MenuItem { Header = name, IsEnabled = enabled && _session != null };
        item.Click += async (_, _) =>
        {
            var session = _session; if (session == null) return;
            item.IsEnabled = false;
            try { if (!await command(session)) _status.Text = "播放器未接受此操作"; else QueueRefresh(); }
            catch (Exception ex) { if (_active) _status.Text = "媒体控制失败：" + ex.Message; }
            finally { if (_active) item.IsEnabled = true; }
        };
        ContextMenu!.Items.Add(item);
    }
    public static async Task<IReadOnlyList<string>> ReadSessionsAsync()
    {
        var manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        return manager.GetSessions().Select(s => s.SourceAppUserModelId).ToArray();
    }
}
