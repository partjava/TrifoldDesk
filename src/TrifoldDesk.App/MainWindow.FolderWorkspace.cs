using System.Text.Json;
using TrifoldDesk.Services;
namespace TrifoldDesk;

public sealed class FolderWorkspaceSnapshot
{
    public FolderConfig Folders { get; set; } = new();
    public WidgetConfig Widgets { get; set; } = new();
}
public partial class MainWindow
{
    private sealed class FolderViewState(string rootId, FolderWidget view)
    {
        public string RootId = rootId, CurrentId = rootId, MappedPath = "";
        public FolderWidget View = view;
        public FolderData Mapped = new();
        public Stack<(string Id,string Path)> Parents = new();
        public FileSystemWatcher? Watcher;
        public CancellationTokenSource? Scan;
        public int Generation;
        public bool Active = true;
        public bool Retired;
        public int RefreshPending;
    }
    private readonly Dictionary<string,FolderViewState> _folderWorkspaceViews = [];
    private void ResetFolderWorkspaceViews()
    {
        foreach(var state in _folderWorkspaceViews.Values)
        { state.Active=false;state.Retired=true;state.Scan?.Cancel();state.Watcher?.Dispose();state.Watcher=null; }
        _folderWorkspaceViews.Clear();
    }
    private ConfigurationHistory<FolderWorkspaceSnapshot>? _folderWorkspaceHistory;
    private FolderWorkspaceSnapshot WorkspaceSnapshot() => new() { Folders=FolderRules.Clone(_folders), Widgets=PluginRules.Clone(_widgets) };
    private void EnsureWorkspaceHistory()
    {
        var current=WorkspaceSnapshot();
        if(_folderWorkspaceHistory==null)_folderWorkspaceHistory=new(current,20);
        // Auto-height and monitor bounds can change widget geometry after a scan.
        // Those visual adjustments must not erase the user's configuration undo.
        else if(JsonSerializer.Serialize(_folderWorkspaceHistory.Current.Folders)!=JsonSerializer.Serialize(current.Folders))_folderWorkspaceHistory.Reset(current);
    }
    private bool SaveFolderWorkspace(FolderConfig folders, WidgetConfig widgets, bool recordHistory=true)
    {
        if(recordHistory)EnsureWorkspaceHistory();
        bool rebuild=JsonSerializer.Serialize(widgets)!=JsonSerializer.Serialize(_widgets);
        try
        {
            var layouts=MonitorLayoutRules.SaveActive(_monitorLayouts,_activeLayoutDevice,widgets);
            ConfigTransaction.Commit(App.DataDirectory,new(){["folders.json"]=folders,["widgets.json"]=widgets,["monitors.json"]=layouts});_monitorLayouts=layouts;
            _folders=FolderRules.Clone(folders);_widgets=PluginRules.Clone(widgets);
            if(rebuild)ApplyPlugins();else { foreach(var state in _folderWorkspaceViews.Values.Where(s=>s.Active).ToArray())RefreshFolderWorkspaceView(state);RefreshDesktopIcons(); }
            if(recordHistory)_folderWorkspaceHistory!.Commit(WorkspaceSnapshot());
            return true;
        }
        catch(Exception ex){Fail("文件夹配置未保存，桌面保持原状",ex);return false;}
    }
    private bool UndoFolderWorkspace(bool redo=false)
    {
        EnsureWorkspaceHistory();var history=_folderWorkspaceHistory!;
        bool available=redo?history.TryRedo(out var state):history.TryUndo(out state);
        if(!available)return false;
        // Folder history restores membership; unrelated widgets and later visual edits stay current.
        var widgets=PluginRules.Clone(_widgets);
        widgets.Items.RemoveAll(w=>w.PluginId=="folder"&&!state.Widgets.Items.Any(s=>s.PluginId=="folder"&&s.InstanceId==w.InstanceId));
        foreach(var folder in state.Widgets.Items.Where(w=>w.PluginId=="folder"))
            if(!widgets.Items.Any(w=>w.InstanceId==folder.InstanceId))widgets.Items.Add(folder);
        if(SaveFolderWorkspace(state.Folders,widgets,false))return true;
        if(redo)history.TryUndo(out _);else history.TryRedo(out _);return false;
    }
    private void WireFolderWorkspace(WidgetInstance item, FolderWidget folder)
    {
        if(_folderWorkspaceViews.Remove(item.InstanceId,out var stale)){stale.Active=false;stale.Retired=true;stale.Scan?.Cancel();stale.Watcher?.Dispose();}
        var state=new FolderViewState(item.InstanceId,folder);_folderWorkspaceViews[item.InstanceId]=state;
        folder.ConfigureWorkspace(item.InstanceId,()=>string.IsNullOrEmpty(state.MappedPath)?ReadFolder(state.CurrentId):state.Mapped,
            paths=>{if(string.IsNullOrEmpty(state.MappedPath))AddFolderPaths(state.CurrentId,paths);},
            (source,slot,copy)=>TransferWorkspaceEntry(state,source,slot,copy),
            id=>RemoveFolderEntry(state.CurrentId,id),
            (id,screen)=>ExportFolderEntry(state.CurrentId,id,screen),
            (source,copy)=>{if(string.IsNullOrEmpty(state.MappedPath))ImportLibraryEntry(state.CurrentId,source,copy);});
        folder.FolderOpenRequested+=entry=>OpenWorkspaceFolder(state,entry);
        folder.BackRequested+=()=>{if(state.Parents.TryPop(out var parent)){state.CurrentId=parent.Id;state.MappedPath=parent.Path;RefreshFolderWorkspaceView(state);}};
        folder.LaunchSettingsRequested+=entry=>EditFolderLaunch(state.CurrentId,entry.Id);
        var menu=folder.ContextMenu!;
        var map=new MenuItem{Header="映射真实目录…"};menu.Items.Add(map);
        map.Click+=(_,_)=>
        {
            var picker=new Microsoft.Win32.OpenFolderDialog{Title="选择要浏览的目录",Multiselect=false};
            _dialogOpen=true;try{if(picker.ShowDialog(this)==true)MapWorkspaceFolder(state.CurrentId,picker.FolderName);}finally{_dialogOpen=false;_leftAt=DateTime.UtcNow;}
        };
        var unmap=new MenuItem{Header="取消目录映射"};menu.Items.Add(unmap);unmap.Click+=(_,_)=>MapWorkspaceFolder(state.CurrentId,"");
        var nest=new MenuItem{Header="放入文件夹"};menu.Items.Add(nest);
        var detach=new MenuItem{Header="移回桌面"};menu.Items.Add(detach);detach.Click+=(_,_)=>DetachWorkspaceFolder(state);
        var refresh=new MenuItem{Header="刷新目录"};menu.Items.Add(refresh);refresh.Click+=(_,_)=>RefreshFolderWorkspaceView(state);
        var undo=new MenuItem{Header="撤销"};menu.Items.Add(undo);undo.Click+=(_,_)=>UndoFolderWorkspace();
        var redo=new MenuItem{Header="重做"};menu.Items.Add(redo);redo.Click+=(_,_)=>UndoFolderWorkspace(true);
        menu.Opened+=(_,_)=>
        {
            bool mapped=!string.IsNullOrEmpty(ReadFolder(state.CurrentId).DirectoryPath);
            unmap.Visibility=refresh.Visibility=mapped?Visibility.Visible:Visibility.Collapsed;
            nest.Items.Clear();
            foreach(var candidate in WorkspaceFolderConfig().Items.Where(f=>FolderWorkspaceRules.CanNest(WorkspaceFolderConfig(),state.CurrentId,f.Id)))
            {
                var target=new MenuItem{Header=candidate.Name};string id=candidate.Id;target.Click+=(_,_)=>NestWorkspaceFolder(state.CurrentId,id);nest.Items.Add(target);
            }
            nest.IsEnabled=nest.Items.Count>0;detach.Visibility=string.IsNullOrEmpty(ReadFolder(state.CurrentId).ParentFolderId)?Visibility.Collapsed:Visibility.Visible;
            undo.IsEnabled=(_folderWorkspaceHistory?.UndoCount??0)>0;redo.IsEnabled=(_folderWorkspaceHistory?.RedoCount??0)>0;
        };
        folder.Loaded+=(_,_)=>{if(state.Retired)return;state.Active=true;RefreshFolderWorkspaceView(state);};
        folder.Unloaded+=(_,_)=>{state.Active=false;state.Scan?.Cancel();state.Watcher?.Dispose();state.Watcher=null;};
        RefreshFolderWorkspaceView(state);
    }
    private void OpenWorkspaceFolder(FolderViewState state, ShortcutItem entry)
    {
        if(!string.IsNullOrEmpty(entry.ChildFolderId))
        {
            if(!_folders.Items.Any(f=>f.Id==entry.ChildFolderId))return;
            state.Parents.Push((state.CurrentId,state.MappedPath));state.CurrentId=entry.ChildFolderId;state.MappedPath=ReadFolder(entry.ChildFolderId).DirectoryPath;
        }
        else if(!string.IsNullOrEmpty(state.MappedPath)&&Directory.Exists(entry.SourcePath))
        {
            if(File.GetAttributes(entry.SourcePath).HasFlag(FileAttributes.ReparsePoint)){ShellLinkHelper.Launch(entry);return;}
            state.Parents.Push((state.CurrentId,state.MappedPath));state.MappedPath=entry.SourcePath;
        }
        RefreshFolderWorkspaceView(state);
    }
    private void RefreshFolderWorkspaceView(FolderViewState state)
    {
        state.Scan?.Cancel();state.Watcher?.Dispose();state.Watcher=null;
        var data=ReadFolder(state.CurrentId);
        if(state.Parents.Count==0)state.MappedPath=data.DirectoryPath;
        state.View.ConfigureWorkspace(state.CurrentId,()=>string.IsNullOrEmpty(state.MappedPath)?ReadFolder(state.CurrentId):state.Mapped,
            paths=>{if(string.IsNullOrEmpty(state.MappedPath))AddFolderPaths(state.CurrentId,paths);},
            (source,slot,copy)=>TransferWorkspaceEntry(state,source,slot,copy),id=>RemoveFolderEntry(state.CurrentId,id),
            (id,screen)=>ExportFolderEntry(state.CurrentId,id,screen),(source,copy)=>{if(string.IsNullOrEmpty(state.MappedPath))ImportLibraryEntry(state.CurrentId,source,copy);});
        state.View.SetNavigation(string.IsNullOrEmpty(state.MappedPath)?data.Name:state.MappedPath,state.Parents.Count>0,!string.IsNullOrEmpty(state.MappedPath));
        if(string.IsNullOrEmpty(state.MappedPath)){state.View.Refresh();return;}
        string path=state.MappedPath;int generation=++state.Generation;
        state.Mapped=new FolderData{Id=state.CurrentId,Name=data.Name,DirectoryPath=path};state.View.Refresh();
        var token=state.Scan=new CancellationTokenSource();
        _=ScanMappedFolder(state,path,generation,token.Token);
        try
        {
            if(!Directory.Exists(path))return;
            state.Watcher=new FileSystemWatcher(path){IncludeSubdirectories=false,NotifyFilter=NotifyFilters.FileName|NotifyFilters.DirectoryName|NotifyFilters.LastWrite};
            FileSystemEventHandler changed=(_,_)=>QueueMappedRefresh(state);RenamedEventHandler renamed=(_,_)=>QueueMappedRefresh(state);
            state.Watcher.Created+=changed;state.Watcher.Deleted+=changed;state.Watcher.Changed+=changed;state.Watcher.Renamed+=renamed;
            state.Watcher.EnableRaisingEvents=true;
        }
        catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){App.Log(ex);}
    }
    private async Task ScanMappedFolder(FolderViewState state,string path,int generation,CancellationToken token)
    {
        try
        {
            var snapshot=await Task.Run(()=>MappedDirectoryScanner.Scan(path,512,token),token);
            if(token.IsCancellationRequested||generation!=state.Generation||!state.Active)return;
            await Dispatcher.InvokeAsync(()=>
            {
                if(token.IsCancellationRequested||generation!=state.Generation||!state.Active)return;
                state.Mapped.Items=snapshot.Entries.Select((entry,index)=>MappedDirectoryScanner.CreateShortcut(entry,0,index)).ToList();
                state.View.ToolTip=snapshot.Error??(snapshot.IsTruncated?"目录内容较多，仅显示前512项。原始文件保持原位置。":"真实目录 · 拖出创建入口 · 原始文件保持原位置");
                state.View.Refresh();
            });
        }
        catch(OperationCanceledException){}
        catch(Exception ex){App.Log(ex);}
    }
    private void QueueMappedRefresh(FolderViewState state)
    {
        if(Interlocked.Exchange(ref state.RefreshPending,1)!=0)return;
        _=Task.Delay(200).ContinueWith(_=>Dispatcher.BeginInvoke(()=>{Interlocked.Exchange(ref state.RefreshPending,0);if(state.Active)RefreshFolderWorkspaceView(state);}));
    }
    private void TransferWorkspaceEntry(FolderViewState state,FolderDrag source,int slot,bool copy)
    {
        if(!string.IsNullOrEmpty(state.MappedPath))return;
        if(source.MappedItem!=null)
        {
            var next=FolderRules.Clone(_folders);var target=next.Items.FirstOrDefault(f=>f.Id==state.CurrentId);
            if(target==null){target=new FolderData{Id=state.CurrentId};next.Items.Add(target);}
            if(target.Items.Any(i=>i.SourcePath.Equals(source.MappedItem.SourcePath,StringComparison.OrdinalIgnoreCase)))return;
            var entry=JsonSerializer.Deserialize<ShortcutItem>(JsonSerializer.Serialize(source.MappedItem))!;entry.Id=Guid.NewGuid().ToString("N");target.Items.Add(entry);
            target.Slots=FolderRules.Positions(target);target.Slots[entry.Id]=Math.Clamp(slot,0,4095);SaveFolderWorkspace(next,_widgets);return;
        }
        var child=source.Desktop?_folders.DesktopEntries.FirstOrDefault(d=>d.Item.Id==source.ItemId)?.Item:_folders.Items.FirstOrDefault(f=>f.Id==source.FolderId)?.Items.FirstOrDefault(i=>i.Id==source.ItemId);
        if(!string.IsNullOrEmpty(child?.ChildFolderId)){NestWorkspaceFolder(child.ChildFolderId,state.CurrentId,slot);return;}
        TransferFolder(source,state.CurrentId,slot,copy);
    }
    private bool MapWorkspaceFolder(string id,string path)
    {
        if(!string.IsNullOrEmpty(path)&&!Directory.Exists(path))return false;
        var next=FolderRules.Clone(_folders);var folder=next.Items.FirstOrDefault(f=>f.Id==id);
        if(folder==null){folder=new FolderData{Id=id};next.Items.Add(folder);}
        folder.DirectoryPath=string.IsNullOrEmpty(path)?"":Path.GetFullPath(path);
        if(!SaveFolderWorkspace(next,_widgets))return false;
        foreach(var state in _folderWorkspaceViews.Values.Where(s=>s.CurrentId==id).ToArray()){state.MappedPath=folder.DirectoryPath;state.Parents.Clear();RefreshFolderWorkspaceView(state);}
        return true;
    }
    private FolderConfig WorkspaceFolderConfig()
    {
        var folders=FolderRules.Clone(_folders);
        foreach(var widget in _widgets.Items.Where(w=>w.PluginId=="folder"))
            if(!folders.Items.Any(f=>f.Id==widget.InstanceId))folders.Items.Add(new FolderData{Id=widget.InstanceId,Name=string.IsNullOrWhiteSpace(widget.Title)?"文件夹":widget.Title});
        return folders;
    }
    private bool NestWorkspaceFolder(string child,string parent,int slot=0)
    {
        var result=FolderWorkspaceRules.Nest(WorkspaceFolderConfig(),child,parent,slot);if(!result.Changed)return false;
        var widgets=PluginRules.Clone(_widgets);widgets.Items.RemoveAll(w=>w.PluginId=="folder"&&w.InstanceId==child);
        return SaveFolderWorkspace(result.Config,widgets);
    }
    private bool DetachWorkspaceFolder(FolderViewState state)
    {
        var result=FolderWorkspaceRules.Detach(_folders,state.CurrentId);if(!result.Changed)return false;
        var widgets=PluginRules.Clone(_widgets);
        if(!widgets.Items.Any(w=>w.InstanceId==state.CurrentId))
        {
            var root=_frames.TryGetValue(state.RootId,out var frame)?frame.Model:null;
            widgets.Items.Add(new WidgetInstance { InstanceId=state.CurrentId,PluginId="folder",Title=ReadFolder(state.CurrentId).Name,PaneIndex=root?.PaneIndex??0,X=(root?.X??0)+24,Y=(root?.Y??0)+24,Width=220,Height=170 });
        }
        return SaveFolderWorkspace(result.Config,widgets);
    }
    private bool StackWorkspaceIcons(string target,string dragged)
    {
        var targetEntry=_folders.DesktopEntries.FirstOrDefault(d=>d.Item.Id==target);if(targetEntry==null)return false;
        string folderId=Guid.NewGuid().ToString("N");var result=FolderWorkspaceRules.StackDesktopIcons(_folders,target,dragged,folderId,"新建文件夹");
        if(!result.Changed)return false;
        result.Config.DesktopEntries.RemoveAll(d=>d.Item.ChildFolderId==folderId);
        var widgets=PluginRules.Clone(_widgets);widgets.Items.Add(new WidgetInstance{InstanceId=folderId,PluginId="folder",Title="新建文件夹",PaneIndex=targetEntry.PaneIndex,X=targetEntry.X,Y=targetEntry.Y,Width=220,Height=170,FolderAutoHeight=true});
        return SaveFolderWorkspace(result.Config,widgets);
    }
    private int WorkspaceSourcePane(string folderId) => _frames.TryGetValue(folderId,out var frame)?frame.Model.PaneIndex:
        _folderWorkspaceViews.Values.Where(v=>v.CurrentId==folderId).Select(v=>_frames.TryGetValue(v.RootId,out var root)?root.Model.PaneIndex:0).FirstOrDefault();
    internal bool TestStack(string target,string dragged)=>StackWorkspaceIcons(target,dragged);
    internal bool TestNest(string child,string parent)=>NestWorkspaceFolder(child,parent);
    internal bool TestMap(string id,string path)=>MapWorkspaceFolder(id,path);
    internal bool TestUndo()=>UndoFolderWorkspace();
    internal bool TestRedo()=>UndoFolderWorkspace(true);
    internal int TestUndoCount=>_folderWorkspaceHistory?.UndoCount??0;
    internal FolderWidget TestFolderView(string id)=>_folderViews[id];
}

