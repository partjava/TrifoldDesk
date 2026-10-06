using Microsoft.Win32;
using System.Diagnostics;

namespace TrifoldDesk;
public sealed class ProfileToolsWindow : Window
{
    private readonly string _directory;
    private readonly TextBlock _status = new() { TextWrapping=TextWrapping.Wrap, Foreground=new SolidColorBrush(Color.FromRgb(185,204,212)), Margin=new Thickness(0,14,0,0) };
    public bool Imported { get; private set; }
    private readonly Action? _beforeReplace;
    private readonly Action<bool>? _afterReplace;
    public ProfileToolsWindow(string directory, bool? import = null, Action? beforeReplace=null, Action<bool>? afterReplace=null)
    {
        _beforeReplace=beforeReplace;_afterReplace=afterReplace;
        _directory=directory; Title="配置备份与恢复"; Width=540; Height=360; MinWidth=440; WindowStartupLocation=WindowStartupLocation.CenterOwner;
        Background=new SolidColorBrush(Color.FromRgb(22,36,48));
        var body=new StackPanel {Margin=new Thickness(24)};Content=body;
        body.Children.Add(new TextBlock {Text="带上你的桌面",FontSize=22,FontWeight=FontWeights.SemiBold});
        body.Children.Add(new TextBlock {Text="备份布局、入口与图标资源。原始文档留在原位置。",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,10,0,18)});
        var actions=new StackPanel {Orientation=Orientation.Horizontal};body.Children.Add(actions);
        var export=new Button{Content="导出配置",Margin=new Thickness(0,0,12,0)};actions.Children.Add(export);export.Click+=(_,_)=>Export();
        var importButton=new Button{Content="导入配置"};actions.Children.Add(importButton);importButton.Click+=(_,_)=>Import();
        body.Children.Add(_status);
        if(import.HasValue)Loaded+=(_,_)=>{if(import.Value)Import();else Export();};
    }
    private void Export()
    {
        var picker=new SaveFileDialog{Filter="TrifoldDesk 配置包 (*.zip)|*.zip",FileName="TrifoldDesk-桌面备份-"+DateTime.Now.ToString("yyyyMMdd")+".zip"};
        if(picker.ShowDialog(this)!=true)return;
        try { ProfilePackage.Export(_directory,picker.FileName);_status.Text="已导出："+picker.FileName; }
        catch(Exception ex){App.Log(ex);_status.Text="导出失败："+ex.Message;}
    }
    private void Import()
    {
        var picker=new OpenFileDialog{Filter="TrifoldDesk 配置包 (*.zip)|*.zip"};if(picker.ShowDialog(this)!=true)return;
        try
        {
            var preview=ProfilePackage.Preview(picker.FileName);
            var prompt=$"配置包包含 {preview.Files.Count} 个配置或资源文件。\n导入将替换当前配置，并完整保留当前配置备份。";
            if(preview.MissingExternalPaths.Count>0)prompt+=$"\n有 {preview.MissingExternalPaths.Count} 个原始路径在本机不存在：\n"+string.Join("\n",preview.MissingExternalPaths.Take(5));
            if(preview.Warnings.Count>0)prompt+="\n"+string.Join("\n",preview.Warnings);
            if(MessageBox.Show(this,prompt,"恢复桌面配置",MessageBoxButton.OKCancel,MessageBoxImage.Information,MessageBoxResult.Cancel)!=MessageBoxResult.OK)return;
            ProfilePackageReport result;
            _beforeReplace?.Invoke();
            try{result=ProfilePackage.Import(picker.FileName,_directory);}
            catch{_afterReplace?.Invoke(false);throw;}
            Imported=true;_afterReplace?.Invoke(true);
            MessageBox.Show(this,"配置已导入。旧配置备份：\n"+(result.BackupDirectory??"原配置为空"),"恢复完成");DialogResult=true;
        }
        catch(Exception ex){App.Log(ex);_status.Text=(Imported?"配置已导入，但重新加载失败，请重启软件：":"导入未完成，原配置保留：")+ex.Message;}
    }
}
public sealed class StartupDiagnosticsWindow : Window
{
    public StartupDiagnosticsWindow(string report,string directory)
    {
        Title="启动诊断";Width=650;Height=500;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=new SolidColorBrush(Color.FromRgb(22,36,48));
        var body=new DockPanel{Margin=new Thickness(20)};Content=body;
        var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,14,0,0)};DockPanel.SetDock(actions,Dock.Bottom);body.Children.Add(actions);
        var copy=new Button{Content="复制诊断",Margin=new Thickness(0,0,10,0)};copy.Click+=(_,_)=>Clipboard.SetText(report);actions.Children.Add(copy);
        var open=new Button{Content="打开配置目录"};open.Click+=(_,_)=>{if(Directory.Exists(directory))Process.Start(new ProcessStartInfo(directory){UseShellExecute=true});};actions.Children.Add(open);
        body.Children.Add(new TextBox{Text=report,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=Brushes.Transparent,Foreground=Brushes.White,BorderThickness=new Thickness(0)});
    }
}


