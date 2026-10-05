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
                var entries=_diskEntries; diskTile.Children.Add(entries);
                void RefreshDisks()
                {
                    entries.Children.Clear();
                    foreach(var drive in model.Drives.Where(d=>d.Kind=="本地磁盘"))
                    {
                        entries.Children.Add(new TextBlock { Text=drive.Name.TrimEnd('\\')+"  "+(drive.FreeBytes is long free?$"余 {free/1073741824d:0.#} GB":"不可用"),FontSize=10,Foreground=instance.Style=="paper"?Brushes.DarkSlateGray:Brushes.WhiteSmoke,Margin=new Thickness(0,0,0,3),ToolTip=drive.CapacityText });
                        entries.Children.Add(new ProgressBar { Value=drive.UsedPercent,Maximum=100,Height=4,Margin=new Thickness(0,0,0,10),Foreground=drive.LowSpace?Brushes.OrangeRed:Brushes.Turquoise });
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
            content.Children.Add(new TextBlock { Text = id switch { "cpu" => "CPU", "memory" => "内存", "gpu" => "GPU · 3D", "network" => "网络", _ => "电池" }, FontSize = 10, Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(instance.Style == "paper" ? "#587079" : "#A8BDC8")) });
            var metric = new DashboardWidget(id, instance.Style, model, true); Grid.SetRow(metric, 1); content.Children.Add(metric); Metrics.Add(metric); _tiles.Children.Add(content);
        }
        SizeChanged += (_, _) => ArrangeTiles(); Loaded += (_, _) => ArrangeTiles();
    }
    private void ArrangeTiles()
    {
        double width = Math.Max(1, ActualWidth - 18); int columns = width >= 200 ? 2 : 1;
        double rows=Math.Max(1,Math.Ceiling(_tiles.Children.Count/(double)columns));
        foreach (var tile in _tiles.Children.OfType<Grid>()) { tile.Width = Math.Max(1, width / columns - 12); tile.Height = Math.Clamp(ActualHeight/rows-10,80,145); }
    }
}
