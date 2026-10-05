using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
namespace TrifoldDesk;
public sealed class PaneState { public bool[] Folded { get; set; } = new bool[3]; }
public partial class MainWindow
{
    private PaneState _paneState = new();
    private readonly bool[] _paneBusy = new bool[3];
    private readonly Canvas _paneLayer = new() { Background = null };
    private readonly List<MenuItem> _paneEntries = [];
    private void InitializePaneMenus()
    {
        var loaded = _config.Load<PaneState>("panes.json"); _paneState = loaded.Value;
        if (_paneState.Folded?.Length != 3) _paneState.Folded = new bool[3];
        var menu = new ContextMenu(); ContextMenu = menu;
        menu.Opened += (_,_) => _menuOpen = true;
        menu.Closed += (_,_) => _menuOpen = false;
        foreach (var (label,action) in new (string,Action)[] { ("插件库",()=>PluginLibraryClick(this,new RoutedEventArgs())), ("入口管理",()=>OpenManagementClick(this,new RoutedEventArgs())), ("个性化",()=>OpenAppearanceClick(this,new RoutedEventArgs())), ("全部收起",()=>SetCollapsed(true)) })
        { var entry=new MenuItem { Header=label }; entry.Click+=(_,_)=>action(); menu.Items.Add(entry); }
        menu.Items.Add(new Separator());
        for (int n=0;n<3;n++)
        {
            int index=n; var entry=new MenuItem(); menu.Items.Add(entry); _paneEntries.Add(entry);entry.Click+=(_,_)=>TogglePane(index);
            menu.Opened+=(_,_)=>entry.Header=(_paneState.Folded[index]?"展开":"折叠")+new[]{"左面","中面","右面"}[index];
        }
        ((Grid)Content).Background=Brushes.Transparent; ((Grid)Content).Children.Add(_paneLayer);
        Loaded+=(_,_)=>UpdatePaneClip(); PanelRoot.SizeChanged+=(_,_)=>UpdatePaneClip();
    }
    private void UpdatePaneClip()
    {
        double width=PanelRoot.ActualWidth/3, height=PanelRoot.ActualHeight;
        var clip=new GeometryGroup();
        for(int n=0;n<3;n++)
        {
            if(!_paneState.Folded[n]&&!_paneBusy[n])clip.Children.Add(new RectangleGeometry(new Rect(n*width,0,width,height)));
        }
        PanelRoot.Clip=clip;
    }
    private void TogglePane(int index)
    {
        if(_paneBusy[index]||_foldOverlay!=null||PanelRoot.ActualWidth<3)return;
        bool folded=!_paneState.Folded[index]; var next=new PaneState { Folded=(bool[])_paneState.Folded.Clone() }; next.Folded[index]=folded;
        try { _config.Save("panes.json",next); } catch(Exception ex) { Fail("折叠状态未保存",ex); return; }
        if (!_settings.AnimationsEnabled) { _paneState=next; UpdatePaneClip(); return; }
        int width=(int)Math.Ceiling(PanelRoot.ActualWidth),height=Math.Max(1,(int)Math.Ceiling(PanelRoot.ActualHeight));
        var oldClip=PanelRoot.Clip; PanelRoot.Clip=null;
        var snapshot=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32); snapshot.Render(PanelRoot); snapshot.Freeze(); PanelRoot.Clip=oldClip;
        int left=width*index/3,right=width*(index+1)/3;
        var image=new Image { Source=new CroppedBitmap(snapshot,new Int32Rect(left,0,right-left,height)),Width=right-left,Height=height,RenderTransformOrigin=new Point(1,.5) };
        var scale=new ScaleTransform(); var skew=new SkewTransform(); image.RenderTransform=new TransformGroup { Children=new TransformCollection { scale,skew } }; Canvas.SetLeft(image,left); _paneLayer.Children.Add(image);
        _paneState=next; _paneBusy[index]=true; UpdatePaneClip();
        Animate(scale,ScaleTransform.ScaleXProperty,folded?1:.02,folded?.02:1,TimeSpan.Zero);
        Animate(skew,SkewTransform.AngleYProperty,folded?0:10,folded?10:0,TimeSpan.Zero);
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(180) }; timer.Tick+=(_,_)=>{timer.Stop();_paneLayer.Children.Remove(image);_paneBusy[index]=false;UpdatePaneClip();}; timer.Start();
    }
    internal bool TestPaneFolded(int index)=>_paneState.Folded[index];
    internal void TestTogglePane(int index)=>TogglePane(index);
    internal void TestPaneMenuClick(int index)=>_paneEntries[index].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    internal bool TestNoPaneButtons=>!_paneLayer.Children.OfType<Button>().Any();
}
