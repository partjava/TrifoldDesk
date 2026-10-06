using System.Windows.Input;
using System.Windows.Controls.Primitives;
using TrifoldDesk.Services;
namespace TrifoldDesk;
public sealed record FolderDrag(string FolderId, string ItemId, bool Desktop = false, ShortcutItem? MappedItem = null);
public sealed record LibraryDrag(int Screen, string ItemId);
public sealed class FolderWidget : Grid
{
    public const string DragFormat = "TrifoldDesk.FolderEntry";
    public const string LibraryFormat = "TrifoldDesk.LibraryEntry";
    private readonly string _id;
    private string _currentFolderId;
    private bool _mappedReadOnly;
    private bool _navigationVisible;
    private readonly DockPanel _navigation = new() { Margin = new Thickness(0, 0, 0, 5), Visibility = Visibility.Collapsed };
    private readonly TextBlock _navigationTitle = new() { FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _back = new() { Content = "←", Width = 27, Height = 25, Padding = new Thickness(0), Margin = new Thickness(0, 0, 6, 0), ToolTip = "返回" };
    public event Action<ShortcutItem>? FolderOpenRequested;
    public event Action<ShortcutItem>? LaunchSettingsRequested;
    public event Action? BackRequested;
    internal void ConfigureWorkspace(string id, Func<FolderData> read, Action<string[]> add, Action<FolderDrag,int,bool> transfer, Action<string> remove, Action<string,int> export, Action<LibraryDrag,bool> import)
    { _currentFolderId=id; _read=read; _add=add; _transfer=transfer; _remove=remove; _export=export; _import=import; }
    internal void SetNavigation(string title, bool canBack, bool mapped)
    {
        bool changed=_mappedReadOnly!=mapped; _mappedReadOnly=mapped;
        _navigationTitle.Text=title; _navigationTitle.ToolTip=title; _back.Visibility=canBack?Visibility.Visible:Visibility.Collapsed;
        _navigationVisible=canBack||mapped; _navigation.Visibility=_navigationVisible&&!_compact?Visibility.Visible:Visibility.Collapsed;
        if(changed)_buttons.Clear();
    }
    internal bool TestMappedReadOnly=>_mappedReadOnly;
    internal string TestCurrentFolderId=>_currentFolderId;
    internal void TestOpenEntry(int index)=>FolderOpenRequested?.Invoke(_read().Items[index]);
    internal void TestBack()=>BackRequested?.Invoke();
    private Func<FolderData> _read;
    private Action<string[]> _add;
    private Action<FolderDrag, int, bool> _transfer;
    private Action<string> _remove;
    private Action<string, int> _export;
    private Action<LibraryDrag, bool> _import;
    private readonly WrapPanel _icons = new();
    private readonly Dictionary<string,(string Signature,Button Button)> _buttons=[];
    private string _previewSignature=""; private int _lastColumns=-1,_lastCapacity=-1;
    private readonly TextBlock _count = new();
    private Point _press;
    private bool _dragged;
    private bool _busy;
    private readonly UniformGrid _previewIcons = new() { Columns = 3, Rows = 3 };
    private readonly Border _preview = new() { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), Visibility = Visibility.Collapsed };
    private bool _compact;
    private bool _dropGridActive;
    private readonly System.Windows.Threading.DispatcherTimer _edgeTimer = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private int _edge;
    public event Action<int>? EdgeExpansionRequested;
    public event Action<bool>? EdgeExpansionFinished;
    public event Action<bool>? DragActiveChanged;
    internal void EndEdgeDrag()=>StopEdge(false);
    internal void TestEdgeHover()=>TrackEdge(new Point(ActualWidth-1,ActualHeight/2));
    private void StopEdge(bool accepted) { _edgeTimer.Stop(); _edge=0; EdgeExpansionFinished?.Invoke(accepted); }
    private void TrackEdge(Point point)
    {
        int edge = point.X < 18 ? 1 : point.X > ActualWidth-18 ? 2 : point.Y < 18 ? 3 : point.Y > ActualHeight-18 ? 4 : 0;
        if(edge==_edge)return;
        _edgeTimer.Stop(); _edge=edge;
        if(edge!=0)_edgeTimer.Start();
    }
    public event Action? ExpandRequested;
    public event Action? ContentChanged;
    public double FittedHeight(double width) => Math.Max(_dropGridActive?146:80, Math.Ceiling((FolderRules.Positions(_read()).Values.DefaultIfEmpty(0).Max()+1) / (double)Math.Max(1,(int)((width-10)/70))) * 66 + 10);
    public double FittedWidth(double width)
    {
        int columns=Math.Max(1,(int)((width-10)/70));
        int extent=FolderRules.Positions(_read()).Values.DefaultIfEmpty(0).Max()+1;
        return _dropGridActive?Math.Max(220,width):Math.Max(80,Math.Min(columns,extent)*70+10);
    }
    private void SetDropGrid(bool value) { if(_dropGridActive==value)return;_dropGridActive=value;ContentChanged?.Invoke(); }
    public double CompactHeight => Math.Min(3,Math.Max(1,(int)Math.Ceiling(_read().Items.Count/3d))) * 36 + 12;
    public void SetCompact(bool value)
    {
        _compact = value;
        foreach (UIElement child in Children) child.Visibility = child == _preview ? (value ? Visibility.Visible : Visibility.Collapsed) : child==_navigation ? (!value&&_navigationVisible?Visibility.Visible:Visibility.Collapsed) : (value ? Visibility.Collapsed : Visibility.Visible);
        RefreshPreview();
    }
    private void RefreshPreview()
    {
        var previewItems=_read().Items.Take(9).ToArray(); var signature=_read().Items.Count+"|"+System.Text.Json.JsonSerializer.Serialize(previewItems); if(signature==_previewSignature)return; _previewSignature=signature;
        _previewIcons.Children.Clear();
        _previewIcons.Rows = Math.Min(3,Math.Max(1,(int)Math.Ceiling(_read().Items.Count/3d)));
        foreach (var item in _read().Items.Take(9)) _previewIcons.Children.Add(new Image { Source = ItemIcon(item), Width = 30, Height = 30, Margin = new Thickness(3), ToolTip = item.Name });
        _preview.ToolTip = $"{_read().Items.Count} 个入口 · 点击展开 · 可直接拖入";
    }
    internal int PreviewCount => Math.Min(9,_read().Items.Count);
    internal void TestPreviewClick() => _preview.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent = MouseLeftButtonUpEvent });
    public bool InteractionActive => _busy || _edgeTimer.IsEnabled || ContextMenu?.IsOpen == true || _icons.Children.OfType<Button>().Any(b => b.ContextMenu?.IsOpen == true);
    public FolderWidget(string id, Func<FolderData> read, Action<string[]> add, Action<FolderDrag, int, bool> transfer, Action<string> remove, Action<string, int> export, Action<LibraryDrag, bool> import)
    {
        _id = _currentFolderId = id; _read = read; _add = add; _transfer = transfer; _remove = remove; _export = export; _import = import;
        _edgeTimer.Tick+=(_,_)=> { _edgeTimer.Stop();if(_edge!=0)EdgeExpansionRequested?.Invoke(_edge); };
        Unloaded+=(_,_)=>_edgeTimer.Stop();
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); RowDefinitions.Add(new RowDefinition()); RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _navigation.Children.Add(_back); _navigation.Children.Add(_navigationTitle); Children.Add(_navigation);
        _back.Click+=(_,_)=>BackRequested?.Invoke();
        ToolTip = "拖入添加 · 拖动图标分类 · Ctrl拖动复制";
        var scroll = new ScrollViewer { Content = _icons, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; Grid.SetRow(scroll, 1); Children.Add(scroll);
        var footer = new DockPanel { LastChildFill = false, Margin = new Thickness(0, 2, 0, 0), Visibility = Visibility.Collapsed }; Grid.SetRow(footer, 2); Children.Add(footer);
        var addButton = new Button { Content = "＋", ToolTip = "添加入口", Width = 28, Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 15, Padding = new Thickness(8, 5, 8, 5) }; DockPanel.SetDock(addButton, Dock.Right); footer.Children.Add(addButton);
        addButton.Click += (_, _) => { _busy = true; try { var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "添加应用、快捷方式或资料" }; if (dialog.ShowDialog(Window.GetWindow(this)) == true) _add(dialog.FileNames); } finally { _busy = false; } };
        var folderMenu = new ContextMenu(); ContextMenu = folderMenu;
        var installedEntry=new MenuItem { Header="从已安装应用添加…" };folderMenu.Items.Add(installedEntry);
        installedEntry.Click+=(_,_)=>
        {
            _busy=true;
            try { var picker=new InstalledAppsWindow{Owner=Window.GetWindow(this)};if(picker.ShowDialog()==true)_add(picker.Selected.Select(ShellLinkHelper.SaveShellAppLink).ToArray()); }
            catch(Exception ex){App.Log(ex);MessageBox.Show(Window.GetWindow(this),ex.Message,"应用列表无法读取");}
            finally{_busy=false;}
        };
        var addEntry = new MenuItem { Header = "添加入口" }; folderMenu.Items.Add(addEntry);
        addEntry.Click += (_,_) => addButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        folderMenu.Opened+=(_,_)=>{ installedEntry.IsEnabled=addEntry.IsEnabled=!_mappedReadOnly; };
        Children.Remove(footer);
        _count.FontSize = 11; _count.Opacity = .75; _count.VerticalAlignment = VerticalAlignment.Center;
        AllowDrop = true; Background = Brushes.Transparent;
        DragOver += (_, e) => { if(_mappedReadOnly){e.Effects=DragDropEffects.None;e.Handled=true;return;} e.Effects = e.Data.GetDataPresent(DragFormat) || e.Data.GetDataPresent(LibraryFormat) ? (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)||e.Data.GetData(DragFormat) is FolderDrag { MappedItem: not null } ? DragDropEffects.Copy : DragDropEffects.Move) : e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; if(e.Effects!=DragDropEffects.None)TrackEdge(e.GetPosition(this)); e.Handled = true; };
        DragLeave+=(_,e)=>{var point=e.GetPosition(this);if(point.X<0||point.Y<0||point.X>ActualWidth||point.Y>ActualHeight){StopEdge(false);SetDropGrid(false);}};
        Drop += (_, e) => { var point=e.GetPosition(_icons); int columns=Math.Max(1,(int)(scroll.ActualWidth/70)); DropAt(e,Math.Max(0,(int)(point.Y/66))*columns+Math.Clamp((int)(point.X/70),0,columns-1)); };
        scroll.SizeChanged += (_, _) => { int columns=Math.Max(1,(int)(scroll.ActualWidth/70)); int capacity=columns*Math.Max(1,(int)(scroll.ActualHeight/66)); if(columns==_lastColumns && capacity==_lastCapacity)return; _lastColumns=columns; _lastCapacity=capacity; Refresh(); };
        _preview.Child = _previewIcons; Grid.SetRowSpan(_preview,3); Children.Add(_preview);
        _preview.MouseLeftButtonUp += (_, e) => { if (_compact) { ExpandRequested?.Invoke(); e.Handled = true; } };
        Refresh();
    }
    private void DropAt(DragEventArgs e, int index)
    {
        if(_mappedReadOnly){e.Effects=DragDropEffects.None;e.Handled=true;return;}
        StopEdge(true);
        if (e.Data.GetData(DragFormat) is FolderDrag source) _transfer(source, index, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        else if (e.Data.GetData(LibraryFormat) is LibraryDrag library) _import(library, Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
        else if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) _add(paths);
        e.Handled = true;
        SetDropGrid(false);
    }
    public void Refresh()
    {
        RefreshPreview();
        _icons.Children.Clear();
        var folder=_read(); foreach(var stale in _buttons.Keys.Where(id=>!folder.Items.Any(i=>i.Id==id)).ToArray())_buttons.Remove(stale);
        var positions=FolderRules.Positions(folder);
        foreach (var item in _read().Items.OrderBy(i=>positions[i.Id]))
        {
            int index = positions[item.Id];
            while(_icons.Children.Count<index)AddEmptySlot(_icons.Children.Count);
            var signature=System.Text.Json.JsonSerializer.Serialize(item);
            if(_buttons.TryGetValue(item.Id,out var cached) && cached.Signature==signature){ cached.Button.Tag=index; _icons.Children.Add(cached.Button); continue; }
            var button = new Button { Width = 68, Height = 64, AllowDrop=true, Padding = new Thickness(2), Margin = new Thickness(1), Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, ToolTip = item.Name + "\n" + item.SourcePath };
            var stack = new StackPanel(); button.Content = stack;
            stack.Children.Add(new Image { Source = ItemIcon(item), Width = 40, Height = 40 });
            stack.Children.Add(new TextBlock { Text = item.Name, FontSize = 11, TextAlignment = TextAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) });
            button.PreviewMouseLeftButtonDown += (_, e) => { _press = e.GetPosition(button); _dragged = false; button.CaptureMouse(); e.Handled=true; };
            button.PreviewMouseLeftButtonUp += (_, e) => { var point=e.GetPosition(button); button.ReleaseMouseCapture(); e.Handled=true; if(!_dragged&&point.X>=0&&point.Y>=0&&point.X<=button.ActualWidth&&point.Y<=button.ActualHeight)button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
            button.PreviewMouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || _dragged) return;
                var point = e.GetPosition(button); if (Math.Abs(point.X - _press.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - _press.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                _dragged = true; button.ReleaseMouseCapture(); var data = new DataObject(); data.SetData(DragFormat, new FolderDrag(_currentFolderId, item.Id, false, _mappedReadOnly?item:null)); _busy = true; DragActiveChanged?.Invoke(true); try { var result=DragDrop.DoDragDrop(this, data, _mappedReadOnly?DragDropEffects.Copy:DragDropEffects.Move | DragDropEffects.Copy); StopEdge(result!=DragDropEffects.None); } finally { _busy = false; StopEdge(false); SetDropGrid(false); DragActiveChanged?.Invoke(false); } e.Handled = true;
            };
            button.Click += (_, _) => { if (_dragged) { _dragged = false; return; } try { if(!string.IsNullOrEmpty(item.ChildFolderId)||(_mappedReadOnly&&Directory.Exists(item.SourcePath)))FolderOpenRequested?.Invoke(item);else ShellLinkHelper.Launch(item); } catch (Exception ex) { App.Log(ex); MessageBox.Show(Window.GetWindow(this), ex.Message, "入口无法打开"); } };
            button.Tag=index; button.Drop += (_, e) => DropAt(e,(int)button.Tag);
            var menu = new ContextMenu(); button.ContextMenu = menu;
            var launchSettings = new MenuItem { Header = "启动方式…" }; launchSettings.Click+=(_,_)=>LaunchSettingsRequested?.Invoke(item); menu.Items.Add(launchSettings);
            var delete = new MenuItem { Header = "从文件夹移除（保留源文件）" }; delete.Click += (_, _) => _remove(item.Id); menu.Items.Add(delete);
            foreach (int screen in new[] { 0, 1 }) { int target = screen; var move = new MenuItem { Header = screen == 0 ? "移出到应用库" : "移出到资料库" }; move.Click += (_, _) => _export(item.Id, target); menu.Items.Add(move); }
            menu.Opened+=(_,_)=> { foreach(var action in menu.Items.OfType<MenuItem>())action.IsEnabled=!_mappedReadOnly; };
            _buttons[item.Id]=(signature,button); _icons.Children.Add(button);
        }
        int capacity=Math.Max(1,(int)(Math.Max(70,ActualWidth)/70))*Math.Max(1,(int)(Math.Max(66,ActualHeight)/66));
        while(_icons.Children.Count<Math.Min(4096,capacity))AddEmptySlot(_icons.Children.Count);
        UpdateCount(ActualWidth, Math.Max(0, ActualHeight - 85));
        ContentChanged?.Invoke();
    }
    private static ImageSource? ItemIcon(ShortcutItem item) => !string.IsNullOrEmpty(item.ChildFolderId)
        ? ShellLinkHelper.GetIcon(new ShortcutItem { SourcePath=Environment.GetFolderPath(Environment.SpecialFolder.Windows),TargetPath=Environment.GetFolderPath(Environment.SpecialFolder.Windows) }) : ShellLinkHelper.GetIcon(item);
    private void AddEmptySlot(int slot)
    {
        var cell=new Border { Width=68,Height=64,Margin=new Thickness(1),Background=Brushes.Transparent,AllowDrop=true };
        cell.Drop+=(_,e)=>DropAt(e,slot); _icons.Children.Add(cell);
    }
    private void UpdateCount(double width, double height) => _count.Text = $"{_read().Items.Count} 个入口 · {Math.Max(1, (int)(width / 84))} 列 × {Math.Max(0, (int)(height / 86))} 行";
    internal void TestBusy(bool value) => _busy = value;
    internal void TestDropGrid(bool value)=>SetDropGrid(value);
    internal double TestScrollToEnd() { var scroll=Children.OfType<ScrollViewer>().Single(); scroll.UpdateLayout();scroll.ScrollToEnd();scroll.UpdateLayout();return scroll.VerticalOffset; }
}
