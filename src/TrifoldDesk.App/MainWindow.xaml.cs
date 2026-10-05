using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using TrifoldDesk.Services;

namespace TrifoldDesk;
public partial class MainWindow : Window
{
    private readonly ConfigManager _config;
    private readonly MainViewModel _viewModel = new();
    private readonly AppSettings _settings;
    private readonly HandleWindow _handle;
    private readonly SystemMonitorService _monitor;
    private readonly DiskMonitorService _disks;
    private readonly SystemActionService _actions = new();
    private readonly TrayService _tray;
    private readonly DispatcherTimer _idleTimer, _hoverTimer, _controlTimer;
    private bool _ready, _syncingControls, _audioMuted, _readingBrightness;
    private BrightnessState? _brightness;
    private bool _brightnessDirty, _syncingBrightness;
    private DateTime _leftAt = DateTime.UtcNow;
    private bool _collapsed, _closing, _menuOpen, _dialogOpen, _dragging, _docking;
    private int _animationVersion;
    private string? _loadWarning;

    public MainWindow()
    {
        InitializeComponent(); DataContext = _viewModel;
        Icon=System.Windows.Media.Imaging.BitmapFrame.Create(new Uri("pack://application:,,,/Assets/AppIcon.ico"));
        _config = new ConfigManager(App.DataDirectory);
        var settings = _config.Load<AppSettings>("settings.json"); _settings = settings.Value;
        if(!App.IsSelfTest)_settings.StartWithWindows=SystemActionService.AutoStartRegistered();
        _settings.GlassOpacity = Math.Clamp(_settings.GlassOpacity, .05, .55);
        var shortcuts = _config.Load<ShortcutConfig>("shortcuts.json");
        _loadWarning = string.Join("\n", new[] { settings.Warning, shortcuts.Warning }.Where(w => w != null));
        foreach (var item in (shortcuts.Value.Items ?? []).Where(i => i != null && i.ScreenIndex is 0 or 1).OrderBy(i => i.Order))
        { try { Collection(item.ScreenIndex).Add(new ShortcutViewModel(item)); } catch (Exception ex) { App.Log(ex); _loadWarning += "\n部分入口加载失败，请检查日志。"; } }
        _viewModel.RebuildLibrary();
        _handle = new HandleWindow { Topmost = _settings.AlwaysOnTop };
        _handle.ToggleRequested += () => { if (_collapsed) Reveal(); else SetCollapsed(true); };
        _handle.RevealRequested += Reveal; _handle.SettingsRequested += OpenSettings; _handle.ExitRequested += Exit;
        _hoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _hoverTimer.Tick += (_, _) => { _hoverTimer.Stop(); if (_settings.HoverExpand && _handle.IsMouseOver && !_handle.MenuIsOpen) RevealPanel(false); };
        _handle.Entered += () => { _idleTimer?.Stop(); if (_collapsed && _settings.HoverExpand) _hoverTimer.Start(); };
        _handle.LeftHandle += () => { _hoverTimer.Stop(); _leftAt = DateTime.UtcNow; _idleTimer?.Start(); };
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _idleTimer.Tick += IdleTick;
        _monitor = new SystemMonitorService(Dispatcher) { SelectedNetworkId = _settings.NetworkInterfaceId };
        _monitor.Updated += snapshot => { MonitorSampleReceived = true; _viewModel.Update(snapshot); };
        _disks = new DiskMonitorService(Dispatcher); _disks.Updated += _viewModel.UpdateDrives;
        _controlTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _controlTimer.Tick += (_, _) => { RefreshAudio(); _viewModel.RefreshClock(); };
        _tray = new TrayService(() => Dispatcher.Invoke(Reveal), () => Dispatcher.Invoke(OpenSettings), () => Dispatcher.Invoke(Exit));
        Topmost = _settings.AlwaysOnTop; ApplyGlass(false); _ready = true; InitializePlugins(); InitializePaneMenus(); HubTabs.SelectedIndex = 3;
        Loaded += (_, _) =>
        {
            _handle.Show(); Dock(); SetCollapsed(_settings.IsCollapsed, false);
            if(!_settings.AlwaysOnTop) { WindowDockService.KeepAtDesktop(this);WindowDockService.KeepAtDesktop(_handle); }
            if (!string.IsNullOrWhiteSpace(_loadWarning)) { MessageBox.Show(this, _loadWarning, "配置恢复提示"); _viewModel.Status = "配置读取异常，原文件已保留。"; }
            _leftAt = DateTime.UtcNow; _idleTimer.Start();
        };
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
    }
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e); WindowDockService.ApplyToolWindow(this);
        HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
    }
    private IntPtr WindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if(msg==0x0046&&!_settings.AlwaysOnTop)WindowDockService.ProtectDesktopOrder(lParam);
        if (msg is 0x02E0 or 0x007E or 0x001A) Dispatcher.BeginInvoke(() => { if (!_closing && !_docking) Dock(); });
        return IntPtr.Zero;
    }
    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() => { if (!_closing) Dock(); });
    private void Dock()
    {
        if (_docking) return; _docking = true;
        try { WindowDockService.Dock(this, _handle, _settings.MonitorDevice); }
        finally { _docking = false; }
    }
    private ObservableCollection<ShortcutViewModel> Collection(int screen) => screen == 0 ? _viewModel.Apps : _viewModel.Projects;
    private bool SaveShortcuts()
    {
        foreach (var collection in new[] { _viewModel.Apps, _viewModel.Projects })
            for (int index = 0; index < collection.Count; index++) collection[index].Item.Order = index;
        _viewModel.RebuildLibrary();
        try { _config.Save("shortcuts.json", new ShortcutConfig { Items = _viewModel.Apps.Concat(_viewModel.Projects).Select(v => v.Item).ToList() }); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Fail("入口暂未保存", ex); return false; }
    }
    private bool SaveSettings()
    {
        try { _config.Save("settings.json", _settings); return true; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Fail("设置暂未保存", ex); return false; }
    }
    private void Fail(string title, Exception error) { App.Log(error); _viewModel.Status = title + "：" + error.Message; MessageBox.Show(error.Message, title, MessageBoxButton.OK, MessageBoxImage.Warning); }
    private void AddPaths(IEnumerable<string> paths, int screen)
    {
        int added = 0, skipped = 0; var errors = new List<string>();
        foreach (string path in paths)
        {
            try
            {
                if (!File.Exists(path) && !Directory.Exists(path)) { errors.Add(Path.GetFileName(path) + " 不存在"); continue; }
                if (ShortcutRules.IsDuplicate(Collection(screen).Select(v => v.Item), path, screen)) { skipped++; continue; }
                var item = ShellLinkHelper.CreateShortcut(path, screen, Collection(screen).Count);
                Collection(screen).Add(new ShortcutViewModel(item)); added++;
            }
            catch (Exception ex) { App.Log(ex); errors.Add(Path.GetFileName(path) + "：" + ex.Message); }
        }
        bool saved = added == 0 || SaveShortcuts();
        _viewModel.Status = $"已添加 {added} 项，跳过重复 {skipped} 项" + (saved ? "" : " · 尚未保存") + (errors.Count > 0 ? $" · 失败 {errors.Count} 项" : "");
        if (errors.Count > 0) MessageBox.Show(string.Join("\n", errors), "部分入口未能添加");
    }
    private void AddClick(object sender, RoutedEventArgs e)
    {
        _dialogOpen = true;
        try
        {
            var menu = new ContextMenu();
            int screen = int.Parse((string)((Button)sender).Tag);
            var file = new MenuItem { Header = "添加文件或快捷方式…" }; file.Click += (_, _) => ChooseFiles(screen);
            var folder = new MenuItem { Header = "添加文件夹…" }; folder.Click += (_, _) => ChooseFolder(screen);
            menu.Items.Add(file); menu.Items.Add(folder); menu.Closed += (_, _) => { _dialogOpen = false; _leftAt = DateTime.UtcNow; };
            menu.PlacementTarget = (Button)sender; menu.IsOpen = true;
        }
        catch { _dialogOpen = false; throw; }
    }
    private void ChooseFiles(int screen)
    {
        _dialogOpen = true;
        try { var dialog = new OpenFileDialog { Multiselect = true, Title = "添加桌面入口", Filter = "所有文件|*.*" }; if (dialog.ShowDialog(this) == true) AddPaths(dialog.FileNames, screen); }
        finally { _dialogOpen = false; _leftAt = DateTime.UtcNow; }
    }
    private void ChooseFolder(int screen)
    {
        _dialogOpen = true;
        try { var dialog = new OpenFolderDialog { Title = "添加项目文件夹" }; if (dialog.ShowDialog(this) == true) AddPaths([dialog.FolderName], screen); }
        finally { _dialogOpen = false; _leftAt = DateTime.UtcNow; }
    }
    private void PanelDragEnter(object sender, DragEventArgs e)
    {
        bool accepted = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = accepted ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true;
        _dragging = accepted; ApplyGlass(false);
        ((Border)sender).BorderBrush = accepted ? (Brush)FindResource("Accent") : new SolidColorBrush(Color.FromArgb(53, 255, 255, 255));
    }
    private void PanelDragLeave(object sender, DragEventArgs e) { _dragging = false; ResetDropBorder(sender); }
    private static void ResetDropBorder(object sender) => ((Border)sender).BorderBrush = new SolidColorBrush(Color.FromArgb(53, 255, 255, 255));
    private void PanelDrop(object sender, DragEventArgs e)
    {
        _dragging = false; ResetDropBorder(sender); e.Handled = true;
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths, int.Parse((string)((Border)sender).Tag));
    }
    private void ShortcutClick(object sender, RoutedEventArgs e) { if (_libraryDragging) { _libraryDragging = false; return; } Launch((ShortcutViewModel)((Button)sender).DataContext); }
    private void Launch(ShortcutViewModel shortcut)
    { try { ShellLinkHelper.Launch(shortcut.Item); _viewModel.Status = "已打开 " + shortcut.Name; } catch (Exception ex) { shortcut.Refresh(); Fail("打开失败", ex); } }
    private static ShortcutViewModel ContextItem(object sender)
    {
        var menu = (ContextMenu)((MenuItem)sender).Parent;
        return (ShortcutViewModel)((FrameworkElement)menu.PlacementTarget).DataContext;
    }
    private void MenuOpen(object sender, RoutedEventArgs e) => Launch(ContextItem(sender));
    private void MenuShowFolder(object sender, RoutedEventArgs e) { try { ShellLinkHelper.ShowInFolder(ContextItem(sender).Item); } catch (Exception ex) { Fail("无法打开位置", ex); } }
    private void MenuRemove(object sender, RoutedEventArgs e)
    {
        var shortcut = ContextItem(sender); Collection(shortcut.Item.ScreenIndex).Remove(shortcut);
        if (SaveShortcuts()) _viewModel.Status = "已移除入口，原文件保留。";
    }
    private void MenuRename(object sender, RoutedEventArgs e)
    {
        _dialogOpen = true;
        try
        {
            var shortcut = ContextItem(sender); string? name = TextInputDialog.Ask(this, "重命名入口", shortcut.Name);
            if (name != null) { shortcut.Item.Name = name; shortcut.Refresh(); SaveShortcuts(); }
        }
        finally { _dialogOpen = false; }
    }
    private void MenuRelocate(object sender, RoutedEventArgs e)
    {
        _dialogOpen = true;
        try
        {
            var shortcut = ContextItem(sender); string? path = null;
            var choice = MessageBox.Show(this, "重新选择文件夹？\n选“否”可选择文件或快捷方式。", "重新定位", MessageBoxButton.YesNoCancel);
            if (choice == MessageBoxResult.Yes) { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) == true) path = dialog.FolderName; }
            else if (choice == MessageBoxResult.No) { var dialog = new OpenFileDialog(); if (dialog.ShowDialog(this) == true) path = dialog.FileName; }
            if (path == null) return;
            if (Collection(shortcut.Item.ScreenIndex).Any(v => v != shortcut && string.Equals(v.Item.SourcePath, path, StringComparison.OrdinalIgnoreCase))) { MessageBox.Show("该区域已经有此入口。"); return; }
            var replacement = ShellLinkHelper.CreateShortcut(path, shortcut.Item.ScreenIndex, shortcut.Item.Order);
            replacement.Id = shortcut.Item.Id; replacement.Name = shortcut.Name; replacement.Group = shortcut.Item.Group;
            var items = Collection(shortcut.Item.ScreenIndex); int index = items.IndexOf(shortcut); items[index] = new ShortcutViewModel(replacement); SaveShortcuts();
        }
        catch (Exception ex) { Fail("重新定位失败", ex); }
        finally { _dialogOpen = false; }
    }
    private void ShortcutMenuOpening(object sender, ContextMenuEventArgs e) => _menuOpen = true;
    private void ShortcutMenuClosing(object sender, ContextMenuEventArgs e) { _menuOpen = false; _leftAt = DateTime.UtcNow; }
    private void AwakeClick(object sender, RoutedEventArgs e)
    { try { _actions.SetAwake(!_actions.IsAwake); _viewModel.IsAwake = _actions.IsAwake; } catch (Exception ex) { Fail("常亮切换失败", ex); } }
    private async void RecycleClick(object sender, RoutedEventArgs e)
    {
        _dialogOpen = true; var button = (Button)sender;
        try
        {
            if (MessageBox.Show(this, "永久清空所有驱动器的回收站？此操作不可撤销。", "清空回收站", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            button.IsEnabled = false; await Task.Run(SystemActionService.EmptyRecycleBin); _viewModel.Status = "回收站已清空。";
        }
        catch (Exception ex) { Fail("清空回收站失败", ex); }
        finally { button.IsEnabled = true; _dialogOpen = false; _leftAt = DateTime.UtcNow; }
    }
    private void SettingsClick(object sender, RoutedEventArgs e) => OpenSettings();
    public void OpenSettings()
    {
        if (_dialogOpen) return; Reveal(); _dialogOpen = true;
        try
        {
            var window = new SettingsWindow(_settings) { Owner = this };
            if (window.ShowDialog() == true)
            {
                var updated = window.Result;
                if (!App.IsSelfTest)
                {
                    try { SystemActionService.SetAutoStart(updated.StartWithWindows); }
                    catch (Exception ex) { Fail("开机自启设置失败", ex); updated.StartWithWindows = _settings.StartWithWindows; }
                }
                _settings.GlassOpacity = updated.GlassOpacity; _settings.HoverExpand = updated.HoverExpand; _settings.AutoCollapse = updated.AutoCollapse;
                _settings.AlwaysOnTop = updated.AlwaysOnTop; _settings.AnimationsEnabled = updated.AnimationsEnabled; _settings.TransparentIdle = updated.TransparentIdle;
                _settings.StartWithWindows = updated.StartWithWindows; _settings.MonitorDevice = updated.MonitorDevice; _settings.NetworkInterfaceId = updated.NetworkInterfaceId;
                Topmost = _handle.Topmost = _settings.AlwaysOnTop; _monitor.SelectedNetworkId = _settings.NetworkInterfaceId;
                if(!_settings.AlwaysOnTop){WindowDockService.KeepAtDesktop(this);WindowDockService.KeepAtDesktop(_handle);}
                ApplyGlass(false); Dock(); SaveSettings();
            }
        }
        finally { _dialogOpen = false; _leftAt = DateTime.UtcNow; }
    }
    public void Reveal() => RevealPanel(true);
    private void RevealPanel(bool userRequested)
    {
        if (_closing) return;
        if (WindowState != WindowState.Normal) WindowState = WindowState.Normal;
        SetCollapsed(false);
        if (userRequested) { if(_settings.AlwaysOnTop)Activate();else WindowDockService.KeepAtDesktop(this); WindowDockService.RaiseHandle(_handle); }
    }
    private void SetCollapsed(bool collapsed, bool animate = true)
    {
        if (_foldStateInitialized && _collapsed == collapsed && _foldOverlay == null && IsVisible != collapsed) return;
        _foldStateInitialized = true;
        _collapsed = collapsed; _settings.IsCollapsed = collapsed; _handle.SetCollapsed(collapsed);
        _handle.Visibility = collapsed ? Visibility.Visible : Visibility.Hidden;
        _monitor.SetCollapsed(collapsed); _hoverTimer.Stop();
        int version = ++_animationVersion;
        double current = PanelTranslation.X;
        PanelTranslation.BeginAnimation(TranslateTransform.XProperty, null);
        if (!collapsed && !IsVisible) { Show(); Dock(); current = ActualWidth; }
        ApplyGlass(false);
        double target = collapsed ? ActualWidth : 0;
        if (!animate || !_settings.AnimationsEnabled)
        { ClearFold(); PanelTranslation.X = target; if (collapsed) Hide(); }
        else
        {
            PanelTranslation.X = current;
            StartFold(collapsed);
            var animation = new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }, FillBehavior = FillBehavior.Stop };
            animation.Completed += (_, _) =>
            {
                if (version != _animationVersion) return;
                PanelTranslation.BeginAnimation(TranslateTransform.XProperty, null); PanelTranslation.X = target;
                ClearFold();
                if (collapsed) Hide();
            };
            PanelTranslation.BeginAnimation(TranslateTransform.XProperty, animation);
        }
        _leftAt = DateTime.UtcNow; SaveSettings();
    }
    private void ApplyGlass(bool idle)
    {
        double alpha = _settings.GlassOpacity * (idle && _settings.TransparentIdle ? .45 : 1);
        // Only the background changes; foreground content remains readable.
        Resources["GlassFill"] = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromArgb((byte)(alpha * 255), 255, 255, 255), 0),
            new(Color.FromArgb((byte)(Math.Min(.52, alpha + .15) * 255), 15, 30, 43), 1)
        }, new Point(0, 0), new Point(1, 1));
    }
    private void PanelMouseEnter(object sender, MouseEventArgs e) { _idleTimer.Stop(); ApplyGlass(false); foreach (var item in _viewModel.Apps.Concat(_viewModel.Projects)) item.Refresh(); }
    private void PanelMouseLeave(object sender, MouseEventArgs e) { _leftAt = DateTime.UtcNow; _idleTimer.Start(); }
    private void IdleTick(object? sender, EventArgs e)
    {
        _viewModel.RefreshClock();
        if (_collapsed || IsMouseOver || _handle.IsMouseOver || _menuOpen || _handle.MenuIsOpen || _dialogOpen || _dragging || IsKeyboardFocusWithin) return;
        if (_frames.Values.Any(f => f.InteractionActive) || OwnedWindows.Cast<Window>().Any(w => w.IsVisible && w.IsActive)) { _leftAt = DateTime.UtcNow; return; }
        double seconds = (DateTime.UtcNow - _leftAt).TotalSeconds;
        if (seconds > 1.2) ApplyGlass(true);
        if (seconds > 3 && _settings.AutoCollapse) SetCollapsed(true);
    }
    public void Exit()
    {
        if (_closing) return; _closing = true;
        SaveShortcuts(); SaveSettings();
        _idleTimer.Stop(); _hoverTimer.Stop(); _controlTimer.Stop(); _monitor.Dispose(); _disks.Dispose();
        SystemEvents.DisplaySettingsChanged -= DisplayChanged;
        try { _actions.Dispose(); } catch (Exception ex) { App.Log(ex); }
        _tray.Dispose(); _handle.Close(); Close(); Application.Current.Shutdown();
    }
    private void WindowClosing(object? sender, CancelEventArgs e) { if (!_closing) { e.Cancel = true; SetCollapsed(true); } }
    internal bool HandleIsVisible => _handle.IsVisible;
    internal bool CollapsedForTest => _collapsed;
    internal bool MonitorSampleReceived { get; private set; }
    internal void TestCollapse() => SetCollapsed(true);
    internal void ExportPreview(string path)
    {
        PanelRoot.UpdateLayout();
        int width = Math.Max(1, (int)Math.Ceiling(ActualWidth)), height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        // Synthetic backdrop for visual QA; this is not a capture of the user's wallpaper.
        var scene = new DrawingVisual();
        using (var drawing = scene.RenderOpen())
        {
            drawing.DrawRectangle(new LinearGradientBrush(Color.FromRgb(18, 32, 49), Color.FromRgb(43, 68, 68), 35), null, new Rect(0, 0, width, height));
            drawing.DrawEllipse(new SolidColorBrush(Color.FromRgb(34, 72, 78)), null, new Point(width * .72, height * .22), width * .3, height * .3);
            drawing.DrawRectangle(new VisualBrush(PanelRoot), null, new Rect(12, 12, Math.Max(1, width - 24), Math.Max(1, height - 24)));
        }
        bitmap.Render(scene);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var stream = File.Create(path); encoder.Save(stream);
    }
    internal void TestAddPaths(string[] paths, int screen) => AddPaths(paths, screen);
    internal int TestShortcutCount(int screen) => Collection(screen).Count;
    internal void TestRemoveShortcut(int screen, int index) { Collection(screen).RemoveAt(index); SaveShortcuts(); }
}
