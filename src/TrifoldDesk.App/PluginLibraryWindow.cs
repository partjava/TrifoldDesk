namespace TrifoldDesk;
public sealed class PluginLibraryWindow : Window
{
    public WidgetConfig Result { get; }
    private readonly StackPanel _instances = new();
    private readonly List<Button> _addButtons = [];
    private readonly Dictionary<string, (Button Add, ComboBox Pane, ComboBox Style)> _choices = [];
    private static readonly string[] PaneNames=["左","中","右"];
    private static string DisplayName(PluginDefinition definition)=>definition.Id switch {"system-summary"=>"系统概览","folder"=>"文件夹","apps"=>"应用","projects"=>"资料","cpu"=>"CPU","gpu"=>"GPU","memory"=>"内存","network"=>"网速","disks"=>"磁盘",_=>definition.Name};
    public PluginLibraryWindow(WidgetConfig current)
    {
        Result = PluginRules.Clone(current);
        Title = "插件库"; Width = 680; Height = 720; MinWidth = 580; MinHeight = 450; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(22, 36, 48));
        if (Application.Current.MainWindow?.TryFindResource(typeof(ComboBox)) is Style comboStyle) Resources[typeof(ComboBox)] = comboStyle;
        MaxHeight = SystemParameters.WorkArea.Height; MaxWidth = SystemParameters.WorkArea.Width;
        var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
        var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var cancel = new Button { Content = "取消", IsCancel = true, Margin = new Thickness(0, 0, 12, 0) }; footer.Children.Add(cancel);
        var save = new Button { Content = "保存", IsDefault = true }; save.Click += (_, _) => DialogResult = true; footer.Children.Add(save);
        var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; root.Children.Add(scroll); var body = new StackPanel(); scroll.Content = body;
        foreach (var definition in PluginRules.Catalog)
        {
            var card = new Border { Background = new SolidColorBrush(Color.FromRgb(34, 48, 60)), CornerRadius = new CornerRadius(9), Padding = new Thickness(12,9,12,9), Margin = new Thickness(0, 0, 0, 6) };
            var row = new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});card.Child=row;body.Children.Add(card);
            row.Children.Add(new TextBlock { Text=DisplayName(definition),FontSize=14,FontWeight=FontWeights.SemiBold,VerticalAlignment=VerticalAlignment.Center,ToolTip=definition.Description,Margin=new Thickness(0,0,10,0),TextTrimming=TextTrimming.CharacterEllipsis });
            var actions = new StackPanel { Orientation = Orientation.Horizontal };Grid.SetColumn(actions,1);row.Children.Add(actions);
            var pane = new ComboBox { Width = 70,Height=32,Padding=new Thickness(6,2,6,2),FontSize=12,ToolTip="放置位置", ItemsSource = PaneNames, SelectedIndex = definition.Pane, Margin = new Thickness(0, 0, 8, 0) }; actions.Children.Add(pane);
            var style = new ComboBox { Width = 100,Height=32,Padding=new Thickness(6,2,6,2),FontSize=12,ToolTip="外观", ItemsSource = new[] { "透明", "纸片", "霓虹" }, SelectedIndex = 0, Visibility = definition.Id is "system-summary" or "calendar" or "cpu" or "memory" or "network" or "clock" or "battery" or "gpu" ? Visibility.Visible : Visibility.Collapsed, Margin = new Thickness(0, 0, 8, 0) }; actions.Children.Add(style);
            var add = new Button { Content = "＋",ToolTip="添加",Width=34,Height=32,Padding=new Thickness(0),FontSize=17, Tag = definition }; _addButtons.Add(add); actions.Children.Add(add);
            _choices[definition.Id] = (add, pane, style);
            add.Click += (_, _) => { if (PluginRules.CanAdd(Result, definition.Id, pane.SelectedIndex)) { var added = PluginRules.Add(Result, definition.Id, pane.SelectedIndex, style.SelectedIndex == 1 ? "paper" : style.SelectedIndex == 2 ? "neon" : "glass"); if (definition.Id == "folder") { added.Width=280; added.Height=320; } Refresh(); } };
        }
        body.Children.Add(new TextBlock { Text = "已添加", FontSize = 13,Opacity=.7, Margin = new Thickness(0, 14, 0, 10) }); body.Children.Add(_instances);Refresh();
    }
    private void Refresh()
    {
        _instances.Children.Clear();
        foreach (var item in Result.Items.ToArray())
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 7) }; var remove = new Button { Content = "移除", Padding = new Thickness(10, 5, 10, 5) };
            DockPanel.SetDock(remove, Dock.Right); row.Children.Add(remove);
            row.Children.Add(new TextBlock { Text = $"{DisplayName(PluginRules.Catalog.First(p => p.Id == item.PluginId))}   ·   {PaneNames[item.PaneIndex]}",FontSize=13, VerticalAlignment = VerticalAlignment.Center }); _instances.Children.Add(row);
            remove.Click += (_, _) => { Result.Items.Remove(item); Refresh(); };
        }
        foreach (var button in _addButtons) { var definition = (PluginDefinition)button.Tag; button.IsEnabled = PluginRules.CanAdd(Result, definition.Id, _choices[definition.Id].Pane.SelectedIndex); }
    }
    internal void TestAdd(string id, int pane, int style)
    { var controls = _choices[id]; controls.Pane.SelectedIndex = pane; controls.Style.SelectedIndex = style; controls.Add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
}
