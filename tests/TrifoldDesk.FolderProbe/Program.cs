using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using TrifoldDesk;
using TrifoldDesk.Core;
class Program
{
    [STAThread] static void Main()
    {

        string root=Path.Combine(Environment.CurrentDirectory,".test-data","folder-probe","data-"+Guid.NewGuid());Directory.CreateDirectory(root);
        typeof(TrifoldDesk.App).GetProperty("DataDirectory",BindingFlags.Static|BindingFlags.Public)!.SetValue(null,root);
        typeof(TrifoldDesk.App).GetProperty("IsSelfTest",BindingFlags.Static|BindingFlags.Public)!.SetValue(null,true);
        var app=new ProbeApplication();var xml=new System.Xml.XmlDocument();xml.Load("src/TrifoldDesk.App/App.xaml");string resources=xml.DocumentElement!.FirstChild!.InnerXml;app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse("<ResourceDictionary xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\">"+resources+"</ResourceDictionary>");var main=new MainWindow();app.MainWindow=main;main.Show();Pump(()=>main.IsLoaded);
        object? Call(object target,string name,params object[] arguments)=>target.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(target,arguments);
        T Field<T>(string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(main)!;
        void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS: "+name);}
        try
        {
            string sourceA=Path.Combine(root,"a.txt"),sourceB=Path.Combine(root,"b.txt");File.WriteAllText(sourceA,"a");File.WriteAllText(sourceB,"b");
            Call(main,"TestApplyWidgets",new WidgetConfig{Items=[new WidgetInstance{InstanceId="parent",PluginId="folder",PaneIndex=0},new WidgetInstance{InstanceId="child",PluginId="folder",PaneIndex=0}]});
            Call(main,"TestFolderAdd","parent",new[]{sourceA});Call(main,"TestFolderAdd","child",new[]{sourceB});
            Check((bool)Call(main,"TestNest","child","parent")!,"nest creates virtual folder link");
            Check(!Field<WidgetConfig>("_widgets").Items.Any(w=>w.InstanceId=="child"),"nested folder leaves desktop mount");
            var parent=(FolderWidget)Call(main,"TestFolderView","parent")!;int childIndex=Field<FolderConfig>("_folders").Items.First(f=>f.Id=="parent").Items.FindIndex(i=>i.ChildFolderId=="child");
            Call(parent,"TestOpenEntry",childIndex);
            Check((string)parent.GetType().GetProperty("TestCurrentFolderId",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(parent)! =="child","nested folder opens inline");
            Call(parent,"TestBack");Check((string)parent.GetType().GetProperty("TestCurrentFolderId",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(parent)! =="parent","back returns to parent folder");
            Check((bool)Call(main,"TestUndo")!&&Field<WidgetConfig>("_widgets").Items.Any(w=>w.InstanceId=="child"),"undo restores nested folder desktop mount atomically");
            Check((bool)Call(main,"TestRedo")!,"redo replays nested configuration");Call(main,"TestUndo");
            string mapped=Path.Combine(root,"mapped");Directory.CreateDirectory(mapped);File.WriteAllText(Path.Combine(mapped,"original.txt"),"kept");Directory.CreateDirectory(Path.Combine(mapped,"nested"));
            Check((bool)Call(main,"TestMap","parent",mapped)!,"directory map saves configuration");
            parent=(FolderWidget)Call(main,"TestFolderView","parent")!;
            Pump(()=>parent.GetType().GetProperty("TestMappedReadOnly",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(parent) is true);
            var read=(Func<FolderData>)typeof(FolderWidget).GetField("_read",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(parent)!;
            Pump(()=>read().Items.Count==2);
            Call(parent,"TestOpenEntry",0);
            Pump(()=>((System.Windows.Controls.TextBlock)typeof(FolderWidget).GetField("_navigationTitle",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(parent)!).Text.EndsWith("nested"));
            Check(read().Items.Count==0,"mapped directory opens actual child hierarchy inline");Call(parent,"TestBack");Pump(()=>read().Items.Count==2);
            File.WriteAllText(Path.Combine(mapped,"fresh.txt"),"new");Pump(()=>read().Items.Count==3);Check(read().Items.Any(i=>i.Name=="fresh.txt"),"directory watcher refreshes newly created file");
            var mappedItem=read().Items.First(i=>i.Name=="original.txt");Call(main,"DropDesktopEntry",new FolderDrag("parent",mappedItem.Id,false,mappedItem),new Point(40,40),false);
            Check(Field<FolderConfig>("_folders").DesktopEntries.Count==1&&File.ReadAllText(Path.Combine(mapped,"original.txt"))=="kept","mapped drag-out creates reference while preserving original file");
            Check((bool)Call(main,"TestUndo")!&&Field<FolderConfig>("_folders").DesktopEntries.Count==0,"mapped reference creation is undoable without filesystem mutation");
            Check(File.ReadAllText(Path.Combine(mapped,"original.txt"))=="kept","mapping preserves original filesystem contents");
            Check((bool)Call(main,"TestUndo")!&&string.IsNullOrEmpty(Field<FolderConfig>("_folders").Items.First(f=>f.Id=="parent").DirectoryPath),"undo restores unmapped folder without touching files");
            Call(main,"TestDesktopExit","parent",0,new Point(40,40));Call(main,"TestDesktopExit","child",0,new Point(125,40));
            var desktop=Field<FolderConfig>("_folders").DesktopEntries;string first=desktop[0].Item.Id,second=desktop[1].Item.Id;
            int folderCount=Field<FolderConfig>("_folders").Items.Count;
            Call(main,"ShowDesktopStackTarget",first);
            var icons=(System.Collections.Generic.List<System.Windows.Controls.Button>)typeof(MainWindow).GetField("_desktopIcons",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(main)!;
            Check(icons.Any(i=>(string?)i.Tag==first&&i.BorderThickness.Left>0),"stacking hover highlights prospective target");
            Call(main,"ShowDesktopStackTarget",new object?[]{null}!);
            Check(Field<FolderConfig>("_folders").DesktopEntries.Count==2&&Field<FolderConfig>("_folders").Items.Count==folderCount&&!icons.Any(i=>i.BorderThickness.Left>0),"cancelling stacking preview leaves layout unchanged");
            Check((bool)Call(main,"TestStack",first,second)!,"stacking desktop icons creates folder widget");
            Check(Field<FolderConfig>("_folders").DesktopEntries.Count==0&&Field<FolderConfig>("_folders").Items.Any(f=>f.Items.Count==2),"stacking moves only referenced entries into virtual folder");
            Check((bool)Call(main,"TestUndo")!&&Field<FolderConfig>("_folders").DesktopEntries.Count==2,"stack undo restores both desktop icons");
            var canvas=Field<System.Windows.Controls.Canvas>("WidgetCanvas");double paneWidth=canvas.ActualWidth/3;
            Call(main,"TestApplyWidgets",new WidgetConfig{Items=[new(){InstanceId="blocked",PluginId="folder",PaneIndex=0,X=0,Y=0,Width=paneWidth,Height=canvas.ActualHeight,FolderAutoHeight=false},new(){InstanceId="moving",PluginId="folder",PaneIndex=2,X=paneWidth*2,Y=0,Width=180,Height=170,FolderAutoHeight=false}]});
            main.UpdateLayout();var moving=(WidgetFrame)Call(main,"TestFrame","moving")!;double previousX=moving.Model.X;string beforeMove=File.ReadAllText(Path.Combine(root,"widgets.json"));
            typeof(TrifoldDesk.App).GetProperty("IsSelfTest")!.SetValue(null,false);
            try{Call(moving,"TestDrag",paneWidth*.4-previousX,60d);}finally{typeof(TrifoldDesk.App).GetProperty("IsSelfTest")!.SetValue(null,true);}
            Check(moving.Model.PaneIndex==2&&Math.Abs(moving.Model.X-previousX)<.01,"drag into a fully occupied page restores original page and geometry");
            Check(File.ReadAllText(Path.Combine(root,"widgets.json"))==beforeMove,"rejected crowded-page drop does not persist overlapping placement");
        }
        finally{main.Close();app.Shutdown();}
    }
    static void Pump(Func<bool> ready)
    {
        var frame=new DispatcherFrame();var deadline=DateTime.UtcNow.AddSeconds(8);var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};
        timer.Tick+=(_,_)=>{if(ready()||DateTime.UtcNow>deadline){timer.Stop();frame.Continue=false;}};timer.Start();Dispatcher.PushFrame(frame);if(!ready())throw new Exception("UI timeout");
    }
}





class ProbeApplication : TrifoldDesk.App { protected override void OnStartup(StartupEventArgs e) {} }

