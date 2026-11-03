using Microsoft.Win32;
namespace TrifoldDesk;
public sealed class ScriptLaunchDialog : Window
{
    private readonly ShortcutItem _item;private readonly ComboBox _mode=new();private readonly TextBox _executable=new(),_arguments=new(),_directory=new();private readonly CheckBox _runAsAdmin=new(){Content="默认以管理员身份运行",Margin=new Thickness(0,12,0,0),Foreground=Brushes.WhiteSmoke};
    public ShortcutItem Result {get;private set;}
    public ScriptLaunchDialog(ShortcutItem item)
    {
        _item=item;Result=System.Text.Json.JsonSerializer.Deserialize<ShortcutItem>(System.Text.Json.JsonSerializer.Serialize(item))!;
        Title="启动方式 · "+item.Name;Width=520;Height=440;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(22,36,48));
        var root=new StackPanel{Margin=new Thickness(24)};Content=root;
        foreach(var box in new[]{_executable,_arguments,_directory}){box.Padding=new Thickness(8,6,8,6);box.Foreground=new SolidColorBrush(Color.FromRgb(24,34,44));box.Background=Brushes.White;}
        Add("启动方式",_mode);_mode.ItemsSource=new[]{"系统默认","Python","PowerShell","指定程序"};_mode.SelectedIndex=Array.IndexOf(new[]{"shell","python","powershell","program"},item.LaunchMode);if(_mode.SelectedIndex<0)_mode.SelectedIndex=0;
        Add("解释器 / 程序",_executable);_executable.Text=item.ExecutablePath;
        var browse=new Button{Content="选择程序…",HorizontalAlignment=HorizontalAlignment.Right};root.Children.Add(browse);browse.Click+=(_,_)=>{var picker=new OpenFileDialog{Filter="程序 (*.exe)|*.exe|全部文件|*.*"};if(picker.ShowDialog(this)==true)_executable.Text=picker.FileName;};
        Add("参数",_arguments);_arguments.Text=item.Arguments;Add("工作目录",_directory);_directory.Text=item.WorkingDirectory;_runAsAdmin.IsChecked=item.RunAsAdmin;root.Children.Add(_runAsAdmin);
        var save=new Button{Content="保存",IsDefault=true,Margin=new Thickness(0,16,0,0)};root.Children.Add(save);save.Click+=(_,_)=>Save();
        void Add(string label,Control control){root.Children.Add(new TextBlock{Text=label,Margin=new Thickness(0,8,0,4)});root.Children.Add(control);}
    }
    private void Save()
    {
        var next=System.Text.Json.JsonSerializer.Deserialize<ShortcutItem>(System.Text.Json.JsonSerializer.Serialize(_item))!;
        next.LaunchMode=new[]{"shell","python","powershell","program"}[_mode.SelectedIndex];next.ExecutablePath=_executable.Text.Trim();next.Arguments=_arguments.Text;next.WorkingDirectory=_directory.Text.Trim();next.RunAsAdmin=_runAsAdmin.IsChecked==true;
        try{ScriptLaunchRules.Create(next);Result=next;DialogResult=true;}
        catch(Exception ex){MessageBox.Show(this,ex.Message,"启动配置无法保存");}
    }
}
public partial class MainWindow
{
    private void MenuLaunchSettings(object sender,RoutedEventArgs e)
    {
        var view=ContextItem(sender);
        ScriptLaunchDialog dialog;
        _dialogOpen=true;
        try{dialog=new ScriptLaunchDialog(view.Item){Owner=this};if(dialog.ShowDialog()!=true)return;}
        finally{_dialogOpen=false;_leftAt=DateTime.UtcNow;}
        var old=view.Item;var next=dialog.Result;var previous=(old.LaunchMode,old.ExecutablePath,old.Arguments,old.WorkingDirectory,old.RunAsAdmin);
        old.LaunchMode=next.LaunchMode;old.ExecutablePath=next.ExecutablePath;old.Arguments=next.Arguments;old.WorkingDirectory=next.WorkingDirectory;old.RunAsAdmin=next.RunAsAdmin;
        if(!SaveShortcuts())(old.LaunchMode,old.ExecutablePath,old.Arguments,old.WorkingDirectory,old.RunAsAdmin)=previous;
        view.Refresh();
    }
    private void EditFolderLaunch(string folderId,string itemId)
    {
        var item=ReadFolder(folderId).Items.FirstOrDefault(i=>i.Id==itemId);if(item==null)return;
        ScriptLaunchDialog dialog;
        _dialogOpen=true;
        try{dialog=new ScriptLaunchDialog(item){Owner=this};if(dialog.ShowDialog()!=true)return;}
        finally{_dialogOpen=false;_leftAt=DateTime.UtcNow;}
        var next=FolderRules.Clone(_folders);var folder=next.Items.First(f=>f.Id==folderId);int index=folder.Items.FindIndex(i=>i.Id==itemId);folder.Items[index]=dialog.Result;SaveFolders(next);
    }
    private void EditDesktopLaunch(DesktopEntry entry)
    {
        ScriptLaunchDialog dialog;
        _dialogOpen=true;
        try{dialog=new ScriptLaunchDialog(entry.Item){Owner=this};if(dialog.ShowDialog()!=true)return;}
        finally{_dialogOpen=false;_leftAt=DateTime.UtcNow;}
        var next=FolderRules.Clone(_folders);var target=next.DesktopEntries.FirstOrDefault(d=>d.Item.Id==entry.Item.Id);if(target==null)return;
        target.Item=dialog.Result;SaveFolders(next);
    }
}
