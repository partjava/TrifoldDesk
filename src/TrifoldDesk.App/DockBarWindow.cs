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
    private readonly StackPanel _itemsPanel = new() { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _pill = new();
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(550) };
    private bool _isSlidDown;
    private double _normalTop;

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

        Topmost = _config.AlwaysOnTop;

        BuildUI();

        Loaded += (_, _) => Reposition();
        SizeChanged += (_, _) => Reposition();
        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;
        Closed += (_, _) => SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;

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
        _pill.Background = new LinearGradientBrush(
            Color.FromArgb(70, 24, 32, 46),
            Color.FromArgb(92, 12, 16, 24),
            90);
        _pill.BorderBrush = new LinearGradientBrush(
            Color.FromArgb(150, 255, 255, 255),
            Color.FromArgb(35, 255, 255, 255),
            45);
        _pill.BorderThickness = new Thickness(1.4);
        _pill.Effect = new DropShadowEffect
        {
            BlurRadius = 32,
            ShadowDepth = 6,
            Direction = 270,
            Opacity = 0.42,
            Color = Colors.Black
        };

        var root = new Grid();
        root.Children.Add(_pill);

        var topShine = new Border
        {
            Height = 1.2,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(24, 1, 24, 0),
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops =
                [
                    new(Color.FromArgb(0, 255, 255, 255), 0.0),
                    new(Color.FromArgb(180, 255, 255, 255), 0.5),
                    new(Color.FromArgb(0, 255, 255, 255), 1.0)
                ]
            },
            IsHitTestVisible = false
        };
        root.Children.Add(topShine);
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
            _config.Items = DockRules.CreateDefault().Items;
            SaveConfig();
            Render();
        };
        contextMenu.Items.Add(resetDefaults);

        _pill.ContextMenu = contextMenu;

        Render();
    }

    public void Render()
    {
        var mainStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        // 1. Launchpad button
        var launchpadBtn = CreateLaunchpadButton();
        mainStack.Children.Add(launchpadBtn);

        // 2. Vertical separator
        var separator = new Border
        {
            Width = 1,
            Height = 24,
            Background = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
            Margin = new Thickness(6, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        mainStack.Children.Add(separator);

        // 3. Pinned items
        _itemsPanel.Children.Clear();
        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var ordered = _config.Items.OrderBy(i => i.Order).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var item = ordered[i];
            var btn = CreateItemElement(item, dpi, i == 0, i == ordered.Count - 1);
            _itemsPanel.Children.Add(btn);
        }
        mainStack.Children.Add(_itemsPanel);

        // 4. Add shortcut button
        var addBtn = CreateAddButton();
        mainStack.Children.Add(addBtn);

        _pill.Child = mainStack;
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
        btn.MouseLeftButtonUp += (_, _) =>
        {
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
        btn.MouseLeftButtonUp += (_, _) => AnimateClick(btn, OpenInstalledAppsPicker);

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
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        var scale = new ScaleTransform(1.0, 1.0);
        container.RenderTransform = scale;

        var iconSource = GetDockIcon(item, dpi);
        var icon = new Image
        {
            Source = iconSource,
            Width = 36,
            Height = 36,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        container.Child = icon;

        container.MouseEnter += (_, _) =>
        {
            container.Background = new SolidColorBrush(Color.FromArgb(42, 255, 255, 255));
            AnimateScale(scale, 1.18);
        };
        container.MouseLeave += (_, _) =>
        {
            container.Background = Brushes.Transparent;
            AnimateScale(scale, 1.0);
        };
        container.MouseLeftButtonUp += (_, _) =>
        {
            AnimateClick(container, () => LaunchApp(item));
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

    private static ImageSource? GetDockIcon(DockItem item, double dpi)
    {
        if (string.IsNullOrWhiteSpace(item.TargetPath)) return null;
        if (item.TargetPath.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || !Path.IsPathRooted(item.TargetPath))
        {
            return ShellLinkHelper.GetShellAppIcon(item.TargetPath, dpi, 40);
        }
        var shortcut = new ShortcutItem
        {
            SourcePath = item.TargetPath,
            TargetPath = item.TargetPath,
            IconPath = item.IconPath,
            IconIndex = item.IconIndex
        };
        return ShellLinkHelper.GetIcon(shortcut, dpi, 40) ?? ShellLinkHelper.GetShellAppIcon(item.TargetPath, dpi, 40);
    }

    private void LaunchApp(DockItem item, bool admin = false)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = item.TargetPath,
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
            if (File.Exists(item.TargetPath))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{item.TargetPath}\"") { UseShellExecute = true });
            }
            else if (Directory.Exists(item.TargetPath))
            {
                Process.Start(new ProcessStartInfo(item.TargetPath) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private void OpenInstalledAppsPicker()
    {
        var picker = new InstalledAppsWindow { Owner = this };
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
        double y = logicalAreaBottom - ActualHeight - 10;

        Left = Math.Round(x);
        _normalTop = Math.Round(y);
        if (!_isSlidDown) Top = _normalTop;
    }

    private void SlideDock(bool down)
    {
        _isSlidDown = down;
        var screen = Forms.Screen.PrimaryScreen ?? Forms.Screen.AllScreens[0];
        var source = PresentationSource.FromVisual(this);
        double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
        double screenBottom = screen.WorkingArea.Bottom / dpiY;
        double targetTop = down ? screenBottom - 5 : _normalTop;

        var anim = new DoubleAnimation(targetTop, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(TopProperty, anim);
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
            var down = new DoubleAnimation(0.92, TimeSpan.FromMilliseconds(70));
            down.Completed += (_, _) =>
            {
                var up = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120));
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
        var candidates = new List<(string Name, string Path)>
        {
            ("文件资源管理器", "explorer.exe"),
            ("Microsoft Edge", "msedge.exe"),
            ("任务管理器", "taskmgr.exe"),
            ("系统设置", "ms-settings:")
        };

        // Try to add popular apps if present in shortcuts.json
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
}
