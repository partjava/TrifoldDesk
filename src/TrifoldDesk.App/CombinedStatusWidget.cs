namespace TrifoldDesk;
public sealed class CombinedStatusWidget : ScrollViewer
{
    private readonly WrapPanel _tiles = new();
    internal List<DashboardWidget> Metrics { get; } = [];
    private readonly StackPanel _diskEntries=new();
    internal int TestDiskBars => _diskEntries.Children.OfType<ProgressBar>().Count();
    public CombinedStatusWidget(WidgetInstance instance, MainViewModel model)
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Hidden; HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled; Content = _tiles;
        foreach (var id in SummaryRules.Modules(instance))
        {
            if(id=="disks")
            {
                var diskTile=new Grid { Margin=new Thickness(6,3,6,7) };
                diskTile.RowDefinitions.Add(new(){Height=GridLength.Auto}); diskTile.RowDefinitions.Add(new());
                diskTile.Children.Add(Header("disks","硬盘",instance.Style));
                var entries=_diskEntries; Grid.SetRow(entries,1); diskTile.Children.Add(entries); entries.Margin=new Thickness(0,10,0,0);
                void RefreshDisks()
                {
                    entries.Children.Clear();
                    foreach(var drive in model.Drives.Where(d=>d.Kind=="本地磁盘"))
                    {
                        entries.Children.Add(new TextBlock { Text=drive.Name.TrimEnd('\\')+"  "+(drive.FreeBytes is long free && drive.TotalBytes is long total?$"余 {free/1073741824d:0.#} / {total/1073741824d:0.#} GB":"不可用"),FontSize=10,Foreground=instance.Style=="paper"?Brushes.DarkSlateGray:Brushes.WhiteSmoke,Margin=new Thickness(0,0,0,4),TextTrimming=TextTrimming.CharacterEllipsis,ToolTip=drive.CapacityText+" · 进度条表示已用容量" });
                        entries.Children.Add(new ProgressBar { Value=drive.UsedPercent,Maximum=100,Height=5,Margin=new Thickness(0,0,0,10),Template=BarTemplate(),Foreground=drive.LowSpace?Brushes.OrangeRed:new LinearGradientBrush(Color.FromRgb(56,186,193),Color.FromRgb(116,225,209),0),Background=new SolidColorBrush(Color.FromArgb(40,190,200,205)) });
                    }
                    if(entries.Children.Count==0)entries.Children.Add(new TextBlock { Text="磁盘读取中…",FontSize=10 });
                }
                System.Collections.Specialized.NotifyCollectionChangedEventHandler changed=(_,_)=>RefreshDisks();
                Loaded+=(_,_)=>{model.Drives.CollectionChanged+=changed;RefreshDisks();};
                Unloaded+=(_,_)=>model.Drives.CollectionChanged-=changed;
                _tiles.Children.Add(diskTile); continue;
            }
            var content = new Grid { Margin = new Thickness(6, 3, 6, 7) };
            content.RowDefinitions.Add(new() { Height = GridLength.Auto }); content.RowDefinitions.Add(new());
            var header=Header(id,id switch { "cpu" => "CPU", "memory" => "内存", "gpu" => "GPU · 3D", "network" => "网络", _ => "电池" },instance.Style); content.Children.Add(header);
            var metric = new DashboardWidget(id, instance.Style, model, true); Grid.SetRow(metric, 1); content.Children.Add(metric); Metrics.Add(metric); _tiles.Children.Add(content);
            if(id is "cpu" or "memory" or "gpu") metric.UtilizationUpdated += header.Children.OfType<OrganicMetricRing>().Single().Sample;
            if(id=="battery") metric.BatteryUpdated+=(percent,charging)=>{var icon=header.Children.OfType<HardwareGlyph>().Single();icon.BatteryPercent=percent;icon.Charging=charging;icon.InvalidateVisual();};
            if(id is "cpu" or "memory" or "gpu" or "network") { var divider=new Border{Height=1,Background=new SolidColorBrush(Color.FromArgb(22,170,185,195)),VerticalAlignment=VerticalAlignment.Bottom,IsHitTestVisible=false};Grid.SetRowSpan(divider,2);content.Children.Add(divider); }
        }
        SizeChanged += (_, _) => ArrangeTiles(); Loaded += (_, _) => ArrangeTiles();
    }
    private static Grid Header(string id,string label,string style)
    {
        var grid=new Grid{Margin=new Thickness(0,0,0,4)};
        grid.Children.Add(new TextBlock{Text=label,FontSize=11,Foreground=new SolidColorBrush((Color)ColorConverter.ConvertFromString(style=="paper"?"#587079":"#C0CDD3")),VerticalAlignment=VerticalAlignment.Center});
        if(id is "cpu" or "memory" or "gpu") grid.Children.Add(new OrganicMetricRing { Width=28,Height=28,HorizontalAlignment=HorizontalAlignment.Right,Accent=new SolidColorBrush((Color)ColorConverter.ConvertFromString(style=="paper"?"#267E73":"#74E1D1")) });
        else grid.Children.Add(new HardwareGlyph{Kind=id,Width=20,Height=20,HorizontalAlignment=HorizontalAlignment.Right,Ink=new SolidColorBrush((Color)ColorConverter.ConvertFromString(style=="paper"?"#267E73":"#74E1D1")),Opacity=.7,IsHitTestVisible=false});
        return grid;
    }
    private static ControlTemplate BarTemplate()
    {
        var track=new FrameworkElementFactory(typeof(Grid),"PART_Track");
        var back=new FrameworkElementFactory(typeof(Border));back.SetValue(Border.CornerRadiusProperty,new CornerRadius(2.5));back.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Background"){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)});track.AppendChild(back);
        var fill=new FrameworkElementFactory(typeof(Border),"PART_Indicator");fill.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Left);fill.SetValue(Border.CornerRadiusProperty,new CornerRadius(2.5));fill.SetBinding(Border.BackgroundProperty,new System.Windows.Data.Binding("Foreground"){RelativeSource=new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent)});track.AppendChild(fill);
        return new ControlTemplate(typeof(ProgressBar)){VisualTree=track};
    }
    private void ArrangeTiles()
    {
        double width = Math.Max(1, ActualWidth - 18); int columns = width >= 200 ? 2 : 1;
        double rows=Math.Max(1,Math.Ceiling(_tiles.Children.Count/(double)columns));
        foreach (var tile in _tiles.Children.OfType<Grid>()) { tile.Width = Math.Max(1, width / columns - 12); tile.Height = Math.Clamp(ActualHeight/rows-10,80,145); }
    }
}
