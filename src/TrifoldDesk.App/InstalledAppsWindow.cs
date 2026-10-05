using TrifoldDesk.Services;
namespace TrifoldDesk;
public sealed class InstalledAppsWindow:Window
{
    private readonly WrapPanel _apps=new();
    private readonly HashSet<InstalledApp> _selected=[];
    private readonly IReadOnlyList<InstalledApp> _catalog;
    public IReadOnlyList<InstalledApp> Selected=>_selected.ToArray();
    public InstalledAppsWindow()
    {
        Title="从已安装应用添加"; Width=760;Height=560;WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(24,32,40));
        _catalog=InstalledAppsService.Read();
        var root=new DockPanel{Margin=new Thickness(18)};Content=root;
        var search=new TextBox{Height=34,Margin=new Thickness(0,0,0,12),ToolTip="搜索应用名称"};DockPanel.SetDock(search,Dock.Top);root.Children.Add(search);
        var add=new Button{Content="添加所选应用",Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(add,Dock.Bottom);root.Children.Add(add);
        add.Click+=(_,_)=>{if(_selected.Count>0)DialogResult=true;};
        root.Children.Add(new ScrollViewer{Content=_apps,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        search.TextChanged+=(_,_)=>Render(search.Text);Render("");
    }
    private void Render(string query)
    {
        _apps.Children.Clear();
        foreach(var app in _catalog.Where(a=>a.DisplayName.Contains(query,StringComparison.CurrentCultureIgnoreCase)))
        {
            var content=new StackPanel();content.Children.Add(new Image{Source=ShellLinkHelper.GetShellAppIcon(app.ShellPath),Width=36,Height=36,Margin=new Thickness(0,0,0,5)});
            content.Children.Add(new TextBlock{Text=app.DisplayName,TextWrapping=TextWrapping.Wrap,MaxHeight=34,TextTrimming=TextTrimming.CharacterEllipsis,TextAlignment=TextAlignment.Center,FontSize=12});
            var choice=new CheckBox{Content=content,Width=132,Height=90,Margin=new Thickness(5),IsChecked=_selected.Contains(app),ToolTip=app.Details};
            choice.Checked+=(_,_)=>_selected.Add(app);choice.Unchecked+=(_,_)=>_selected.Remove(app);_apps.Children.Add(choice);
        }
    }
}
