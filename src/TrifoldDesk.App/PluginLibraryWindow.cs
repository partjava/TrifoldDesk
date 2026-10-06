namespace TrifoldDesk;

public sealed class PluginLibraryWindow : Window
{
    public WidgetConfig Result { get; }
    private readonly StackPanel _instances = new();
    private readonly WrapPanel _cards = new();
    private readonly Dictionary<string, (Button Add, ComboBox Pane, ComboBox Style)> _choices = [];
    private readonly Dictionary<string, FrameworkElement> _previews = [];
    private static readonly string[] PaneNames = ["左", "中", "右"];
    private static readonly string[] AvailableIds = ["system-summary", "folder", "calendar", "clock", "controls", "workbench", "weather", "music", "project-browser"];
    private static string DisplayName(string id) => id switch
    {
        "system-summary" => "系统概览", "folder" => "文件夹", "calendar" => "日历", "clock" => "时钟", "controls" => "快捷控制",
        "apps" => "应用", "projects" => "资料", "cpu" => "CPU", "gpu" => "GPU", "memory" => "内存", "network" => "网速", "battery" => "电池", "disks" => "磁盘",
        _ => PluginRules.Catalog.FirstOrDefault(p => p.Id == id)?.Name ?? id
    };

    public PluginLibraryWindow(WidgetConfig current)
    {
        Result = PluginRules.Clone(current);
        Title = "插件库"; Width = 680; Height = 720; MinWidth = 420; MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = PluginPreview.Fill("#162430"); Foreground = Brushes.WhiteSmoke;
        if (Application.Current.MainWindow?.TryFindResource(typeof(ComboBox)) is Style comboStyle) Resources[typeof(ComboBox)] = comboStyle;
        MaxHeight = SystemParameters.WorkArea.Height; MaxWidth = SystemParameters.WorkArea.Width;
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        footer.Children.Add(new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0, 0, 12, 0) });
        var install=new Button{Content="导入插件…",Margin=new Thickness(0,0,12,0)};footer.Children.Insert(0,install);install.Click+=(_,_)=>InstallPlugin();
        var save = new Button { Content = "保存", IsDefault = true }; save.Click += (_, _) => DialogResult = true; footer.Children.Add(save);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        root.Children.Add(scroll); var body = new StackPanel(); scroll.Content = body; body.Children.Add(_cards);
        foreach (var id in AvailableIds)
        {
            var definition = PluginRules.Catalog.FirstOrDefault(p => p.Id == id);
            if (definition == null) continue;
            var card = new Border { Background = PluginPreview.Fill("#22303C"), CornerRadius = new CornerRadius(9), Padding = new Thickness(12), Margin = new Thickness(0, 0, 8, 8), ToolTip = definition.Description };
            var stack = new StackPanel(); card.Child = stack; _cards.Children.Add(card);
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 9) }; stack.Children.Add(row);
            var actions = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(actions, Dock.Right); row.Children.Add(actions);
            var pane = new ComboBox { Width = 62, Height = 30, Padding = new Thickness(6, 2, 6, 2), FontSize = 12, ToolTip = "放置位置", ItemsSource = PaneNames, SelectedIndex = definition.Pane, Margin = new Thickness(0, 0, 6, 0) };
            actions.Children.Add(pane);
            // Keep existing test/import style semantics; the compact library adds the default appearance.
            var style = new ComboBox { ItemsSource = new[] { "透明", "纸片", "霓虹" }, SelectedIndex = 0 };
            var add = new Button { Content = "＋", ToolTip = "添加", Width = 30, Height = 30, Padding = new Thickness(0), FontSize = 17 };
            actions.Children.Add(add); _choices[id] = (add, pane, style);
            row.Children.Add(new TextBlock { Text = DisplayName(id), FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Brushes.WhiteSmoke, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            var preview = PluginPreview.Create(id); _previews[id] = preview; stack.Children.Add(preview);
            pane.SelectionChanged += (_, _) => RefreshAddButtons();
            add.Click += (_, _) =>
            {
                if (!PluginRules.CanAdd(Result, id, pane.SelectedIndex)) return;
                var added = PluginRules.Add(Result, id, pane.SelectedIndex, style.SelectedIndex == 1 ? "paper" : style.SelectedIndex == 2 ? "neon" : "glass");
                if (id == "folder") { added.Width = 280; added.Height = 320; }
                Refresh();
            };
        }
        _cards.SizeChanged += (_, _) =>
        {
            double width = Math.Max(1, _cards.ActualWidth); int columns = width >= 560 ? 2 : 1;
            foreach (var card in _cards.Children.OfType<Border>()) card.Width = Math.Max(1, width / columns - 8);
        };
        body.Children.Add(new TextBlock { Text = "已添加", FontSize = 13, Foreground = PluginPreview.Fill("#B8C9D2"), Margin = new Thickness(0, 14, 0, 10) });
        body.Children.Add(_instances); Refresh();
    }

    private void InstallPlugin()
    {
        var picker=new Microsoft.Win32.OpenFolderDialog{Title="选择含manifest.json的插件目录"};if(picker.ShowDialog(this)!=true)return;
        try
        {
            if(!PluginRules.CanAdd(Result,"external-plugin",1))throw new InvalidOperationException("组件数量已达上限。");
            var manifest=PluginFiles.Install(picker.FolderName,App.DataDirectory);var widget=PluginRules.Add(Result,"external-plugin",1);widget.ExternalPluginId=manifest.Id;widget.Title=manifest.Name;widget.Width=320;widget.Height=200;Refresh();
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"插件未添加");}
    }
    private void Refresh()
    {
        _instances.Children.Clear();
        foreach (var item in Result.Items.ToArray())
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 8, 7) };
            var remove = new Button { Content = "移除", Padding = new Thickness(10, 5, 10, 5) }; DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
            string pane = item.PaneIndex is >= 0 and < 3 ? PaneNames[item.PaneIndex] : "未知位置";
            row.Children.Add(new TextBlock { Text = $"{DisplayName(item.PluginId)}   ·   {pane}", FontSize = 13, Foreground = Brushes.WhiteSmoke, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            _instances.Children.Add(row); remove.Click += (_, _) => { Result.Items.Remove(item); Refresh(); };
        }
        RefreshAddButtons();
    }
    private void RefreshAddButtons()
    {
        foreach (var (id, controls) in _choices) controls.Add.IsEnabled = PluginRules.CanAdd(Result, id, controls.Pane.SelectedIndex);
    }
    internal IReadOnlyList<string> TestChoiceIds => _choices.Keys.ToArray();
    internal int TestPreviewCount => _previews.Count;
    internal int TestInstanceCount => _instances.Children.Count;
    internal FrameworkElement TestPreview(string id) => _previews[id];
    internal void TestAdd(string id, int pane, int style)
    {
        var controls = _choices[id]; controls.Pane.SelectedIndex = pane; controls.Style.SelectedIndex = style;
        controls.Add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
}
