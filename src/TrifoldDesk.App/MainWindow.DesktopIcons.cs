using System.Windows.Input;
using TrifoldDesk.Services;
namespace TrifoldDesk;
public partial class MainWindow
{
    private readonly List<Button> _desktopIcons = [];
    private void EndFolderEdgeDrags() { foreach(var folder in _folderViews.Values.ToArray())folder.EndEdgeDrag(); }
    private void InitializeDesktopDrop()
    {
        WidgetCanvas.AllowDrop=true;
        WidgetCanvas.Background=Brushes.Transparent;
        WidgetCanvas.DragOver+=(_,e)=>
        {
            if(e.Data.GetData(FolderWidget.DragFormat) is not FolderDrag)return;
            // Existing widget targets handle their own drops before they bubble here.
            if(IsWidgetDrop(e.OriginalSource as DependencyObject))return;
            e.Effects=Keyboard.Modifiers.HasFlag(ModifierKeys.Control)?DragDropEffects.Copy:DragDropEffects.Move;e.Handled=true;
        };
        WidgetCanvas.Drop+=(_,e)=>
        {
            if(e.Data.GetData(FolderWidget.DragFormat) is not FolderDrag drag||IsWidgetDrop(e.OriginalSource as DependencyObject))return;
            DropDesktopEntry(drag,e.GetPosition(WidgetCanvas),Keyboard.Modifiers.HasFlag(ModifierKeys.Control));e.Handled=true;
        };
        WidgetCanvas.SizeChanged+=(_,_)=>RefreshDesktopIcons();
    }
    private static bool IsWidgetDrop(DependencyObject? source)
    {
        while(source!=null)
        {
            if(source is WidgetFrame)return true;
            source=VisualTreeHelper.GetParent(source);
        }
        return false;
    }
    private bool DropDesktopEntry(FolderDrag drag,Point point,bool copy)
    {
        var next=FolderRules.Clone(_folders);
        var folder=next.Items.FirstOrDefault(f=>f.Id==drag.FolderId);
        var existing=next.DesktopEntries.FirstOrDefault(d=>d.Item.Id==drag.ItemId);
        var item=drag.Desktop?existing?.Item:folder?.Items.FirstOrDefault(i=>i.Id==drag.ItemId);
        if(item==null)return false;
        if(copy)item=System.Text.Json.JsonSerializer.Deserialize<ShortcutItem>(System.Text.Json.JsonSerializer.Serialize(item))!;
        if(copy)item.Id=Guid.NewGuid().ToString("N");
        // A folder exit remains on that folder's page even if the pointer crosses a fold seam.
        int pane=drag.Desktop?existing!.PaneIndex:_frames[drag.FolderId].Model.PaneIndex;
        var bounds=WidgetLayout.ClampToPane(point.X-35,point.Y-25,70,70,pane,WidgetCanvas.ActualWidth,WidgetCanvas.ActualHeight,70,70);
        if(drag.Desktop&&!copy) { existing!.X=bounds.X;existing.Y=bounds.Y; }
        else next.DesktopEntries.Add(new DesktopEntry{Item=item,X=bounds.X,Y=bounds.Y,PaneIndex=pane});
        if(!drag.Desktop&&!copy) {folder!.Items.RemoveAll(i=>i.Id==drag.ItemId);folder.Slots.Remove(drag.ItemId);}
        return SaveFolders(next);
    }
    private void RefreshDesktopIcons()
    {
        foreach(var icon in _desktopIcons)WidgetCanvas.Children.Remove(icon);_desktopIcons.Clear();
        if(WidgetCanvas.ActualWidth<=1)return;
        foreach(var entry in _folders.DesktopEntries)
        {
            var bounds=WidgetLayout.ClampToPane(entry.X,entry.Y,70,70,entry.PaneIndex,WidgetCanvas.ActualWidth,WidgetCanvas.ActualHeight,70,70);
            var button=new Button{Width=68,Height=68,Padding=new Thickness(2),Background=Brushes.Transparent,BorderThickness=new Thickness(0),ToolTip=entry.Item.Name+"\n"+entry.Item.SourcePath};
            var content=new StackPanel();content.Children.Add(new Image{Source=ShellLinkHelper.GetIcon(entry.Item),Width=40,Height=40});
            content.Children.Add(new TextBlock{Text=entry.Item.Name,FontSize=11,TextAlignment=TextAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(0,4,0,0)});button.Content=content;
            Point press=default;bool dragging=false;
            button.PreviewMouseLeftButtonDown+=(_,e)=>{press=e.GetPosition(button);dragging=false;button.CaptureMouse();e.Handled=true;};
            button.PreviewMouseMove+=(_,e)=>
            {
                if(e.LeftButton!=MouseButtonState.Pressed||dragging)return;
                var p=e.GetPosition(button);if(Math.Abs(p.X-press.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(p.Y-press.Y)<SystemParameters.MinimumVerticalDragDistance)return;
                dragging=true;button.ReleaseMouseCapture();var data=new DataObject();data.SetData(FolderWidget.DragFormat,new FolderDrag("",entry.Item.Id,true));
                _dragging=true;try{DragDrop.DoDragDrop(button,data,DragDropEffects.Move|DragDropEffects.Copy);}finally{_dragging=false;_leftAt=DateTime.UtcNow;EndFolderEdgeDrags();}e.Handled=true;
            };
            button.PreviewMouseLeftButtonUp+=(_,e)=>{var p=e.GetPosition(button);button.ReleaseMouseCapture();e.Handled=true;if(!dragging&&p.X>=0&&p.Y>=0&&p.X<button.ActualWidth&&p.Y<button.ActualHeight){try{ShellLinkHelper.Launch(entry.Item);}catch(Exception ex){Fail("入口无法打开",ex);}}};
            var menu=new ContextMenu();var remove=new MenuItem{Header="移除图标（保留源文件）"};menu.Items.Add(remove);button.ContextMenu=menu;
            remove.Click+=(_,_)=>{var next=FolderRules.Clone(_folders);next.DesktopEntries.RemoveAll(d=>d.Item.Id==entry.Item.Id);SaveFolders(next);};
            Canvas.SetLeft(button,bounds.X);Canvas.SetTop(button,bounds.Y);Panel.SetZIndex(button,1000);WidgetCanvas.Children.Add(button);_desktopIcons.Add(button);
        }
    }
    internal bool TestDesktopExit(string folder,int index,Point point)=>DropDesktopEntry(new FolderDrag(folder,ReadFolder(folder).Items[index].Id),point,false);
    internal int TestDesktopCount=>_folders.DesktopEntries.Count;
    internal void TestEdgeHover(string folder)=>_folderViews[folder].TestEdgeHover();
    internal void TestEdgeCancel()=>EndFolderEdgeDrags();
    internal void TestDesktopReturn(string folder)=>TransferFolder(new FolderDrag("",_folders.DesktopEntries.Last().Item.Id,true),folder,1,false);
}
