using System.Diagnostics;
using System.IO;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using TrifoldDesk.Core;
using TrifoldDesk.Services;

namespace TrifoldDesk;

public sealed class LaunchpadWindow : Window
{
    private const int Columns = 8;
    private const int Rows = 4;
    private const int PageSize = Columns * Rows; // 32 apps per page

    private readonly Action<DockItem> _onPinToDock;
    private readonly List<InstalledApp> _allApps = [];
    private List<InstalledApp> _filteredApps = [];
    private int _currentPage;
    private bool _loading;
    private bool _isOpen;
    private DateTime _lastToggleAt = DateTime.MinValue;

    private readonly Border _glass = new();
    private readonly UniformGrid _grid = new() { Columns = Columns, Rows = Rows, Margin = new Thickness(14, 4, 14, 6) };
    private readonly TextBox _searchBox = new();
    private readonly TextBlock _searchHint = new() { Text = "搜索应用…", Foreground = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)), FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
    private readonly TextBlock _pageLabel = new() { FontSize = 12, Foreground = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 14, 0) };
    private readonly Button _prevBtn = new();
    private readonly Button _nextBtn = new();
    private readonly TextBlock _statusText = new() { Text = "正在读取已安装应用…", FontSize = 13, Foreground = new SolidColorBrush(Color.FromArgb(170, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

    public LaunchpadWindow(Action<DockItem> onPinToDock)
    {
        _onPinToDock = onPinToDock ?? throw new ArgumentNullException(nameof(onPinToDock));

        Title = "TrifoldDesk Launchpad";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        Width = 840;
        Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        BuildUI();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Dismiss();
            else if (e.Key == Key.Left) PrevPage();
            else if (e.Key == Key.Right) NextPage();
        };

        MouseWheel += (_, e) =>
        {
            if (e.Delta < 0) NextPage();
            else if (e.Delta > 0) PrevPage();
            e.Handled = true;
        };

        Deactivated += (_, _) =>
        {
            if ((DateTime.UtcNow - _lastToggleAt).TotalMilliseconds < 220) return;
            Dismiss();
        };
    }

    private void BuildUI()
    {
        _glass.CornerRadius = new CornerRadius(28);
        _glass.Background = new LinearGradientBrush(
            Color.FromArgb(75, 24, 32, 46),
            Color.FromArgb(105, 12, 16, 24),
            90);
        _glass.BorderBrush = new LinearGradientBrush(
            Color.FromArgb(150, 255, 255, 255),
            Color.FromArgb(35, 255, 255, 255),
            45);
        _glass.BorderThickness = new Thickness(1.4);
        _glass.Effect = new DropShadowEffect
        {
            BlurRadius = 45,
            ShadowDepth = 10,
            Direction = 270,
            Opacity = 0.5,
            Color = Colors.Black
        };
        _glass.ClipToBounds = true;

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(60) });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(44) });

        var topShine = new Border
        {
            Height = 1.2,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(36, 1, 36, 0),
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
        Grid.SetRowSpan(topShine, 3);
        mainGrid.Children.Add(topShine);

        var searchBar = BuildSearchBar();
        Grid.SetRow(searchBar, 0);
        mainGrid.Children.Add(searchBar);

        var gridHost = new Grid();
        gridHost.Children.Add(_statusText);
        gridHost.Children.Add(_grid);
        Grid.SetRow(gridHost, 1);
        mainGrid.Children.Add(gridHost);

        var paginationBar = BuildPaginationBar();
        Grid.SetRow(paginationBar, 2);
        mainGrid.Children.Add(paginationBar);

        _glass.Child = mainGrid;
        Content = _glass;
    }

    private FrameworkElement BuildSearchBar()
    {
        var searchContainer = new Border
        {
            Width = 340,
            Height = 35,
            CornerRadius = new CornerRadius(17.5),
            Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(65, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(0, 14, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var searchGrid = new Grid { Margin = new Thickness(14, 0, 10, 0) };
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new TextBlock
        {
            Text = "🔍",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(icon, 0);
        searchGrid.Children.Add(icon);

        var inputHost = new Grid();
        _searchBox.Background = Brushes.Transparent;
        _searchBox.BorderThickness = new Thickness(0);
        _searchBox.Foreground = Brushes.White;
        _searchBox.CaretBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF"));
        _searchBox.FontSize = 13;
        _searchBox.VerticalAlignment = VerticalAlignment.Center;
        _searchBox.Padding = new Thickness(0);

        _searchBox.TextChanged += (_, _) =>
        {
            _searchHint.Visibility = string.IsNullOrEmpty(_searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            ApplyFilter();
        };

        inputHost.Children.Add(_searchBox);
        inputHost.Children.Add(_searchHint);
        Grid.SetColumn(inputHost, 1);
        searchGrid.Children.Add(inputHost);

        var searchBtn = new Border
        {
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
            Padding = new Thickness(10, 3, 10, 3),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand
        };
        var btnText = new TextBlock { Text = "搜索", FontSize = 11.5, Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)) };
        searchBtn.Child = btnText;
        searchBtn.MouseLeftButtonUp += (_, _) => ApplyFilter();
        Grid.SetColumn(searchBtn, 2);
        searchGrid.Children.Add(searchBtn);

        searchContainer.Child = searchGrid;
        return searchContainer;
    }

    private FrameworkElement BuildPaginationBar()
    {
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 8)
        };

        StyleNavBtn(_prevBtn, "‹");
        _prevBtn.Click += (_, _) => PrevPage();

        StyleNavBtn(_nextBtn, "›");
        _nextBtn.Click += (_, _) => NextPage();

        bar.Children.Add(_prevBtn);
        bar.Children.Add(_pageLabel);
        bar.Children.Add(_nextBtn);

        return bar;
    }

    private static void StyleNavBtn(Button btn, string arrow)
    {
        btn.Width = 28;
        btn.Height = 28;
        btn.Content = arrow;
        btn.FontSize = 18;
        btn.FontWeight = FontWeights.Bold;
        btn.Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255));
        btn.Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
        btn.BorderBrush = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
        btn.Cursor = Cursors.Hand;
        btn.Padding = new Thickness(0, -2, 0, 0);

        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(14));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(BorderBrushProperty));
        border.SetValue(Border.BorderThicknessProperty, new Thickness(1));

        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        btn.Template = template;
    }

    public async void Toggle()
    {
        if ((DateTime.UtcNow - _lastToggleAt).TotalMilliseconds < 220) return;
        _lastToggleAt = DateTime.UtcNow;

        if (_isOpen)
        {
            Dismiss();
        }
        else
        {
            ShowLaunchpad();
            if (_allApps.Count == 0 && !_loading)
            {
                await LoadAppsAsync();
            }
        }
    }

    private void ShowLaunchpad()
    {
        _isOpen = true;
        BeginAnimation(OpacityProperty, null);
        Opacity = 0;
        Show();
        Activate();
        _searchBox.Focus();

        var anim = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        anim.Completed += (_, _) =>
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1.0;
        };
        BeginAnimation(OpacityProperty, anim);
    }

    public void Dismiss()
    {
        if (!_isOpen) return;
        _isOpen = false;

        var anim = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
            FillBehavior = FillBehavior.Stop
        };
        anim.Completed += (_, _) =>
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 0;
            Hide();
        };
        BeginAnimation(OpacityProperty, anim);
    }

    private async Task LoadAppsAsync()
    {
        _loading = true;
        _statusText.Text = "正在读取已安装应用…";
        _statusText.Visibility = Visibility.Visible;
        _grid.Visibility = Visibility.Collapsed;

        try
        {
            var apps = await Task.Run(InstalledAppsService.Read);
            _allApps.Clear();
            _allApps.AddRange(apps);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            _statusText.Text = "读取已安装应用异常";
        }
        finally
        {
            _loading = false;
            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        string query = _searchBox.Text.Trim();
        if (string.IsNullOrEmpty(query))
        {
            _filteredApps = [.. _allApps];
        }
        else
        {
            _filteredApps = _allApps
                .Where(a => a.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                            || a.ShellPath.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || a.TargetPath.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        _currentPage = 0;
        RenderPage();
    }

    private void RenderPage()
    {
        _grid.Children.Clear();
        _statusText.Visibility = Visibility.Collapsed;
        _grid.Visibility = Visibility.Visible;

        if (_filteredApps.Count == 0)
        {
            _statusText.Text = _loading ? "正在读取应用…" : "未搜索到匹配的应用";
            _statusText.Visibility = Visibility.Visible;
            _grid.Visibility = Visibility.Collapsed;
            _pageLabel.Text = "0 / 0";
            _prevBtn.IsEnabled = false;
            _nextBtn.IsEnabled = false;
            return;
        }

        int totalPages = Math.Max(1, (int)Math.Ceiling(_filteredApps.Count / (double)PageSize));
        _currentPage = Math.Clamp(_currentPage, 0, totalPages - 1);

        _pageLabel.Text = $"{_currentPage + 1} / {totalPages}";
        _prevBtn.IsEnabled = _currentPage > 0;
        _nextBtn.IsEnabled = _currentPage < totalPages - 1;

        var pageItems = _filteredApps
            .Skip(_currentPage * PageSize)
            .Take(PageSize)
            .ToList();

        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;

        foreach (var app in pageItems)
        {
            var cell = CreateAppCell(app, dpi);
            _grid.Children.Add(cell);
        }

        for (int i = pageItems.Count; i < PageSize; i++)
        {
            _grid.Children.Add(new Border { Background = Brushes.Transparent, IsHitTestVisible = false });
        }
    }

    private FrameworkElement CreateAppCell(InstalledApp app, double dpi)
    {
        var cell = new Border
        {
            CornerRadius = new CornerRadius(14),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            ToolTip = app.Details,
            Margin = new Thickness(4),
            Padding = new Thickness(4, 6, 4, 6),
            RenderTransformOrigin = new Point(0.5, 0.5)
        };
        var scale = new ScaleTransform(1.0, 1.0);
        cell.RenderTransform = scale;

        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var icon = new Image
        {
            Source = ShellLinkHelper.GetShellAppIcon(app.ShellPath, dpi, 44),
            Width = 42,
            Height = 42,
            Margin = new Thickness(0, 0, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        stack.Children.Add(icon);

        var name = new TextBlock
        {
            Text = app.DisplayName,
            FontSize = 11.5,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 84,
            MaxHeight = 28,
            Effect = TextContrast.Shadow
        };
        stack.Children.Add(name);

        cell.Child = stack;

        cell.MouseEnter += (_, _) =>
        {
            cell.Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
            AnimateScale(scale, 1.12);
        };
        cell.MouseLeave += (_, _) =>
        {
            cell.Background = Brushes.Transparent;
            AnimateScale(scale, 1.0);
        };
        cell.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            AnimateClick(cell, () =>
            {
                LaunchApp(app);
                Dismiss();
            });
        };

        var menu = new ContextMenu();
        var open = new MenuItem { Header = "打开 " + app.DisplayName };
        open.Click += (_, _) => { LaunchApp(app); Dismiss(); };
        menu.Items.Add(open);

        var admin = new MenuItem { Header = "🛡 以管理员身份运行" };
        admin.Click += (_, _) => { LaunchApp(app, true); Dismiss(); };
        menu.Items.Add(admin);

        var folder = new MenuItem { Header = "打开所在目录" };
        folder.Click += (_, _) => ShowInFolder(app);
        menu.Items.Add(folder);

        menu.Items.Add(new Separator());

        var pin = new MenuItem { Header = "📌 固定到底部 Dock 栏" };
        pin.Click += (_, _) =>
        {
            _onPinToDock(new DockItem
            {
                Name = app.DisplayName,
                TargetPath = string.IsNullOrEmpty(app.TargetPath) ? app.ShellPath : app.TargetPath,
                Arguments = app.Arguments
            });
        };
        menu.Items.Add(pin);

        cell.ContextMenu = menu;

        return cell;
    }

    private void PrevPage()
    {
        if (_currentPage > 0)
        {
            _currentPage--;
            RenderPage();
        }
    }

    private void NextPage()
    {
        int totalPages = Math.Max(1, (int)Math.Ceiling(_filteredApps.Count / (double)PageSize));
        if (_currentPage < totalPages - 1)
        {
            _currentPage++;
            RenderPage();
        }
    }

    private static void LaunchApp(InstalledApp app, bool admin = false)
    {
        try
        {
            string target = string.IsNullOrEmpty(app.TargetPath) ? app.ShellPath : app.TargetPath;
            var psi = new ProcessStartInfo
            {
                FileName = target,
                Arguments = app.Arguments,
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

    private static void ShowInFolder(InstalledApp app)
    {
        try
        {
            string target = Environment.ExpandEnvironmentVariables(app.TargetPath);
            if (File.Exists(target))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{target}\"") { UseShellExecute = true });
            }
            else if (Directory.Exists(target))
            {
                Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    private static void AnimateScale(ScaleTransform transform, double target)
    {
        var anim = new DoubleAnimation(target, TimeSpan.FromMilliseconds(150))
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
            var down = new DoubleAnimation(0.93, TimeSpan.FromMilliseconds(65));
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
}
