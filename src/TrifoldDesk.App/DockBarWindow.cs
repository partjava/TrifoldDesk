using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using TrifoldDesk.Core;
using TrifoldDesk.Services;
using Forms = System.Windows.Forms;

namespace TrifoldDesk;

public sealed class DockBarWindow : Window
{
    private readonly ConfigManager _store;
    private DockConfig _config;
    private readonly StackPanel _mainStack = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _itemsPanel = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _pill = new();
    private readonly Border _glass = new() { IsHitTestVisible = false };
    private readonly DispatcherTimer _glassTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private bool _glassRendering;
    private bool _glassClosed;
    private int _glassRevision;
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(450) };
    private readonly DispatcherTimer _edgeTriggerTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private bool _isSlidDown;
    private double _normalTop;

    // Drag-drop reordering & wave magnification state
    private bool _isDockDragging;
    private Point _dockDragStart;
    private DockItem? _dockDraggedItem;

    public event Action? LaunchpadRequested;

    public DockBarWindow(ConfigManager store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Title = "TrifoldDesk Dock";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        SizeToContent = SizeToContent.WidthAndHeight;

        var loaded = _store.Load<DockConfig>("dock.json");
        _config = loaded.Value;
        if (_config.Items.Count == 0)
        {
            _config = PopulateInitialDock(_store);
            try { _store.Save("dock.json", _config); } catch { /* ignore */ }
        }
        else
        {
            // Auto deduplicate any legacy duplicate entries
            int beforeCount = _config.Items.Count;
            _config = DockRules.Deduplicate(_config);
            if (_config.Items.Count != beforeCount)
            {
                try { _store.Save("dock.json", _config); } catch { /* ignore */ }
            }
        }

        Topmost = _config.AlwaysOnTop;

        _glassTimer.Tick += RefreshGlass;
        GlassCapturePolicy.Changed += OnGlassCaptureChanged;

        BuildUI();

        Loaded += (_, _) => Reposition();
        IsVisibleChanged += (_, _) => QueueGlassRefresh();
        DpiChanged += (_, _) => QueueGlassRefresh();

        SizeChanged += (_, _) => Reposition();
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Closed += (_, _) =>
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
            GlassCapturePolicy.Changed -= OnGlassCaptureChanged;

            _glassClosed = true;
            _glassTimer.Stop();
            _hideTimer.Stop();
            _edgeTriggerTimer.Stop();
        };

        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (_config.AutoHide && !IsMouseOver && !_isSlidDown)
            {
                SlideDock(true);
            }
        };

        MouseEnter += (_, _) =>
        {
            _hideTimer.Stop();
            if (_isSlidDown) SlideDock(false);
        };

        MouseLeave += (_, _) =>
        {
            if (_config.AutoHide && !_isSlidDown) _hideTimer.Start();
        };

        // Edge trigger monitor: when cursor approaches the screen bottom, reveal the dock smoothly
        _edgeTriggerTimer.Tick += (_, _) =>
        {
            if (!_config.AutoHide) return;

            var screen = Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
            var pt = Forms.Cursor.Position;
            var source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            int bottomThreshold = Math.Max(screen.WorkingArea.Bottom, screen.Bounds.Bottom) - 10;
            bool isAtBottomEdge = pt.Y >= bottomThreshold;

            double dockScreenLeft = Left * dpiX;
            double dockScreenRight = (Left + ActualWidth) * dpiX;
            bool isInHorizontalRange = pt.X >= (dockScreenLeft - 120) && pt.X <= (dockScreenRight + 120);

            if (_isSlidDown && isAtBottomEdge && isInHorizontalRange)
            {
                SlideDock(false);
            }
            else if (!_isSlidDown && !IsMouseOver)
            {
                // Check if cursor has wandered away
                double dockScreenTop = Top * dpiY;
                double dockScreenBottom = (Top + ActualHeight) * dpiY;
                bool isNearDock = pt.X >= (dockScreenLeft - 30) && pt.X <= (dockScreenRight + 30) &&
                                  pt.Y >= (dockScreenTop - 40) && pt.Y <= (dockScreenBottom + 40);

                if (!isNearDock && !_hideTimer.IsEnabled)
                {
                    _hideTimer.Start();
                }
            }
        };
        _edgeTriggerTimer.Start();

        AllowDrop = true;
        DragOver += (_, e) =>
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        };
        Drop += OnDrop;
    }

    private void OnDisplayChanged(object? sender, EventArgs e) => Dispatcher.InvokeAsync(Reposition);

    private void BuildUI()
    {
        _pill.CornerRadius = new CornerRadius(24);
        _pill.Padding = new Thickness(10, 5, 10, 5);
        _pill.Background = Brushes.Transparent;
        _pill.BorderBrush = new LinearGradientBrush(
            Color.FromArgb(125, 255, 255, 255),
            Color.FromArgb(24, 255, 255, 255),
            45);
        _pill.BorderThickness = new Thickness(1.2);
        _glass.CornerRadius = _pill.CornerRadius;
        _glass.Background = GlassFallback;
        var root = new Grid();
        root.Children.Add(_glass);
        root.Children.Add(_pill);

        Content = root;

        var contextMenu = new ContextMenu();
        var addInstalled = new MenuItem { Header = "＋ 从已安装应用添加…" };
        addInstalled.Click += (_, _) => OpenInstalledAppsPicker();
        contextMenu.Items.Add(addInstalled);

        var addLocal = new MenuItem { Header = "从本地文件添加 (.exe / .lnk)…" };
        addLocal.Click += (_, _) => PickLocalFile();
        contextMenu.Items.Add(addLocal);

        contextMenu.Items.Add(new Separator());

        var topCheck = new MenuItem { Header = "总是置顶", IsCheckable = true, IsChecked = _config.AlwaysOnTop };
        topCheck.Click += (_, _) =>
        {
            _config.AlwaysOnTop = topCheck.IsChecked;
            Topmost = _config.AlwaysOnTop;
            SaveConfig();
        };
        contextMenu.Items.Add(topCheck);

        var autoHideCheck = new MenuItem { Header = "鼠标离开自动隐藏", IsCheckable = true, IsChecked = _config.AutoHide };
        autoHideCheck.Click += (_, _) =>
        {
            _config.AutoHide = autoHideCheck.IsChecked;
            if (!_config.AutoHide && _isSlidDown) SlideDock(false);
            SaveConfig();
        };
        contextMenu.Items.Add(autoHideCheck);

        contextMenu.Items.Add(new Separator());

        var resetDefaults = new MenuItem { Header = "恢复默认应用列表" };
        resetDefaults.Click += (_, _) =>
        {
            _config = DockRules.CreateDefault();
            SaveConfig();
            Render();
        };
        contextMenu.Items.Add(resetDefaults);

        _pill.ContextMenu = contextMenu;

        // Wave magnification on hover
        _itemsPanel.MouseMove += OnItemsPanelMouseMove;
        _itemsPanel.MouseLeave += OnItemsPanelMouseLeave;

        // Static container structure - never reconnect _itemsPanel
        _mainStack.Children.Clear();
        _mainStack.Children.Add(CreateLaunchpadButton());

        var separator = new Border
        {
            Width = 1,
            Height = 24,
            Background = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
            Margin = new Thickness(6, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        _mainStack.Children.Add(separator);
        _mainStack.Children.Add(_itemsPanel);
        _mainStack.Children.Add(CreateAddButton());

        _pill.Child = _mainStack;

        Render();
    }

    public void Render()
    {
        _itemsPanel.Children.Clear();
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var ordered = _config.Items.OrderBy(i => i.Order).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var item = ordered[i];
            var btn = CreateItemElement(item, dpi, i == 0, i == ordered.Count - 1);
            _itemsPanel.Children.Add(btn);
        }
    }

    private FrameworkElement CreateLaunchpadButton()
    {
        var btn = new Border
        {
            Width = 44,
            Height = 44,
            CornerRadius = new CornerRadius(11),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = "应用启动台 (Launchpad)",
            Margin = new Thickness(2, 0, 2, 0),
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        var scale = new ScaleTransform(1.0, 1.0);
        btn.RenderTransform = scale;

        // 3x3 rounded squares grid icon
        var grid = new Canvas { Width = 20, Height = 20, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < 3; c++)
            {
                var dot = new Border
                {
                    Width = 4.8,
                    Height = 4.8,
                    CornerRadius = new CornerRadius(1.4),
                    Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255))
                };
                Canvas.SetLeft(dot, c * 7.5);
                Canvas.SetTop(dot, r * 7.5);
                grid.Children.Add(dot);
            }
        }
        btn.Child = grid;

        btn.MouseEnter += (_, _) =>
        {
            btn.Background = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255));
            AnimateScale(scale, 1.15);
        };
        btn.MouseLeave += (_, _) =>
        {
            btn.Background = Brushes.Transparent;
            AnimateScale(scale, 1.0);
        };
        btn.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            AnimateClick(btn, () =>
            {
                if (LaunchpadRequested != null)
                {
                    LaunchpadRequested.Invoke();
                }
                else
                {
                    OpenInstalledAppsPicker();
                }
            });
        };

        return btn;
    }

    private FrameworkElement CreateAddButton()
    {
        var btn = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Cursor = Cursors.Hand,
            ToolTip = "添加应用到 Dock",
            Margin = new Thickness(6, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        var scale = new ScaleTransform(1.0, 1.0);
        btn.RenderTransform = scale;

        var text = new TextBlock
        {
            Text = "＋",
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromArgb(210, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        btn.Child = text;

        btn.MouseEnter += (_, _) =>
        {
            btn.Background = new SolidColorBrush(Color.FromArgb(55, 255, 255, 255));
            AnimateScale(scale, 1.12);
        };
        btn.MouseLeave += (_, _) =>
        {
            btn.Background = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255));
            AnimateScale(scale, 1.0);
        };
        btn.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            AnimateClick(btn, OpenInstalledAppsPicker);
        };

        return btn;
    }

    private FrameworkElement CreateItemElement(DockItem item, double dpi, bool isFirst, bool isLast)
    {
        var container = new Border
        {
            Width = 46,
            Height = 46,
            CornerRadius = new CornerRadius(11),
            Background = Brushes.Transparent,
            Margin = new Thickness(3, 0, 3, 0),
            Cursor = Cursors.Hand,
            ToolTip = item.Name,
            RenderTransformOrigin = new Point(0.5, 0.5),
            Tag = item
        };
        var scale = new ScaleTransform(1.0, 1.0);
        container.RenderTransform = scale;

        var iconSource = GetDockIcon(item, dpi);
        if (iconSource != null)
        {
            var icon = new Image
            {
                Source = iconSource,
                SnapsToDevicePixels = true,
                UseLayoutRounding = true,
                Width = 36,
                Height = 36,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
            container.Child = icon;
        }
        else
        {
            var fallback = new Border
            {
                SnapsToDevicePixels = true,
                UseLayoutRounding = true,
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(9),
                Background = new LinearGradientBrush(
                    Color.FromArgb(180, 42, 60, 85),
                    Color.FromArgb(220, 24, 36, 54),
                    45),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                BorderThickness = new Thickness(1)
            };
            string label = string.IsNullOrWhiteSpace(item.Name) ? "?" : item.Name.Substring(0, 1);
            var initial = new TextBlock
            {
                Text = label,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            fallback.Child = initial;
            container.Child = fallback;
        }

        container.MouseEnter += (_, _) =>
        {
            if (!_isDockDragging)
            {
                container.Background = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255));
            }
        };
        container.MouseLeave += (_, _) =>
        {
            container.Background = Brushes.Transparent;
        };

        container.MouseLeftButtonDown += (_, e) =>
        {
            _dockDragStart = e.GetPosition(this);
            _dockDraggedItem = item;
            _isDockDragging = false;
        };

        container.MouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dockDraggedItem != item) return;
            var curPos = e.GetPosition(this);
            if (!_isDockDragging && (curPos - _dockDragStart).Length > 6)
            {
                _isDockDragging = true;
                container.CaptureMouse();
            }

            if (_isDockDragging)
            {
                // Dragged upward out of dock: indicate removal
                if (curPos.Y < -15)
                {
                    container.Opacity = 0.35;
                    container.ToolTip = "松开即可从 Dock 栏中移除";
                }
                else
                {
                    container.Opacity = 1.0;
                    container.ToolTip = item.Name;

                    // Real-time slot reorder within items panel without destroying elements or losing capture
                    int curIdx = _itemsPanel.Children.IndexOf(container);
                    int targetIdx = -1;
                    var panelPos = e.GetPosition(_itemsPanel);
                    for (int i = 0; i < _itemsPanel.Children.Count; i++)
                    {
                        if (_itemsPanel.Children[i] is FrameworkElement child && child != container)
                        {
                            var childBounds = child.TranslatePoint(new Point(0, 0), _itemsPanel);
                            if (panelPos.X >= childBounds.X && panelPos.X <= childBounds.X + child.ActualWidth)
                            {
                                targetIdx = i;
                                break;
                            }
                        }
                    }

                    if (curIdx >= 0 && targetIdx >= 0 && curIdx != targetIdx)
                    {
                        _itemsPanel.Children.RemoveAt(curIdx);
                        _itemsPanel.Children.Insert(targetIdx, container);
                    }
                }
            }
        };

        container.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (_isDockDragging)
            {
                container.ReleaseMouseCapture();
                _isDockDragging = false;
                var curPos = e.GetPosition(this);
                if (curPos.Y < -15)
                {
                    _config = DockRules.RemoveItem(_config, item.Id);
                    SaveConfig();
                    Render();
                }
                else
                {
                    // Commit new order from _itemsPanel.Children
                    var reordered = new List<DockItem>();
                    for (int i = 0; i < _itemsPanel.Children.Count; i++)
                    {
                        if (_itemsPanel.Children[i] is FrameworkElement el && el.Tag is DockItem di)
                        {
                            di.Order = i;
                            reordered.Add(di);
                        }
                    }
                    _config.Items = reordered;
                    SaveConfig();
                    Render();
                }
            }
            else
            {
                AnimateClick(container, () => LaunchApp(item));
            }
        };

        // Context menu
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "打开 " + item.Name };
        open.Click += (_, _) => LaunchApp(item);
        menu.Items.Add(open);

        var admin = new MenuItem { Header = "🛡 以管理员身份运行" };
        admin.Click += (_, _) => LaunchApp(item, true);
        menu.Items.Add(admin);

        var folder = new MenuItem { Header = "打开所在目录" };
        folder.Click += (_, _) => ShowInFolder(item);
        menu.Items.Add(folder);

        menu.Items.Add(new Separator());

        if (!isFirst)
        {
            var left = new MenuItem { Header = "向左移动" };
            left.Click += (_, _) =>
            {
                _config = DockRules.ReorderItem(_config, item.Id, -1);
                SaveConfig();
                Render();
            };
            menu.Items.Add(left);
        }

        if (!isLast)
        {
            var right = new MenuItem { Header = "向右移动" };
            right.Click += (_, _) =>
            {
                _config = DockRules.ReorderItem(_config, item.Id, 1);
                SaveConfig();
                Render();
            };
            menu.Items.Add(right);
        }

        menu.Items.Add(new Separator());

        var remove = new MenuItem { Header = "从 Dock 移除" };
        remove.Click += (_, _) =>
        {
            _config = DockRules.RemoveItem(_config, item.Id);
            SaveConfig();
            Render();
        };
        menu.Items.Add(remove);

        container.ContextMenu = menu;

        return container;
    }

    private static string ResolveTargetPath(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return target;

        if (string.Equals(target, "explorer.exe", StringComparison.OrdinalIgnoreCase) || string.Equals(target, "explorer", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        }
        if (string.Equals(target, "taskmgr.exe", StringComparison.OrdinalIgnoreCase) || string.Equals(target, "taskmgr", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(Environment.SystemDirectory, "taskmgr.exe");
        }
        if (string.Equals(target, "msedge.exe", StringComparison.OrdinalIgnoreCase))
        {
            string edge1 = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
            if (File.Exists(edge1)) return edge1;
            string edge2 = @"C:\Program Files\Microsoft\Edge\Application\msedge.exe";
            if (File.Exists(edge2)) return edge2;
        }
        if (string.Equals(target, "ms-settings:", StringComparison.OrdinalIgnoreCase) || string.Equals(target, "ms-settings", StringComparison.OrdinalIgnoreCase))
        {
            return "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel";
        }

        if (!Path.IsPathRooted(target) && !target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + target);
                var val = key?.GetValue("") as string;
                if (!string.IsNullOrEmpty(val) && File.Exists(val)) return val;
            }
            catch { /* ignore */ }
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + target);
                var val = key?.GetValue("") as string;
                if (!string.IsNullOrEmpty(val) && File.Exists(val)) return val;
            }
            catch { /* ignore */ }
        }

        return target;
    }

    private static ImageSource? GetDockIcon(DockItem item, double dpi)
    {
        string path = ResolveTargetPath(item.TargetPath);
        if (string.IsNullOrWhiteSpace(path)) return null;

        if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || !Path.IsPathRooted(path))
        {
            var shellIcon = ShellLinkHelper.GetShellAppIcon(path, dpi, 40);
            if (shellIcon != null) return shellIcon;
        }
        else
        {
            var shortcut = new ShortcutItem
            {
                SourcePath = path,
                TargetPath = path,
                IconPath = string.IsNullOrEmpty(item.IconPath) ? path : item.IconPath,
                IconIndex = item.IconIndex
            };
            var icon = ShellLinkHelper.GetIcon(shortcut, dpi, 40) ?? ShellLinkHelper.GetShellAppIcon(path, dpi, 40);
            if (icon != null) return icon;
        }

        return null;
    }

    private void LaunchApp(DockItem item, bool admin = false)
    {
        try
        {
            string path = ResolveTargetPath(item.TargetPath);
            var psi = new ProcessStartInfo
            {
                FileName = path,
                Arguments = item.Arguments,
                UseShellExecute = true,
                Verb = admin ? "runas" : ""
            };
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private static void ShowInFolder(DockItem item)
    {
        try
        {
            string path = ResolveTargetPath(item.TargetPath);
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }
            else if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private void OpenInstalledAppsPicker()
    {
        var picker = new InstalledAppsWindow
        {
            Topmost = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        if (picker.ShowDialog() == true)
        {
            foreach (var app in picker.Selected)
            {
                _config = DockRules.AddItem(_config, new DockItem
                {
                    Name = app.DisplayName,
                    TargetPath = string.IsNullOrEmpty(app.TargetPath) ? app.ShellPath : app.TargetPath,
                    Arguments = app.Arguments
                });
            }
            SaveConfig();
            Render();
        }
    }

    private void PickLocalFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "选择要添加到 Dock 的应用程序",
            Filter = "程序与快捷方式 (*.exe;*.lnk)|*.exe;*.lnk|所有文件 (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true)
        {
            string path = dlg.FileName;
            _config = DockRules.AddItem(_config, new DockItem
            {
                Name = Path.GetFileNameWithoutExtension(path),
                TargetPath = path
            });
            SaveConfig();
            Render();
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            foreach (var file in files)
            {
                if (File.Exists(file) || Directory.Exists(file))
                {
                    string name = Directory.Exists(file) ? new DirectoryInfo(file).Name : Path.GetFileNameWithoutExtension(file);
                    _config = DockRules.AddItem(_config, new DockItem
                    {
                        Name = name,
                        TargetPath = file
                    });
                }
            }
            SaveConfig();
            Render();
            e.Handled = true;
        }
    }

    public void Reposition()
    {
        var screen = Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
        var area = screen.WorkingArea;
        var source = PresentationSource.FromVisual(this);
        double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

        double logicalAreaWidth = area.Width / dpiX;
        double logicalAreaLeft = area.Left / dpiX;
        double logicalAreaBottom = area.Bottom / dpiY;

        double x = logicalAreaLeft + (logicalAreaWidth - ActualWidth) / 2;
        // Floated ~1 cm (48px) above bottom of screen/working area as requested
        double y = logicalAreaBottom - ActualHeight - 48;

        Left = Math.Round(x);
        _normalTop = Math.Round(y);
        if (!_isSlidDown) Top = _normalTop;
        QueueGlassRefresh();
    }

    private void SlideDock(bool down)
    {
        _isSlidDown = down;
        _glassRevision++;
        _glassTimer.Stop();
        var screen = Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
        var source = PresentationSource.FromVisual(this);
        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
        double screenBottom = screen.WorkingArea.Bottom / dpiY;
        // When slid down, leave a 3px edge strip visible at the bottom
        double targetTop = down ? screenBottom - 3 : _normalTop;

        var anim = new DoubleAnimation(targetTop, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        anim.Completed += (_, _) =>
        {
            BeginAnimation(TopProperty, null);
            Top = targetTop;
            QueueGlassRefresh();
        };
        BeginAnimation(TopProperty, anim);
    }

    private static SolidColorBrush GlassFallback { get; } =
        new(Color.FromArgb(20, 255, 255, 255));

    private void OnGlassCaptureChanged()
    {
        _glass.Background = GlassFallback;
        QueueGlassRefresh();
    }

    private void QueueGlassRefresh()
    {
        _glassRevision++;
        _glassTimer.Stop();
        _glassTimer.Interval = TimeSpan.FromMilliseconds(250);
        if (GlassCapturePolicy.Enabled && !_glassRendering && !_glassClosed && IsVisible && !_isSlidDown)
            _glassTimer.Start();
    }

    private async void RefreshGlass(object? sender, EventArgs e)
    {
        _glassTimer.Stop();
        if (!GlassCapturePolicy.Enabled || _glassRendering || _glassClosed || !IsVisible || _isSlidDown ||
            _glass.ActualWidth <= 0 || _glass.ActualHeight <= 0 ||
            PresentationSource.FromVisual(_glass) is null) return;

        var origin = _glass.PointToScreen(new Point());
        var end = _glass.PointToScreen(new Point(_glass.ActualWidth, _glass.ActualHeight));
        var area = System.Drawing.Rectangle.FromLTRB(
            (int)Math.Round(origin.X), (int)Math.Round(origin.Y),
            (int)Math.Round(end.X), (int)Math.Round(end.Y));
        if (area.Width <= 0 || area.Height <= 0) return;

        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        double scale = VisualTreeHelper.GetDpi(_glass).DpiScaleX;
        double radius = _glass.CornerRadius.TopLeft * scale;
        int revision = _glassRevision;
        _glassRendering = true;
        try
        {
            BitmapSource? frame = null;
            try
            {
                frame = await Task.Run(() => DockGlassRenderer.Render(hwnd, area, radius, scale));
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                or ArgumentException or ExternalException or OutOfMemoryException or System.ComponentModel.Win32Exception)
            {
                Debug.WriteLine(error);
            }

            if (!_glassClosed && revision == _glassRevision)
                _glass.Background = frame is null ? GlassFallback : new ImageBrush(frame);
        }
        finally
        {
            _glassRendering = false;
            if (GlassCapturePolicy.Enabled && !_glassClosed && IsVisible && !_isSlidDown)
            {
                _glassTimer.Interval = TimeSpan.FromMilliseconds(
                    revision == _glassRevision && _glass.Background is not ImageBrush ? 1000 : 250);
                _glassTimer.Start();
            }
        }
    }

    private static void AnimateScale(ScaleTransform transform, double target)
    {
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        transform.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
        transform.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
    }

    private static void AnimateClick(FrameworkElement element, Action callback)
    {
        if (element.RenderTransform is ScaleTransform scale)
        {
            bool fired = false;
            var down = new DoubleAnimation(0.92, TimeSpan.FromMilliseconds(65));
            down.Completed += (_, _) =>
            {
                if (fired) return;
                fired = true;
                var up = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(110));
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, up);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, up);
                callback();
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, down);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, down);
        }
        else
        {
            callback();
        }
    }

    private void SaveConfig()
    {
        try { _store.Save("dock.json", _config); }
        catch (Exception ex) { App.Log(ex); }
    }

    public void PinItem(DockItem item)
    {
        _config = DockRules.AddItem(_config, item);
        SaveConfig();
        Render();
    }

    private static DockConfig PopulateInitialDock(ConfigManager store)
    {
        var config = new DockConfig();
        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string sysDir = Environment.SystemDirectory;
        string edgePath = @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
        if (!File.Exists(edgePath)) edgePath = @"C:\Program Files\Microsoft\Edge\Application\msedge.exe";

        var candidates = new List<(string Name, string Path)>
        {
            ("文件资源管理器", Path.Combine(winDir, "explorer.exe")),
            ("Microsoft Edge", edgePath),
            ("任务管理器", Path.Combine(sysDir, "taskmgr.exe")),
            ("系统设置", "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel")
        };

        try
        {
            var shortcuts = store.Load<ShortcutConfig>("shortcuts.json").Value;
            foreach (var item in shortcuts.Items)
            {
                if (candidates.Count >= 9) break;
                if (!string.IsNullOrWhiteSpace(item.SourcePath) && !candidates.Any(c => string.Equals(c.Path, item.SourcePath, StringComparison.OrdinalIgnoreCase)))
                {
                    candidates.Add((item.Name, item.SourcePath));
                }
            }
        }
        catch { /* ignore */ }

        int order = 0;
        foreach (var c in candidates)
        {
            config.Items.Add(new DockItem { Name = c.Name, TargetPath = c.Path, Order = order++ });
        }
        return config;
    }

    private void OnItemsPanelMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDockDragging) return;
        var pos = e.GetPosition(_itemsPanel);
        foreach (var child in _itemsPanel.Children)
        {
            if (child is FrameworkElement el && el.RenderTransform is ScaleTransform st)
            {
                var center = el.TranslatePoint(new Point(el.ActualWidth / 2, el.ActualHeight / 2), _itemsPanel);
                double dist = Math.Abs(pos.X - center.X);
                // Gaussian magnification wave (macOS dock style)
                double target = 1.0 + 0.22 * Math.Exp(-Math.Pow(dist / 44.0, 2));
                st.ScaleX = target;
                st.ScaleY = target;
            }
        }
    }

    private void OnItemsPanelMouseLeave(object sender, MouseEventArgs e)
    {
        foreach (var child in _itemsPanel.Children)
        {
            if (child is FrameworkElement el && el.RenderTransform is ScaleTransform st)
            {
                AnimateScale(st, 1.0);
            }
        }
    }
}
