namespace TrifoldDesk;
public partial class MainWindow
{
    private WidgetConfig _widgets = new();
    private FolderConfig _folders = new();
    private readonly List<CalendarWidget> _calendars = [];
    private readonly Dictionary<string, FrameworkElement> _widgetContents = [];
    private readonly Dictionary<string, WidgetFrame> _frames = [];
    private readonly Dictionary<string, FolderWidget> _folderViews = [];
    private readonly Dictionary<string, DashboardWidget> _dashboards = [];
    private bool _buildingWidgets;
    private bool _widgetLayoutReady;
    private bool HasPlugin(string id) => _widgets.Items.Any(i => i.PluginId == id);
    private static void Detach(FrameworkElement element)
    {
        switch (element.Parent)
        {
            case Panel panel: panel.Children.Remove(element); break;
            case Border border: border.Child = null; break;
            case ContentControl content: content.Content = null; break;
            case Decorator decorator: decorator.Child = null; break;
        }
    }
    private void InitializePlugins()
    {
        bool legacy = File.Exists(Path.Combine(App.DataDirectory, "settings.json")) || File.Exists(Path.Combine(App.DataDirectory, "shortcuts.json"));
        var loaded = _config.Load<WidgetConfig>("widgets.json"); bool hasLayout = File.Exists(Path.Combine(App.DataDirectory, "widgets.json"));
        _widgets = hasLayout ? PluginRules.Normalize(loaded.Value) : PluginRules.Defaults(legacy);
        var folderLoad = _config.Load<FolderConfig>("folders.json"); _folders = folderLoad.Value;
        _folders.Items ??= []; foreach (var folder in _folders.Items) folder.Items ??= [];
        _folders.DesktopEntries ??= [];
        if (loaded.Warning != null) _loadWarning += "\n" + loaded.Warning;
        if (folderLoad.Warning != null) _loadWarning += "\n" + folderLoad.Warning;
        InitializeMonitorLayouts();
        MigrateLegacyWidgets();
        InitializeDesktopDrop();
        if (loaded.Warning != null) _loadWarning += "\n" + loaded.Warning;
        if (folderLoad.Warning != null) _loadWarning += "\n" + folderLoad.Warning;
        if (!hasLayout) { try { _config.Save("widgets.json", _widgets); } catch (Exception ex) { App.Log(ex); _loadWarning += "\n默认插件布局未保存。"; } }
        foreach (var (id, element) in new (string, FrameworkElement)[] { ("apps", AppsLibraryPanel), ("projects", ProjectsLibraryPanel), ("cpu", CpuWidget), ("memory", MemoryWidget), ("network", NetworkWidget), ("controls", ControlsContent) })
        { Detach(element); element.Margin = new Thickness(0); _widgetContents[id] = element; }
        Detach(DriveHeading); Detach(DriveWidgets);
        var driveStack = new StackPanel(); driveStack.Children.Add(DriveHeading); driveStack.Children.Add(DriveWidgets);
        _widgetContents["disks"] = new ScrollViewer { Content = driveStack, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
        foreach (var border in PanelRoot.Children.OfType<Border>()) if (border.Child != null) border.Child.Visibility = Visibility.Collapsed;
        WidgetCanvas.SizeChanged += (_, _) => { if (!_widgetLayoutReady) return; if (_frames.Count == 0) ApplyPlugins(); else foreach (var frame in _frames.Values) frame.Constrain(false); };
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => { UpdateLayout(); _widgetLayoutReady = true; ApplyPlugins(); });
    }
    private void OpenManagementClick(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen) return; _dialogOpen = true;
        try
        {
            Detach(ManagementContent);
            var dialog = new Window { Owner = this, Title = "全部入口管理", Width = 650, Height = 760, MaxHeight = SystemParameters.WorkArea.Height, Background = new SolidColorBrush(Color.FromRgb(22, 36, 48)), WindowStartupLocation = WindowStartupLocation.CenterOwner, DataContext = _viewModel, Content = ManagementContent };
            ManagementContent.Margin = new Thickness(24); dialog.ShowDialog(); dialog.Content = null;
        }
        finally { _dialogOpen = false; _leftAt = DateTime.UtcNow; }
    }
    private void PluginLibraryClick(object sender, RoutedEventArgs e)
    {
        if (_dialogOpen) return; _dialogOpen = true;
        try { var library = new PluginLibraryWindow(_widgets) { Owner = this }; if (library.ShowDialog() == true) SaveWidgetConfig(library.Result); }
        finally { _dialogOpen = false; _leftAt = DateTime.UtcNow; }
    }
    private bool SaveWidgetConfig(WidgetConfig value)
    {
        try { var clean = PluginRules.Normalize(value); PersistWidgetLayout(clean); _widgets = clean; ApplyPlugins(); return true; }
        catch (Exception ex) { Fail("插件配置未保存，桌面保持原布局", ex); return false; }
    }
    private bool SaveFrame(WidgetInstance frame)
    {
        if(!_frames.TryGetValue(frame.InstanceId,out var live)||!ReferenceEquals(live.Model,frame))return false;
        try
        {
            var next = PluginRules.Clone(_widgets); int index = next.Items.FindIndex(i => i.InstanceId == frame.InstanceId); if (index < 0) return false;
            if(!App.IsSelfTest && _frames.TryGetValue(frame.InstanceId,out var moved))
            {
                double paneWidth=WidgetCanvas.ActualWidth/3,left=frame.PaneIndex*paneWidth;
                var requested=new WidgetBounds(frame.X-left,frame.Y,moved.ActualWidth,moved.ActualHeight);
                var occupied=_frames.Values.Where(f=>f.Model.InstanceId!=frame.InstanceId && f.Model.PaneIndex==frame.PaneIndex).Select(f=>new WidgetBounds(f.Model.X-left,f.Model.Y,f.ActualWidth,f.ActualHeight));
                var free=WidgetPlacement.Avoid(requested,occupied,paneWidth,WidgetCanvas.ActualHeight);
                if(free.HasValue){frame.X=free.Value.X+left;frame.Y=free.Value.Y;Canvas.SetLeft(moved,frame.X);Canvas.SetTop(moved,frame.Y);}
                else{_viewModel.Status="当前页面没有足够空位，保留原位置。";return false;}
            }
            next.Items[index] = PluginRules.Clone(new WidgetConfig { Items = [frame] }).Items[0]; PersistWidgetLayout(next); _widgets = next; return true;
        }
        catch (Exception ex) { Fail("布局未保存", ex); return false; }
    }
    private void RemoveWidget(string id) => SaveWidgetConfig(new WidgetConfig { Items = _widgets.Items.Where(i => i.InstanceId != id).ToList() });
    private void ApplyPlugins()
    {
        if (!_widgetLayoutReady || _buildingWidgets) return; _buildingWidgets = true;
        try
        {
            ResetFolderWorkspaceViews();
            foreach (var element in _widgetContents.Values) Detach(element);
            WidgetCanvas.Children.Clear(); _frames.Clear(); _folderViews.Clear(); _calendars.Clear(); _dashboards.Clear();
            double w = WidgetCanvas.ActualWidth > 1 ? WidgetCanvas.ActualWidth : 1400, h = WidgetCanvas.ActualHeight > 1 ? WidgetCanvas.ActualHeight : 750, pane = w / 3;
            int calendarNumber = 0, folderNumber = 0;
            foreach (var original in _widgets.Items)
            {
                var item = PluginRules.Clone(new WidgetConfig { Items = [original] }).Items[0]; FrameworkElement body;
                if (item.PluginId == "calendar") { var calendar = new CalendarWidget(item.Style,()=>_widgets.Items.FirstOrDefault(w=>w.InstanceId==item.InstanceId)?.Dates??[],dates=>SaveCalendarDates(item.InstanceId,dates)); _calendars.Add(calendar); body = new ScrollViewer { Content = calendar, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden }; }
                else if (item.PluginId == "folder")
                {
                    var folder = new FolderWidget(item.InstanceId, () => ReadFolder(item.InstanceId), paths => AddFolderPaths(item.InstanceId, paths), (source, index, copy) => TransferFolder(source, item.InstanceId, index, copy), entry => RemoveFolderEntry(item.InstanceId, entry), (entry, screen) => ExportFolderEntry(item.InstanceId, entry, screen), (source, copy) => ImportLibraryEntry(item.InstanceId, source, copy));
                    WireFolderWorkspace(item, folder);
                    folder.DragActiveChanged+=active=> { _dragging=active;if(!active) { _leftAt=DateTime.UtcNow;EndFolderEdgeDrags(); } };
                    _folderViews[item.InstanceId] = folder; body = folder;
                }
                else if (item.PluginId is "cpu" or "memory" or "network" or "gpu" or "clock" or "battery") { var dashboard = new DashboardWidget(item.PluginId, item.Style, _viewModel); _dashboards[item.PluginId] = dashboard; body = dashboard; }
                else if (item.PluginId == "external-plugin")body=new ExternalPluginWidget(item.ExternalPluginId,_config);
                else if (item.PluginId == "workbench")body=new ProductivityWidget(item.InstanceId,_config);
                else if (item.PluginId == "weather")body=new WeatherWidget(item.InstanceId,_config);
                else if (item.PluginId == "music")body=new MusicWidget(item.InstanceId,_config);
                else if (item.PluginId == "project-browser")body=new ProjectBrowserWidget(item.InstanceId,_config);
                else if (item.PluginId == "system-summary") body = new CombinedStatusWidget(item, _viewModel);
                else body = _widgetContents[item.PluginId];
                body.Visibility = Visibility.Visible; body.DataContext = _viewModel;
                if (item.X < 0 || item.Y < 0 || item.Width <= 0 || item.Height <= 0)
                {
                    item.X = item.PaneIndex * pane + 8; item.Y = 8; item.Width = Math.Max(200, pane - 20); item.Height = Math.Max(120, h - 16);
                    if(item.PluginId is "workbench" or "weather" or "music" or "project-browser"){item.Width=Math.Min(350,pane-20);item.Height=270;}
                    switch (item.PluginId)
                    {
                        case "calendar": item.Width = Math.Min(350, pane - 20); item.Height = 340; item.Y = 8 + calendarNumber++ * 40; if (HasPlugin("disks")) { item.Y = h - 116; item.IsCollapsed = true; } break;
                        case "apps" or "projects": if (_widgets.Items.Any(i => i.PluginId == "folder" && i.PaneIndex == item.PaneIndex)) item.Height = Math.Max(220, h * .47); break;
                        case "folder": item.Width = Math.Min(380, pane - 20); item.Height = Math.Max(200, Math.Min(340, h * .43)); item.Y = h * .52 + folderNumber++ * 24; break;
                        case "system-summary": item.Width = Math.Min(380, pane - 20); item.Height = 260; break;
                        case "clock": item.Width = Math.Min(350, pane - 20); item.Height = 220; break;
                        case "battery": item.Width = Math.Min(300, pane - 20); item.Height = 220; item.Y = 250; break;
                        case "gpu": item.Width = Math.Min(300, pane - 20); item.Height = 250; item.Y = 488; break;
                        case "cpu": item.Width = pane / 2 - 14; item.Height = 220; break;
                        case "memory": item.X += pane / 2; item.Width = pane / 2 - 14; item.Height = 220; break;
                        case "network": item.Y = 242; item.Height = 180; break;
                        case "disks": item.Y = 438; item.Height = Math.Max(160, h - 438 - 130); break;
                        case "controls": item.Y = h - 58; item.Height = 620; item.IsCollapsed = HasPlugin("disks"); break;
                    }
                }
                if(body is ScrollViewer {Content: CalendarWidget})item.Height=Math.Min(item.Height,340+(item.Dates?.Count??0)*34);
                var frame = new WidgetFrame(item, body, WidgetCanvas, SaveFrame, () => RemoveWidget(item.InstanceId), style => ChangeWidgetTheme(item.InstanceId, style));
                if(body is ScrollViewer {Content: CalendarWidget dateCalendar})
                {
                    var addDate=new MenuItem{Header="添加日期"};addDate.Click+=(_,_)=>dateCalendar.AddDateCounter();frame.ContextMenu!.Items.Add(addDate);
                    var agenda=new MenuItem{Header="日程 / 周历…"};agenda.Click+=(_,_)=>new AgendaWindow(_config){Owner=this}.ShowDialog();frame.ContextMenu.Items.Add(agenda);
                    dateCalendar.ContextMenu=frame.ContextMenu;
                }
                if (item.PluginId is "apps" or "projects") AttachLibraryDrop(frame, item.PluginId == "apps" ? 0 : 1);
                if (item.PluginId == "system-summary")
                {
                    var sensors=new MenuItem{Header="温度 / 风扇 / 显存…"};sensors.Click+=(_,_)=>new HardwareDetailsWindow{Owner=this}.ShowDialog();frame.ContextMenu.Items.Add(sensors);
                    var choices = new MenuItem { Header = "显示内容" }; frame.ContextMenu.Items.Insert(0, choices);
                    foreach (var module in SummaryRules.Available)
                    {
                        var choice = new MenuItem { Header = PluginRules.Catalog.First(p => p.Id == module).Name, IsCheckable = true, IsChecked = SummaryRules.Modules(item).Contains(module) };
                        choices.Items.Add(choice); choice.Click += (_, _) =>
                        {
                            var next = PluginRules.Clone(_widgets); var target = next.Items.First(i => i.InstanceId == item.InstanceId); var modules = SummaryRules.Modules(target).ToList();
                            if (choice.IsChecked) modules.Add(module); else modules.Remove(module);
                            if (modules.Count == 0) { choice.IsChecked = true; return; }
                            target.Modules = modules.Distinct().ToList(); if (!SaveWidgetConfig(next)) choice.IsChecked = SummaryRules.Modules(item).Contains(module);
                        };
                    }
                }
                _frames[item.InstanceId] = frame; WidgetCanvas.Children.Add(frame); frame.Constrain(false);
            }
            _monitor.CpuEnabled = SummaryRules.Needs(_widgets, "cpu"); _monitor.MemoryEnabled = SummaryRules.Needs(_widgets, "memory"); _monitor.NetworkEnabled = SummaryRules.Needs(_widgets, "network");
            _monitor.SetEnabled(_monitor.CpuEnabled || _monitor.MemoryEnabled || _monitor.NetworkEnabled); _disks.SetEnabled(SummaryRules.Needs(_widgets,"disks"));
            if (HasPlugin("controls")) { HubTabs.SelectedIndex = 2; HubTabChanged(HubTabs, new SelectionChangedEventArgs(System.Windows.Controls.Primitives.Selector.SelectionChangedEvent, Array.Empty<object>(), Array.Empty<object>()) { Source = HubTabs }); }
            else _controlTimer.Stop();
            // Commit a complete migrated layout only once the real work area is known.
            if (WidgetCanvas.ActualWidth > 1 && WidgetCanvas.ActualHeight > 1)
            {
                var initialized = new WidgetConfig { Items = _frames.Values.Select(f => PluginRules.Clone(new WidgetConfig { Items = [f.Model] }).Items[0]).ToList() };
                try { if (System.Text.Json.JsonSerializer.Serialize(initialized) != System.Text.Json.JsonSerializer.Serialize(_widgets)) _config.Save("widgets.json", initialized); _widgets = initialized; }
                catch (Exception ex) { App.Log(ex); _viewModel.Status = "默认布局尚未保存。"; }
            }
        }
        finally { _buildingWidgets = false; RefreshDesktopIcons(); }
    }
    private void AttachLibraryDrop(WidgetFrame frame, int screen)
    {
        frame.AllowDrop = true;
        frame.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(FolderWidget.DragFormat) ? DragDropEffects.Move : e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };
        frame.Drop += (_, e) => { if (e.Data.GetData(FolderWidget.DragFormat) is FolderDrag source) ExportFolderEntry(source.FolderId, source.ItemId, screen); else if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddPaths(paths, screen); e.Handled = true; };
    }
    private FolderData ReadFolder(string id) => _folders.Items.FirstOrDefault(f => f.Id == id) ?? new FolderData { Id = id };
    private bool SaveCalendarDates(string id,List<DateCounter> dates)
    {
        var next=PluginRules.Clone(_widgets);var item=next.Items.FirstOrDefault(w=>w.InstanceId==id);if(item==null)return false;
        item.Dates=dates;item.Height=340+dates.Count*34;
        return SaveWidgetConfig(next);
    }
    private void ImportLibraryEntry(string target, LibraryDrag source, bool copy)
    {
        var collection = Collection(source.Screen); var view = collection.FirstOrDefault(v => v.Item.Id == source.ItemId); if (view == null) return;
        var next = FolderRules.Clone(_folders); var folder = next.Items.FirstOrDefault(f => f.Id == target);
        if (folder == null) { folder = new FolderData { Id = target }; next.Items.Add(folder); }
        if (folder.Items.Any(i => i.SourcePath.Equals(view.Item.SourcePath, StringComparison.OrdinalIgnoreCase))) return;
        var cloned = System.Text.Json.JsonSerializer.Deserialize<ShortcutItem>(System.Text.Json.JsonSerializer.Serialize(view.Item))!; cloned.Id = Guid.NewGuid().ToString("N"); cloned.Order = folder.Items.Count; folder.Items.Add(cloned);
        if (!SaveFolders(next) || copy) return;
        int index = collection.IndexOf(view); collection.Remove(view);
        if (!SaveShortcuts()) { collection.Insert(index, view); _viewModel.RebuildLibrary(); _viewModel.Status = "目标文件夹已保存；原入口保留，请稍后重试移除。"; }
    }
    private Point _libraryPress;
    private bool _libraryDragging;
    private void LibraryShortcutPressed(object sender, System.Windows.Input.MouseButtonEventArgs e) { _libraryPress = e.GetPosition((Button)sender); _libraryDragging = false; }
    private void LibraryShortcutMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (e.LeftButton != System.Windows.Input.MouseButtonState.Pressed || _libraryDragging || sender is not Button button || button.DataContext is not ShortcutViewModel view) return;
        var point = e.GetPosition(button);
        if (Math.Abs(point.X - _libraryPress.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(point.Y - _libraryPress.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _libraryDragging = true; _dragging = true; button.ReleaseMouseCapture();
        try { var data = new DataObject(); data.SetData(FolderWidget.LibraryFormat, new LibraryDrag(view.Item.ScreenIndex, view.Item.Id)); DragDrop.DoDragDrop(button, data, DragDropEffects.Move | DragDropEffects.Copy); }
        finally { _dragging = false; _leftAt = DateTime.UtcNow; } e.Handled = true;
    }
    private bool SaveFolders(FolderConfig next) => SaveFolderWorkspace(next, _widgets);
    private void AddFolderPaths(string id, string[] paths)
    {
        var next = FolderRules.Clone(_folders); var folder = next.Items.FirstOrDefault(f => f.Id == id);
        if (folder == null) { folder = new FolderData { Id = id }; next.Items.Add(folder); }
        var errors = new List<string>();
        foreach (string path in paths)
        {
            if (folder.Items.Any(i => i.SourcePath.Equals(path, StringComparison.OrdinalIgnoreCase))) continue;
            try { if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("路径不存在", path); folder.Items.Add(Services.ShellLinkHelper.CreateShortcut(path, 0, folder.Items.Count)); }
            catch (Exception ex) { errors.Add(Path.GetFileName(path) + "：" + ex.Message); }
        }
        SaveFolders(next); if (errors.Count > 0) MessageBox.Show(this, string.Join("\n", errors), "部分入口未添加");
    }
    private void TransferFolder(FolderDrag source, string target, int index, bool copy)
    {
        if(!source.Desktop) { SaveFolders(FolderRules.Place(_folders, source.FolderId, target, source.ItemId, index, copy));return; }
        var next=FolderRules.Clone(_folders);var entry=next.DesktopEntries.FirstOrDefault(d=>d.Item.Id==source.ItemId);if(entry==null)return;
        var staging=new FolderData{Id=Guid.NewGuid().ToString("N"),Items=[entry.Item]};next.Items.Add(staging);
        next=FolderRules.Place(next,staging.Id,target,source.ItemId,index,copy);
        if(!copy&&next.Items.First(f=>f.Id==staging.Id).Items.Count==0)next.DesktopEntries.RemoveAll(d=>d.Item.Id==source.ItemId);
        next.Items.RemoveAll(f=>f.Id==staging.Id);SaveFolders(next);
    }
    private void RemoveFolderEntry(string id, string entry)
    { var next = FolderRules.Clone(_folders); next.Items.FirstOrDefault(f => f.Id == id)?.Items.RemoveAll(i => i.Id == entry); SaveFolders(next); }
    private void ExportFolderEntry(string folder, string entry, int screen)
    {
        var item = ReadFolder(folder).Items.FirstOrDefault(i => i.Id == entry); if (item == null) return;
        if (!Collection(screen).Any(v => v.Item.SourcePath.Equals(item.SourcePath, StringComparison.OrdinalIgnoreCase)))
        {
            var cloned = System.Text.Json.JsonSerializer.Deserialize<ShortcutItem>(System.Text.Json.JsonSerializer.Serialize(item))!; cloned.Id = Guid.NewGuid().ToString("N"); cloned.ScreenIndex = screen;
            var view = new ShortcutViewModel(cloned); Collection(screen).Add(view);
            if (!SaveShortcuts()) { Collection(screen).Remove(view); _viewModel.RebuildLibrary(); return; }
        }
        RemoveFolderEntry(folder, entry);
    }
    internal void TestApplyWidgets(WidgetConfig value) { if (!SaveWidgetConfig(value)) throw new IOException("Widget test save failed."); }
    internal void TestRemoveWidget(string id) => RemoveWidget(id);
    internal int TestCalendarCount => _calendars.Count;
    internal CalendarWidget TestCalendar => _calendars[0];
    internal bool TestCalendarDates(string id,List<DateCounter> dates)=>SaveCalendarDates(id,dates);
    internal bool TestSamplingEnabled => _monitor.IsEnabled || _disks.IsEnabled;
    internal WidgetFrame TestFrame(string id) => _frames[id];
    internal void TestFolderAdd(string id, string[] paths) => AddFolderPaths(id, paths);
    internal int TestFolderCount(string id) => ReadFolder(id).Items.Count;
    internal int TestFolderPreview(string id) => _folderViews[id].PreviewCount;
    internal void TestFolderOpen(string id) => _folderViews[id].TestPreviewClick();
    internal void TestFolderTransfer(string from, string to, int index, bool copy) => TransferFolder(new FolderDrag(from, ReadFolder(from).Items[index].Id), to, 0, copy);
    internal void TestLibraryToFolder(string target, int screen, int index, bool copy) => ImportLibraryEntry(target, new LibraryDrag(screen, Collection(screen)[index].Item.Id), copy);
    internal void TestFolderBusy(string id, bool value) => _folderViews[id].TestBusy(value);
    internal void TestFolderDropGrid(string id,bool value)=>_folderViews[id].TestDropGrid(value);
    internal double TestFolderScroll(string id)=>_folderViews[id].TestScrollToEnd();
    internal DashboardWidget TestDashboard(string id) => _dashboards[id];
    internal MainViewModel TestMonitorModel => _viewModel;
    internal CombinedStatusWidget TestSummary(string id) => ((Grid)_frames[id].Child).Children.OfType<Border>().Select(b=>b.Child).OfType<CombinedStatusWidget>().Single();
    private void ChangeWidgetTheme(string id, string style) { var next = PluginRules.Clone(_widgets); var item = next.Items.FirstOrDefault(i => i.InstanceId == id); if (item == null) return; item.Style = style; SaveWidgetConfig(next); }
}
