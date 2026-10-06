namespace TrifoldDesk;
public interface IProfileReplacementParticipant
{
    void PrepareForProfileReplacement();
}
public partial class MainWindow
{
    internal void PrepareProfileReplacement()
    {
        var participants=Descendants(WidgetCanvas).OfType<IProfileReplacementParticipant>().ToArray();
        // Validate pending edits before retiring anything. A failed flush keeps the live text visible.
        foreach(var notes in participants.OfType<ProductivityWidget>())notes.FlushBeforeProfileReplacement();
        _reminders.SetPaused(true);
        try
        {
            foreach(var participant in participants)participant.PrepareForProfileReplacement();
            ResetFolderWorkspaceViews();
        }
        catch{try{ApplyPlugins();}finally{_reminders.SetPaused(false);}throw;}
    }
    internal void FinishProfileReplacement(bool imported)
    {
        try{if(imported)ReloadImportedProfile();else ApplyPlugins();}
        finally{_reminders.SetPaused(false);}
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for(int index=0;index<VisualTreeHelper.GetChildrenCount(root);index++)
            foreach(var child in Descendants(VisualTreeHelper.GetChild(root,index)))yield return child;
    }
}
