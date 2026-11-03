using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using TrifoldDesk.Services;

namespace TrifoldDesk;

public sealed class InstalledAppsWindow : Window
{
    private readonly WrapPanel _apps = new() { Margin = new Thickness(4) };
    private readonly HashSet<InstalledApp> _selected = [];
    private readonly IReadOnlyList<InstalledApp> _catalog;
    private readonly Dictionary<InstalledApp, (Border Card, Border CheckBadge, TextBlock CheckMark)> _cardMap = [];

    private readonly TextBox _searchBox = new();
    private readonly TextBlock _searchHint = new()
    {
        Text = "搜索已安装应用名称、关键词或路径…",
        Foreground = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
        FontSize = 12.5,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false
    };
    private readonly TextBlock _selectionBadge = new()
    {
        Text = "未选择应用",
        FontSize = 12,
        Foreground = new SolidColorBrush(Color.FromArgb(180, 255, 255, 255)),
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(12, 0, 0, 0)
    };
    private readonly Button _addBtn = new();

    public IReadOnlyList<InstalledApp> Selected => _selected.ToArray();

    public InstalledAppsWindow()
    {
        Title = "从已安装应用添加";
        Width = 780;
        Height = 580;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _catalog = InstalledAppsService.Read();

        BuildUI();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { DialogResult = false; Close(); }
        };
    }

    private void BuildUI()
    {
        // Frosted acrylic glass container
        var glass = new Border
        {
            CornerRadius = new CornerRadius(22),
            Background = new LinearGradientBrush(
                Color.FromArgb(235, 18, 24, 34),
                Color.FromArgb(245, 12, 16, 22),
                90),
            BorderBrush = new LinearGradientBrush(
                Color.FromArgb(140, 255, 255, 255),
                Color.FromArgb(35, 255, 255, 255),
                45),
            BorderThickness = new Thickness(1.4),
            Effect = new DropShadowEffect
            {
                BlurRadius = 40,
                ShadowDepth = 8,
                Direction = 270,
                Opacity = 0.55,
                Color = Colors.Black
            },
            ClipToBounds = true
        };

        var mainGrid = new Grid();
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(46) }); // Title bar
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(52) }); // Search bar
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Grid
        mainGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(56) }); // Bottom actions

        // Specular highlight line
        var shine = new Border
        {
            Height = 1.2,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(32, 1, 32, 0),
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops =
                [
                    new(Color.FromArgb(0, 255, 255, 255), 0.0),
                    new(Color.FromArgb(170, 255, 255, 255), 0.5),
                    new(Color.FromArgb(0, 255, 255, 255), 1.0)
                ]
            },
            IsHitTestVisible = false
        };
        Grid.SetRowSpan(shine, 4);
        mainGrid.Children.Add(shine);

        // Row 0: Custom Title Bar
        var titleBar = BuildTitleBar();
        Grid.SetRow(titleBar, 0);
        mainGrid.Children.Add(titleBar);

        // Row 1: Search bar
        var searchBar = BuildSearchBar();
        Grid.SetRow(searchBar, 1);
        mainGrid.Children.Add(searchBar);

        // Row 2: Scrollable App Cards
        var scrollViewer = new ScrollViewer
        {
            Content = _apps,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(16, 0, 16, 4)
        };
        Grid.SetRow(scrollViewer, 2);
        mainGrid.Children.Add(scrollViewer);

        // Row 3: Bottom Action Bar
        var actionBar = BuildBottomBar();
        Grid.SetRow(actionBar, 3);
        mainGrid.Children.Add(actionBar);

        glass.Child = mainGrid;
        Content = glass;

        _searchBox.TextChanged += (_, _) =>
        {
            _searchHint.Visibility = string.IsNullOrEmpty(_searchBox.Text) ? Visibility.Visible : Visibility.Collapsed;
            Render(_searchBox.Text.Trim());
        };

        Render("");
    }

    private FrameworkElement BuildTitleBar()
    {
        var bar = new Grid { Background = Brushes.Transparent, Margin = new Thickness(20, 0, 14, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        bar.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        };

        var titleStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var titleIcon = new TextBlock
        {
            Text = "＋",
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF")),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var titleText = new TextBlock
        {
            Text = "从已安装应用添加",
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };
        titleStack.Children.Add(titleIcon);
        titleStack.Children.Add(titleText);
        Grid.SetColumn(titleStack, 0);
        bar.Children.Add(titleStack);

        var closeBtn = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        var closeText = new TextBlock
        {
            Text = "✕",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        closeBtn.Child = closeText;
        closeBtn.MouseEnter += (_, _) => closeBtn.Background = new SolidColorBrush(Color.FromArgb(70, 255, 60, 60));
        closeBtn.MouseLeave += (_, _) => closeBtn.Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255));
        closeBtn.MouseLeftButtonUp += (_, _) => { DialogResult = false; Close(); };
        Grid.SetColumn(closeBtn, 1);
        bar.Children.Add(closeBtn);

        return bar;
    }

    private FrameworkElement BuildSearchBar()
    {
        var container = new Border
        {
            Height = 38,
            CornerRadius = new CornerRadius(19),
            Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(20, 4, 20, 10)
        };

        var grid = new Grid { Margin = new Thickness(14, 0, 14, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = new TextBlock
        {
            Text = "🔍",
            FontSize = 13,
            Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(icon, 0);
        grid.Children.Add(icon);

        var inputHost = new Grid();
        _searchBox.Background = Brushes.Transparent;
        _searchBox.BorderThickness = new Thickness(0);
        _searchBox.Foreground = Brushes.White;
        _searchBox.CaretBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF"));
        _searchBox.FontSize = 13;
        _searchBox.VerticalAlignment = VerticalAlignment.Center;
        _searchBox.Padding = new Thickness(0);

        inputHost.Children.Add(_searchBox);
        inputHost.Children.Add(_searchHint);
        Grid.SetColumn(inputHost, 1);
        grid.Children.Add(inputHost);

        var clearBtn = new TextBlock
        {
            Text = "✕",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Visibility = Visibility.Collapsed
        };
        _searchBox.TextChanged += (_, _) => clearBtn.Visibility = string.IsNullOrEmpty(_searchBox.Text) ? Visibility.Collapsed : Visibility.Visible;
        clearBtn.MouseLeftButtonUp += (_, _) => _searchBox.Text = "";
        Grid.SetColumn(clearBtn, 2);
        grid.Children.Add(clearBtn);

        container.Child = grid;
        return container;
    }

    private FrameworkElement BuildBottomBar()
    {
        var bar = new Grid { Margin = new Thickness(20, 8, 20, 12) };
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leftStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var selectAll = new TextBlock
        {
            Text = "全选当前",
            FontSize = 12,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF")),
            Cursor = Cursors.Hand,
            VerticalAlignment = VerticalAlignment.Center
        };
        selectAll.MouseLeftButtonUp += (_, _) => SelectAllCurrent();

        var clearAll = new TextBlock
        {
            Text = "取消选择",
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
            Cursor = Cursors.Hand,
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        clearAll.MouseLeftButtonUp += (_, _) => ClearAllSelection();

        leftStack.Children.Add(selectAll);
        leftStack.Children.Add(clearAll);
        leftStack.Children.Add(_selectionBadge);
        Grid.SetColumn(leftStack, 0);
        bar.Children.Add(leftStack);

        var rightStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var cancelBtn = new Border
        {
            CornerRadius = new CornerRadius(15),
            Background = new SolidColorBrush(Color.FromArgb(25, 255, 255, 255)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(18, 6, 18, 6),
            Margin = new Thickness(0, 0, 10, 0),
            Cursor = Cursors.Hand
        };
        var cancelText = new TextBlock { Text = "取消", FontSize = 12.5, Foreground = Brushes.White };
        cancelBtn.Child = cancelText;
        cancelBtn.MouseLeftButtonUp += (_, _) => { DialogResult = false; Close(); };
        rightStack.Children.Add(cancelBtn);

        _addBtn.Content = "确认添加";
        _addBtn.FontSize = 12.5;
        _addBtn.FontWeight = FontWeights.SemiBold;
        _addBtn.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
        _addBtn.Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#00E5FF"), (Color)ColorConverter.ConvertFromString("#00B4D8"), 45);
        _addBtn.BorderThickness = new Thickness(0);
        _addBtn.Padding = new Thickness(22, 6, 22, 6);
        _addBtn.Cursor = Cursors.Hand;

        var btnTemplate = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(15));
        border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(BackgroundProperty));
        border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(PaddingProperty));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        btnTemplate.VisualTree = border;
        _addBtn.Template = btnTemplate;

        _addBtn.Click += (_, _) =>
        {
            if (_selected.Count > 0)
            {
                DialogResult = true;
                Close();
            }
        };
        rightStack.Children.Add(_addBtn);

        Grid.SetColumn(rightStack, 1);
        bar.Children.Add(rightStack);

        UpdateBadge();
        return bar;
    }

    private void Render(string query)
    {
        _apps.Children.Clear();
        _cardMap.Clear();

        var filtered = _catalog
            .Where(a => string.IsNullOrWhiteSpace(query)
                        || a.DisplayName.Contains(query, StringComparison.CurrentCultureIgnoreCase)
                        || a.ShellPath.Contains(query, StringComparison.OrdinalIgnoreCase)
                        || a.TargetPath.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        double dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;

        foreach (var app in filtered)
        {
            var card = CreateAppCard(app, dpi);
            _apps.Children.Add(card);
        }

        UpdateBadge();
    }

    private Border CreateAppCard(InstalledApp app, double dpi)
    {
        bool isSelected = _selected.Contains(app);

        var card = new Border
        {
            Width = 142,
            Height = 98,
            CornerRadius = new CornerRadius(14),
            Margin = new Thickness(4),
            Padding = new Thickness(8, 6, 8, 6),
            Cursor = Cursors.Hand,
            ToolTip = app.Details
        };

        UpdateCardAppearance(card, isSelected);

        var grid = new Grid();

        // Top right check badge
        var badge = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 0, 0)
        };
        var checkMark = new TextBlock
        {
            Text = "✓",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = Color.FromRgb(15, 23, 42).ToString() != null ? new SolidColorBrush(Color.FromRgb(15, 23, 42)) : Brushes.Black,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        badge.Child = checkMark;
        UpdateBadgeAppearance(badge, checkMark, isSelected);
        grid.Children.Add(badge);

        // Center content stack
        var stack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var icon = new Image
        {
            Source = ShellLinkHelper.GetShellAppIcon(app.ShellPath, dpi, 40),
            Width = 38,
            Height = 38,
            Margin = new Thickness(0, 2, 0, 5),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);
        stack.Children.Add(icon);

        var title = new TextBlock
        {
            Text = app.DisplayName,
            FontSize = 11.5,
            Foreground = Brushes.White,
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 32,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Effect = TextContrast.Shadow
        };
        stack.Children.Add(title);

        grid.Children.Add(stack);
        card.Child = grid;

        _cardMap[app] = (card, badge, checkMark);

        card.MouseEnter += (_, _) =>
        {
            if (!_selected.Contains(app))
            {
                card.Background = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255));
                card.BorderBrush = new SolidColorBrush(Color.FromArgb(65, 255, 255, 255));
            }
        };
        card.MouseLeave += (_, _) =>
        {
            if (!_selected.Contains(app))
            {
                UpdateCardAppearance(card, false);
            }
        };

        card.MouseLeftButtonUp += (_, e) =>
        {
            if (e.ClickCount == 2)
            {
                _selected.Clear();
                _selected.Add(app);
                DialogResult = true;
                Close();
                return;
            }

            ToggleAppSelection(app);
        };

        return card;
    }

    private void ToggleAppSelection(InstalledApp app)
    {
        if (_selected.Contains(app)) _selected.Remove(app);
        else _selected.Add(app);

        if (_cardMap.TryGetValue(app, out var elem))
        {
            bool sel = _selected.Contains(app);
            UpdateCardAppearance(elem.Card, sel);
            UpdateBadgeAppearance(elem.CheckBadge, elem.CheckMark, sel);
        }

        UpdateBadge();
    }

    private static void UpdateCardAppearance(Border card, bool selected)
    {
        if (selected)
        {
            card.Background = new SolidColorBrush(Color.FromArgb(42, 0, 229, 255));
            card.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF"));
            card.BorderThickness = new Thickness(1.5);
        }
        else
        {
            card.Background = new SolidColorBrush(Color.FromArgb(16, 255, 255, 255));
            card.BorderBrush = new SolidColorBrush(Color.FromArgb(28, 255, 255, 255));
            card.BorderThickness = new Thickness(1);
        }
    }

    private static void UpdateBadgeAppearance(Border badge, TextBlock mark, bool selected)
    {
        if (selected)
        {
            badge.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF"));
            badge.BorderThickness = new Thickness(0);
            mark.Visibility = Visibility.Visible;
        }
        else
        {
            badge.Background = new SolidColorBrush(Color.FromArgb(15, 255, 255, 255));
            badge.BorderBrush = new SolidColorBrush(Color.FromArgb(45, 255, 255, 255));
            badge.BorderThickness = new Thickness(1);
            mark.Visibility = Visibility.Collapsed;
        }
    }

    private void SelectAllCurrent()
    {
        foreach (var app in _cardMap.Keys)
        {
            _selected.Add(app);
            var (card, badge, mark) = _cardMap[app];
            UpdateCardAppearance(card, true);
            UpdateBadgeAppearance(badge, mark, true);
        }
        UpdateBadge();
    }

    private void ClearAllSelection()
    {
        foreach (var app in _cardMap.Keys)
        {
            _selected.Remove(app);
            var (card, badge, mark) = _cardMap[app];
            UpdateCardAppearance(card, false);
            UpdateBadgeAppearance(badge, mark, false);
        }
        UpdateBadge();
    }

    private void UpdateBadge()
    {
        if (_selected.Count == 0)
        {
            _selectionBadge.Text = "未选择应用";
            _selectionBadge.Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));
            _addBtn.Content = "确认添加";
            _addBtn.Opacity = 0.5;
        }
        else
        {
            _selectionBadge.Text = $"已选中 {_selected.Count} 个应用";
            _selectionBadge.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00E5FF"));
            _addBtn.Content = $"确认添加 ({_selected.Count})";
            _addBtn.Opacity = 1.0;
        }
    }
}
