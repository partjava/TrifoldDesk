using TrifoldDesk.Services;
namespace TrifoldDesk;
public partial class MainWindow
{
 private MonitorLayoutConfig _monitorLayouts=new();private string _activeLayoutDevice="";
 private void InitializeMonitorLayouts()
 {
  var loaded=_config.Load<MonitorLayoutConfig>("monitors.json");_monitorLayouts=loaded.Value;
  if(loaded.Warning!=null)_loadWarning+="\n"+loaded.Warning;
  _activeLayoutDevice=WindowDockService.SelectedScreen(_settings.MonitorDevice).DeviceName;
  _widgets=PluginRules.Normalize(MonitorLayoutRules.LoadActive(_monitorLayouts,_activeLayoutDevice,_widgets));
 }
 private void PersistWidgetLayout(WidgetConfig widgets)
 {
  var next=MonitorLayoutRules.SaveActive(_monitorLayouts,_activeLayoutDevice,widgets);
  ConfigTransaction.Commit(_config.DirectoryPath,new(){["widgets.json"]=widgets,["monitors.json"]=next});_monitorLayouts=next;
 }
 private void SwitchMonitorLayout(string device)
 {
  if(string.IsNullOrEmpty(_activeLayoutDevice)||device==_activeLayoutDevice)return;
  var saved=MonitorLayoutRules.SaveActive(_monitorLayouts,_activeLayoutDevice,_widgets);
  var next=PluginRules.Normalize(MonitorLayoutRules.LoadActive(saved,device,PluginRules.Defaults(false)));
  saved=MonitorLayoutRules.SaveActive(saved,device,next);
  ConfigTransaction.Commit(_config.DirectoryPath,new(){["widgets.json"]=next,["monitors.json"]=saved});
  _monitorLayouts=saved;_activeLayoutDevice=device;_widgets=next;MigrateLegacyWidgets();ApplyPlugins();
 }
}
