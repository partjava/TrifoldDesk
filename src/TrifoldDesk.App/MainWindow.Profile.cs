using TrifoldDesk.Services;
namespace TrifoldDesk;
public partial class MainWindow
{
    private string BuildDiagnosticsReport()
    {
        var entries=_folders.Items.SelectMany(f=>f.Items).Concat(_folders.DesktopEntries.Select(e=>e.Item)).Concat(_viewModel.Apps.Concat(_viewModel.Projects).Select(v=>v.Item)).DistinctBy(i=>i.Id).ToArray();
        var missing=entries.Where(i=>string.IsNullOrEmpty(i.ChildFolderId)&&!i.SourcePath.StartsWith("shell:",StringComparison.OrdinalIgnoreCase)&&!File.Exists(i.SourcePath)&&!Directory.Exists(i.SourcePath)).ToArray();
        return $"TrifoldDesk {typeof(App).Assembly.GetName().Version}\n程序：{Environment.ProcessPath}\n实际配置目录：{_config.DirectoryPath}\n布局组件：{_widgets.Items.Count}\n文件夹：{_folders.Items.Count}\n入口：{entries.Length}\n缺失路径：{missing.Length}\n桌面宿主：{_desktopHost?.Message??"底层窗口模式"}\n显示器布局：{_monitorLayouts.Layouts.Count}（当前 {_activeLayoutDevice}）\n只读配置：{string.Join("、",_config.ReadOnlyFiles)}\n\n读取与恢复提示：\n{(string.IsNullOrWhiteSpace(_loadWarning)?"无读取警告":_loadWarning)}\n\n"+string.Join("\n",missing.Select(i=>i.Name+"："+i.SourcePath))+"\n\n诊断包含本机路径，请在公开分享前检查。";
    }
    private void MigrateLegacyWidgets()
    {
        if(_config.ReadOnlyFiles.Count>0 || !string.IsNullOrWhiteSpace(_loadWarning))return;
        var shortcuts=new ShortcutConfig {Items=_viewModel.Apps.Concat(_viewModel.Projects).Select(v=>v.Item).ToList()};
        var result=LegacyPluginMigration.Apply(_widgets,_folders,shortcuts);if(!result.Changed)return;
        try
        {
            var backup=_config.DirectoryPath+".migration-backup-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".zip";
            ProfilePackage.Export(_config.DirectoryPath,backup);
            var monitors=MonitorLayoutRules.SaveActive(_monitorLayouts,_activeLayoutDevice,result.Widgets);
            ConfigTransaction.Commit(_config.DirectoryPath,new Dictionary<string,object>{{"folders.json",result.Folders},{"widgets.json",result.Widgets},{"monitors.json",monitors}});
            _monitorLayouts=monitors;
            _widgets=PluginRules.Normalize(result.Widgets);_folders=result.Folders;
            _viewModel.Status="旧组件已转换为系统概览和普通文件夹，原入口保留。迁移前备份："+backup;
        }
        catch(Exception ex){App.Log(ex);_loadWarning+="\n旧组件迁移未完成，已保留原数据："+ex.Message;}
    }
    private void ReloadImportedProfile()
    {
        _folderWorkspaceHistory=null;
        var settings=_config.Load<AppSettings>("settings.json");
        foreach(var property in typeof(AppSettings).GetProperties().Where(p=>p.CanWrite))property.SetValue(_settings,property.GetValue(settings.Value));
        _settings.AlwaysOnTop=false;_settings.AutoCollapse=false;_settings.HoverExpand=false;
        if(!App.IsSelfTest)_settings.StartWithWindows=SystemActionService.AutoStartRegistered();
        var shortcuts=_config.Load<ShortcutConfig>("shortcuts.json");_viewModel.Apps.Clear();_viewModel.Projects.Clear();
        foreach(var item in shortcuts.Value.Items.Where(i=>i.ScreenIndex is 0 or 1).OrderBy(i=>i.Order))Collection(item.ScreenIndex).Add(new ShortcutViewModel(item));
        _viewModel.RebuildLibrary();var widgets=_config.Load<WidgetConfig>("widgets.json");var folders=_config.Load<FolderConfig>("folders.json");
        _widgets=PluginRules.Normalize(widgets.Value);_folders=folders.Value;
        _loadWarning=string.Join("\n",new[]{settings.Warning,shortcuts.Warning,widgets.Warning,folders.Warning}.Where(w=>w!=null));
        _paneState=_config.Load<PaneState>("panes.json").Value;if(_paneState.Folded?.Length!=3)_paneState.Folded=new bool[3];
        InitializeMonitorLayouts();MigrateLegacyWidgets();_monitor.SelectedNetworkId=_settings.NetworkInterfaceId;
        Topmost=_handle.Topmost=false;ApplyGlass(false);Dock();ApplyPlugins();UpdatePaneClip();ApplyDesktopHost();
        _viewModel.Status="配置已恢复，旧配置备份保留。";
    }
}


