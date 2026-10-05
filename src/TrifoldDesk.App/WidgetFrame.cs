using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
namespace TrifoldDesk;

public sealed class WidgetFrame : Border
{
    public WidgetInstance Model { get; }
    public bool InteractionActive => _gesturing || ContextMenu?.IsOpen == true || _body is FolderWidget folder && folder.InteractionActive;
    private readonly Canvas _canvas;
    private readonly FrameworkElement _body;
    private readonly Thumb _move = new(), _resize = new();
    private readonly List<Thumb> _corners = [];
    private double _resizeDx, _resizeDy;
    private bool _resizeLeft, _resizeTop;
    private readonly Button _fold = new(), _lock = new();
    private readonly TextBlock _title = new();
    private MenuItem? _auto;
    private StackPanel? _actions;
    private readonly DispatcherTimer _leaveTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
    private readonly Func<WidgetInstance, bool> _save;
    private WidgetInstance? _before;
    private bool _gesturing;
    private bool _paneInitialized;
    private double _dragX, _dragY;
    private WidgetInstance? _edgeBefore;
    private void ExpandFolderEdge(int edge)
    {
        if(Model.IsLocked||Model.IsCollapsed)return;
        _edgeBefore ??= Copy(); Model.FolderAutoHeight=false;
        double left=Model.PaneIndex*_canvas.ActualWidth/3, right=left+_canvas.ActualWidth/3;
        double amount;
        if(edge==1) { amount=Math.Min(70,Model.X-left);Model.X-=amount;Model.Width+=amount; }
        if(edge==2)Model.Width+=Math.Min(70,right-Model.X-Model.Width);
        if(edge==3) { amount=Math.Min(66,Model.Y);Model.Y-=amount;Model.Height+=amount; }
        if(edge==4)Model.Height+=Math.Min(66,_canvas.ActualHeight-Model.Y-Model.Height);
        Constrain(false);
    }
    private void FinishFolderEdge(bool accepted)
    {
        if(_edgeBefore==null)return;
        var previous=_edgeBefore; _edgeBefore=null;
        if(!accepted||!_save(Model))Restore(previous);
    }
    public WidgetFrame(WidgetInstance model, FrameworkElement body, Canvas canvas, Func<WidgetInstance, bool> save, Action remove, Action<string>? themeChanged = null)
    {
        Model = model; _body = body; _canvas = canvas; _save = save;
        if (body is FolderWidget folderBody) { folderBody.ExpandRequested += ToggleCollapsed; folderBody.ContentChanged += () => Constrain(false); folderBody.EdgeExpansionRequested+=ExpandFolderEdge;folderBody.EdgeExpansionFinished+=FinishFolderEdge; }
        CornerRadius = new CornerRadius(18); BorderThickness = new Thickness(1); BorderBrush = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255));
        if (model.PluginId is "apps" or "projects") { CornerRadius = new CornerRadius(0); BorderThickness = new Thickness(0); Background = Brushes.Transparent; }
        bool themed = model.PluginId is "system-summary" or "cpu" or "memory" or "network" or "gpu" or "clock" or "battery" or "calendar";
        if (themed && model.Style is "paper" or "neon") Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString(model.Style == "paper" ? "#F8F1E4" : "#E62E214C"), (Color)ColorConverter.ConvertFromString(model.Style == "paper" ? "#E8EEE8" : "#D416253E"), 50);
        else Background = Brushes.Transparent; ClipToBounds = true;
        if (model.PluginId == "folder") CornerRadius = new CornerRadius(10);
        bool folderFrame = model.PluginId == "folder";
        var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(folderFrame ? 4 : model.PluginId == "clock" ? 12 : 36) }); grid.RowDefinitions.Add(new RowDefinition()); Child = grid;
        var header = new Grid { Margin = new Thickness(12, 0, 10, 0) }; grid.Children.Add(header);
        _move.Cursor = Cursors.SizeAll; _move.Background = Brushes.Transparent;
        var transparent = new FrameworkElementFactory(typeof(Border)); transparent.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        _move.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = transparent }; header.Children.Add(_move);
        _title.TextTrimming = TextTrimming.CharacterEllipsis; _title.FontSize = 11; _title.FontWeight = FontWeights.SemiBold;
        if (themed && model.Style == "paper") _title.Foreground = new SolidColorBrush(Color.FromRgb(48, 72, 76));
        _title.VerticalAlignment = VerticalAlignment.Center; _title.Margin = new Thickness(0, 0, 112, 0); _title.IsHitTestVisible = false; header.Children.Add(_title);
        if (folderFrame) { _title.Visibility = Visibility.Collapsed; ToolTip = model.Title; }
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }; header.Children.Add(actions);
        _actions = actions;
        actions.Visibility = Visibility.Collapsed;
        actions.Opacity = model.IsCollapsed ? 1 : 0;
        MouseEnter += (_, _) => { actions.Opacity = 1; _resize.Opacity = 1; };
        MouseLeave += (_, _) => { if (!Model.IsCollapsed) actions.Opacity = 0; _resize.Opacity = 0; };
        foreach (var button in new[] { _lock, _fold }) { button.Padding = new Thickness(7, 4, 7, 4); button.FontSize = 11; button.Margin = new Thickness(3, 0, 0, 0); actions.Children.Add(button); }
        _lock.ToolTip = "锁定/解锁位置与尺寸"; _lock.Click += (_, _) => ChangeState(() => Model.IsLocked = !Model.IsLocked);
        _fold.ToolTip = "收起/展开"; _fold.Click += (_, _) => ToggleCollapsed();
        var bodyHolder = new Border { Padding = folderFrame ? new Thickness(4, 0, 4, 4) : new Thickness(12, 0, 12, 16), Child = body }; Grid.SetRow(bodyHolder, 1); grid.Children.Add(bodyHolder);
        if (folderFrame) { Grid.SetRowSpan(header,2); _move.Height=4; _move.VerticalAlignment=VerticalAlignment.Top; actions.VerticalAlignment = VerticalAlignment.Top; Panel.SetZIndex(header,1); }
        _resize.Width = 20; _resize.Height = 20; _resize.HorizontalAlignment = HorizontalAlignment.Right; _resize.VerticalAlignment = VerticalAlignment.Bottom; _resize.Cursor = Cursors.SizeNWSE;
        _resize.Width=12; _resize.Height=12;
        var resizeSurface=new FrameworkElementFactory(typeof(Border)); resizeSurface.SetValue(Border.BackgroundProperty,Brushes.Transparent);
        _resize.Template = new ControlTemplate(typeof(Thumb)) { VisualTree = resizeSurface }; Grid.SetRowSpan(_resize,2); grid.Children.Add(_resize); _corners.Add(_resize);
        foreach (var (left,top) in new[] { (true,true),(false,true),(true,false) })
        {
            var cornerSurface=new FrameworkElementFactory(typeof(Border)); cornerSurface.SetValue(Border.BackgroundProperty,Brushes.Transparent);
            var corner=new Thumb { Width=12, Height=12, HorizontalAlignment=left?HorizontalAlignment.Left:HorizontalAlignment.Right, VerticalAlignment=top?VerticalAlignment.Top:VerticalAlignment.Bottom, Cursor=left==top?Cursors.SizeNWSE:Cursors.SizeNESW, Template=new ControlTemplate(typeof(Thumb)) { VisualTree=cornerSurface } };
            Grid.SetRowSpan(corner,2); grid.Children.Add(corner); _corners.Add(corner);
            corner.DragStarted+=BeginGesture; corner.DragDelta+=(_,e)=>ResizeCorner(e.HorizontalChange,e.VerticalChange,left,top); corner.DragCompleted+=EndGesture;
        }
        _move.DragStarted += BeginGesture; _resize.DragStarted += BeginGesture;
        _move.DragDelta += (_, e) => Move(e.HorizontalChange, e.VerticalChange);
        _resize.DragDelta += (_, e) => Resize(e.HorizontalChange, e.VerticalChange);
        _move.DragCompleted += EndGesture; _resize.DragCompleted += EndGesture;
        var menu = new ContextMenu(); ContextMenu = menu;
        var lockEntry = new MenuItem { Header="锁定位置", IsCheckable=true }; menu.Items.Add(lockEntry); lockEntry.Click+=(_,_)=>ChangeState(()=>Model.IsLocked=!Model.IsLocked);
        var foldEntry = new MenuItem(); menu.Items.Add(foldEntry); foldEntry.Click+=(_,_)=>ToggleCollapsed();
        menu.Opened+=(_,_)=>{lockEntry.IsChecked=Model.IsLocked;foldEntry.Header=Model.IsCollapsed?"展开":"收起";};
        if (body is FolderWidget && body.ContextMenu is ContextMenu folderMenu)
        {
            while (folderMenu.Items.Count > 0) { var entry=folderMenu.Items[0]; folderMenu.Items.RemoveAt(0); menu.Items.Add(entry); }
            body.ContextMenu=menu;
            var fit = new MenuItem { Header="边框贴合内容", IsCheckable=true }; menu.Items.Add(fit);
            fit.Click+=(_,_)=>ChangeState(()=>Model.FolderAutoHeight=fit.IsChecked);
            menu.Opened+=(_,_)=>fit.IsChecked=Model.FolderAutoHeight;
        }
        if (themed && themeChanged != null) foreach (var (name, style) in new[] { ("无色透明", "glass"), ("浅色纸片", "paper"), ("霓虹紫", "neon") }) { var choice = new MenuItem { Header = "主题 · " + name, IsCheckable = true, IsChecked = model.Style == style }; choice.Click += (_, _) => themeChanged(style); menu.Items.Add(choice); }
        var rename = new MenuItem { Header = "重命名插件" }; menu.Items.Add(rename);
        rename.Click += (_, _) => { _before = Copy(); var name = TextInputDialog.Ask(Window.GetWindow(this)!, "插件名称", _title.Text); if (name != null) { Model.Title = name.Trim(); UpdateState(); Commit(); } _before = null; };
        var auto = new MenuItem { Header = "鼠标离开后自动收起", IsCheckable = true, IsChecked = Model.AutoCollapse }; menu.Items.Add(auto);
        _auto = auto;
        auto.Click += (_, _) => { ChangeState(() => Model.AutoCollapse = auto.IsChecked); auto.IsChecked = Model.AutoCollapse; };
        var delete = new MenuItem { Header = "移除桌面插件（保留内容）" }; menu.Items.Add(delete); delete.Click += (_, _) => remove();
        MouseEnter += (_, _) => _leaveTimer.Stop(); MouseLeave += (_, _) => { if (Model.AutoCollapse && !Model.IsCollapsed) _leaveTimer.Start(); };
        _leaveTimer.Tick += (_, _) => { _leaveTimer.Stop(); if (!InteractionActive && !IsMouseOver && !IsKeyboardFocusWithin && !Model.IsCollapsed && Window.GetWindow(this)?.OwnedWindows.Cast<Window>().Any(w => w.IsVisible && w.IsActive) != true) ToggleCollapsed(); };
        Unloaded += (_, _) => _leaveTimer.Stop();
        PreviewMouseDown += (_, _) => { int highest = _canvas.Children.OfType<UIElement>().Select(Panel.GetZIndex).DefaultIfEmpty().Max(); Panel.SetZIndex(this, highest + 1); };
        UpdateState(); Constrain(false);
    }
    private void BeginGesture(object sender, DragStartedEventArgs e) { _gesturing = true; _before = Copy(); _dragX=Model.X; _dragY=Model.Y; _resizeDx=0; _resizeDy=0; _leaveTimer.Stop(); }
    private void EndGesture(object sender, DragCompletedEventArgs e)
    {
        _gesturing = false;
        if (Model.IsLocked) { _before = null; return; }
        if (e.Canceled && _before != null) Restore(_before);
        else
        {
            if (sender == _move && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) { Model.X = Math.Round(Model.X / 8) * 8; Model.Y = Math.Round(Model.Y / 8) * 8; }
            if(_body is FolderWidget resizedFolder && sender!=_move)
            {
                Model.FolderAutoHeight=false;
                Model.Width=Math.Min(Model.Width,resizedFolder.FittedWidth(Model.Width));
                Model.Height=Math.Min(Model.Height,resizedFolder.FittedHeight(Model.Width));
            }
            Constrain(false);
            if(sender!=_move && _before!=null) { if(_resizeLeft)Model.X=_before.X+_before.Width-Model.Width; if(_resizeTop)Model.Y=_before.Y+_before.Height-Model.Height; Constrain(false); }
            Commit();
        }
        _before = null;
    }
    private WidgetInstance Copy() => PluginRules.Clone(new WidgetConfig { Items = [Model] }).Items[0];
    private void Restore(WidgetInstance saved)
    {
        Model.X = saved.X; Model.Y = saved.Y; Model.Width = saved.Width; Model.Height = saved.Height;
        Model.PaneIndex = saved.PaneIndex; _paneInitialized = true;
        Model.IsLocked = saved.IsLocked; Model.IsCollapsed = saved.IsCollapsed; Model.Title = saved.Title; Model.AutoCollapse = saved.AutoCollapse;
        Model.FolderAutoHeight=saved.FolderAutoHeight;
        UpdateState(); Constrain(false);
    }
    private void Move(double dx, double dy)
    {
        if (Model.IsLocked) return;
        _dragX += dx; _dragY += dy;
        Model.PaneIndex = WidgetLayout.NearestPane(_dragX, Width, _canvas.ActualWidth);
        _paneInitialized = true; Model.X = _dragX; Model.Y = _dragY; Constrain(false);
    }
    private void Resize(double dx, double dy) => ResizeCorner(dx,dy,false,false);
    private void ResizeCorner(double dx,double dy,bool left,bool top)
    {
        if(Model.IsLocked||Model.IsCollapsed||_before==null)return;
        _resizeLeft=left; _resizeTop=top;
        _resizeDx+=dx; _resizeDy+=dy; var origin=_before;
        double paneWidth=_canvas.ActualWidth/3, paneLeft=Model.PaneIndex*paneWidth;
        double maxWidth=left?origin.X+origin.Width-paneLeft:paneLeft+paneWidth-origin.X;
        double maxHeight=top?origin.Y+origin.Height:_canvas.ActualHeight-origin.Y;
        Model.Width=Math.Clamp(origin.Width+(left?-_resizeDx:_resizeDx),Math.Min(_body is FolderWidget?32:200,maxWidth),maxWidth);
        Model.Height=Math.Clamp(origin.Height+(top?-_resizeDy:_resizeDy),Math.Min(_body is FolderWidget?32:120,maxHeight),maxHeight);
        Model.X=left?origin.X+origin.Width-Model.Width:origin.X; Model.Y=top?origin.Y+origin.Height-Model.Height:origin.Y;
        Model.FolderAutoHeight=false; Constrain(false);
    }
    public void Constrain(bool commit)
    {
        if (_canvas.ActualWidth <= 1 || _canvas.ActualHeight <= 1) return;
        double paneWidth = _canvas.ActualWidth / 3;
        if (!_paneInitialized) { Model.PaneIndex = WidgetLayout.NearestPane(Model.X, Model.IsCollapsed && _body is FolderWidget ? 180 : Model.Width, _canvas.ActualWidth); _paneInitialized = true; }
        double paneLeft = Model.PaneIndex * paneWidth;
        Model.Width = Math.Clamp(Model.Width, Math.Min(_body is FolderWidget?32:200,paneWidth), paneWidth);
        if (Model.IsCollapsed && _body is FolderWidget)
        {
            Width = Math.Min(180,paneWidth); Height = Math.Min(((FolderWidget)_body).CompactHeight,_canvas.ActualHeight);
            Model.X = Math.Clamp(Model.X,paneLeft,Math.Max(paneLeft,paneLeft+paneWidth-Width)); Model.Y = Math.Clamp(Model.Y,0,Math.Max(0,_canvas.ActualHeight-Height));
            Canvas.SetLeft(this,Model.X); Canvas.SetTop(this,Model.Y); if (commit) Commit(); return;
        }
        var bounds = WidgetLayout.ClampToPane(Model.X, Model.Y, Model.Width, Model.IsCollapsed ? 120 : Model.Height, Model.PaneIndex, _canvas.ActualWidth, _canvas.ActualHeight, _body is FolderWidget?32:200, _body is FolderWidget?32:120);
        Model.X = bounds.X; Model.Y = Model.IsCollapsed ? Math.Clamp(Model.Y, 0, Math.Max(0, _canvas.ActualHeight - 48)) : bounds.Y; Model.Width = bounds.Width;
        if (!Model.IsCollapsed) Model.Height = bounds.Height;
        if (!Model.IsCollapsed && _body is FolderWidget folder)
        {
            if(Model.FolderAutoHeight)Model.Width=Math.Min(paneWidth,folder.FittedWidth(Model.Width));
            Model.X=Math.Clamp(Model.X,paneLeft,Math.Max(paneLeft,paneLeft+paneWidth-Model.Width));
            Model.Height=Math.Min(_canvas.ActualHeight,Model.FolderAutoHeight?folder.FittedHeight(Model.Width):Math.Max(32,Model.Height));
            Model.Y=Math.Clamp(Model.Y,0,Math.Max(0,_canvas.ActualHeight-Model.Height));
        }
        Canvas.SetLeft(this, Model.X); Canvas.SetTop(this, Model.Y); Width = Model.Width; Height = Model.IsCollapsed ? 48 : Model.Height;
        if (commit) Commit();
    }
    private void UpdateState()
    {
        _title.Visibility = Model.PluginId is "clock" or "folder" ? Visibility.Collapsed : Visibility.Visible; _title.Text = string.IsNullOrWhiteSpace(Model.Title) ? PluginRules.Catalog.First(p => p.Id == Model.PluginId).Name : Model.Title;
        if (_body is FolderWidget) ToolTip=_title.Text;
        _fold.Content = Model.IsCollapsed ? "展开" : "收起"; _lock.Content = Model.IsLocked ? "锁定" : "自由";
        bool phone = Model.IsCollapsed && _body is FolderWidget;
        _title.Margin = new Thickness(0,0,phone ? 34 : 112,0); _lock.Visibility = phone ? Visibility.Collapsed : Visibility.Visible;
        if (phone) _fold.Content = "↗";
        if (_actions != null) _actions.Opacity = Model.IsCollapsed || IsMouseOver ? 1 : 0;
        _resize.Opacity = IsMouseOver ? 1 : 0;
        if (_body is FolderWidget folder) folder.SetCompact(Model.IsCollapsed);
        _body.Visibility = Model.IsCollapsed && _body is not FolderWidget ? Visibility.Collapsed : Visibility.Visible; _resize.Visibility = Model.IsLocked || Model.IsCollapsed ? Visibility.Collapsed : Visibility.Visible;
        foreach(var corner in _corners) corner.Visibility=Model.IsLocked||Model.IsCollapsed?Visibility.Collapsed:Visibility.Visible;
        Height = Model.IsCollapsed ? (_body is FolderWidget compactFolder ? compactFolder.CompactHeight : 48) : Model.Height; Width = Model.IsCollapsed && _body is FolderWidget ? 180 : Model.Width;
    }
    private void ChangeState(Action change) { _before = Copy(); change(); UpdateState(); Constrain(false); Commit(); _before = null; }
    private void ToggleCollapsed() => ChangeState(() => Model.IsCollapsed = !Model.IsCollapsed);
    private void Commit()
    {
        if (!_save(Model) && _before != null) Restore(_before);
    }
    internal void TestDrag(double x, double y) { _move.RaiseEvent(new DragStartedEventArgs(0, 0)); _move.RaiseEvent(new DragDeltaEventArgs(x, y)); _move.RaiseEvent(new DragCompletedEventArgs(x, y, false)); }
    internal void TestEdgeExpansion(int edge)=>ExpandFolderEdge(edge);
    internal void TestEdgeFinish(bool accepted)=>FinishFolderEdge(accepted);
    internal void TestResize(double x, double y) { _resize.RaiseEvent(new DragStartedEventArgs(0, 0)); _resize.RaiseEvent(new DragDeltaEventArgs(x, y)); _resize.RaiseEvent(new DragCompletedEventArgs(x, y, false)); }
    internal void TestCornerResize(int corner,double x,double y) { var thumb=_corners[corner]; thumb.RaiseEvent(new DragStartedEventArgs(0,0)); thumb.RaiseEvent(new DragDeltaEventArgs(x,y)); thumb.RaiseEvent(new DragCompletedEventArgs(x,y,false)); }
    internal void TestLock() => _lock.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    internal void TestCollapse() => _fold.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    internal void TestAutoCollapse() { if (_auto != null) { _auto.IsChecked = !_auto.IsChecked; _auto.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent)); } }
}
