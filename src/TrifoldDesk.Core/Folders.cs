using System.Text.Json;
namespace TrifoldDesk.Core;
public sealed class FolderData
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "应用文件夹";
    public string DirectoryPath { get; set; } = "";
    public string ParentFolderId { get; set; } = "";
    public List<ShortcutItem> Items { get; set; } = [];
    public Dictionary<string,int> Slots { get; set; } = [];
}
public sealed class FolderConfig
{
    public int SchemaVersion { get; set; } = 2;
    public List<FolderData> Items { get; set; } = [];
    public List<DesktopEntry> DesktopEntries { get; set; } = [];
}
public sealed class DesktopEntry
{
    public ShortcutItem Item { get; set; } = new();
    public double X { get; set; }
    public double Y { get; set; }
    public int PaneIndex { get; set; }
}
public static class FolderRules
{
    public static Dictionary<string,int> Positions(FolderData folder)
    {
        var result=new Dictionary<string,int>(); var used=new HashSet<int>();
        foreach(var item in folder.Items)
            if(folder.Slots != null && folder.Slots.TryGetValue(item.Id,out int slot) && slot is >=0 and <4096 && used.Add(slot)) result[item.Id]=slot;
        foreach(var item in folder.Items)
            if(!result.ContainsKey(item.Id)) { int slot=0; while(slot<4095 && used.Contains(slot))slot++; used.Add(slot); result[item.Id]=slot; }
        return result;
    }
    public static FolderConfig Place(FolderConfig current,string from,string to,string itemId,int slot,bool copy)
    {
        var next=Clone(current); var source=next.Items.FirstOrDefault(f=>f.Id==from);
        var item=source?.Items.FirstOrDefault(i=>i.Id==itemId); if(source==null||item==null)return next;
        if(!string.IsNullOrWhiteSpace(item.ChildFolderId)) copy=false;
        var target=next.Items.FirstOrDefault(f=>f.Id==to); if(target==null){target=new FolderData{Id=to};next.Items.Add(target);}
        if(from!=to&&IsDuplicate(target,item))return next;
        source.Slots=Positions(source); target.Slots=Positions(target); slot=Math.Clamp(slot,0,4095);
        int oldSlot=source.Slots[itemId];
        var occupied=target.Items.FirstOrDefault(i=>i.Id!=itemId&&target.Slots[i.Id]==slot);
        if(occupied!=null)
        {
            int replacement=from==to?oldSlot:0;
            if(from!=to)while(replacement<4095&&(target.Slots.Values.Contains(replacement)||replacement==slot))replacement++;
            target.Slots[occupied.Id]=replacement;
        }
        if(from!=to)
        {
            if(copy) { item=JsonSerializer.Deserialize<ShortcutItem>(JsonSerializer.Serialize(item))!; item.Id=Guid.NewGuid().ToString("N"); }
            else {source.Items.Remove(item);source.Slots.Remove(itemId);}
            target.Items.Add(item);
        }
        target.Slots[item.Id]=slot; return next;
    }
    public static FolderConfig Clone(FolderConfig source) => JsonSerializer.Deserialize<FolderConfig>(JsonSerializer.Serialize(source))!;
    public static FolderConfig Transfer(FolderConfig current, string from, string to, string itemId, int index, bool copy)
    {
        var next = Clone(current);
        var source = next.Items.FirstOrDefault(f => f.Id == from); var destination = next.Items.FirstOrDefault(f => f.Id == to);
        var item = source?.Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null || source == null) return next;
        if (!string.IsNullOrWhiteSpace(item.ChildFolderId)) copy = false;
        if (destination == null) { destination = new FolderData { Id = to }; next.Items.Add(destination); }
        if (from != to && IsDuplicate(destination, item)) return next;
        if (from == to) { int oldIndex=source.Items.IndexOf(item); if(oldIndex<index)index--; }
        if (from == to || !copy)
        {
            source.Items.Remove(item);
            if (from != to) source.Slots?.Remove(itemId);
        }
        if (copy && from != to) { item = JsonSerializer.Deserialize<ShortcutItem>(JsonSerializer.Serialize(item))!; item.Id = Guid.NewGuid().ToString("N"); }
        destination.Items.Insert(Math.Clamp(index, 0, destination.Items.Count), item);
        foreach (var folder in next.Items) for (int n = 0; n < folder.Items.Count; n++) folder.Items[n].Order = n;
        return next;
    }
    private static bool IsDuplicate(FolderData target, ShortcutItem item) =>
        (!string.IsNullOrWhiteSpace(item.SourcePath) && target.Items.Any(i => string.Equals(i.SourcePath, item.SourcePath, StringComparison.OrdinalIgnoreCase))) ||
        (!string.IsNullOrWhiteSpace(item.ChildFolderId) && target.Items.Any(i => string.Equals(i.ChildFolderId, item.ChildFolderId, StringComparison.OrdinalIgnoreCase)));
}
