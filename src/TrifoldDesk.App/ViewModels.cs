using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using TrifoldDesk.Services;

namespace TrifoldDesk;
public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
public sealed class ShortcutViewModel : Observable
{
    public ShortcutItem Item { get; }
    public ShortcutViewModel(ShortcutItem item) { Item = item; Refresh(); }
    public string Name => Item.Name;
    public string Group => string.IsNullOrWhiteSpace(Item.Group) ? "未分组" : Item.Group;
    public string ScreenName => Item.ScreenIndex == 0 ? "应用" : "资料";
    private bool _selected;
    public bool IsSelected { get => _selected; set { if (_selected == value) return; _selected = value; Changed(); } }
    public string Tooltip => Item.Name + "\n" + Item.SourcePath + (IsMissing ? "\n已失效，请右键重新定位" : "");
    public bool IsMissing => !ShellLinkHelper.IsAvailable(Item);
    public ImageSource? Icon { get; private set; }
    public void Refresh()
    {
        if (File.Exists(Item.SourcePath) && Path.GetExtension(Item.SourcePath).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            var current = ShellLinkHelper.CreateShortcut(Item.SourcePath, Item.ScreenIndex, Item.Order);
            if (Item.TargetPath != current.TargetPath || Item.IconPath != current.IconPath || Item.IconIndex != current.IconIndex) ShellLinkHelper.InvalidateIcon(Item.SourcePath);
            Item.TargetPath = current.TargetPath;
            if(Item.LaunchMode=="shell"){Item.Arguments = current.Arguments; Item.WorkingDirectory = current.WorkingDirectory;}
            Item.IconPath = current.IconPath; Item.IconIndex = current.IconIndex;
        }
        Icon = ShellLinkHelper.GetIcon(Item); Changed(nameof(Icon)); Changed(nameof(Name)); Changed(nameof(Tooltip)); Changed(nameof(IsMissing)); Changed(nameof(Group)); Changed(nameof(ScreenName));
    }
}
public sealed class MainViewModel : Observable
{
    public ObservableCollection<ShortcutViewModel> Apps { get; } = [];
    public ObservableCollection<ShortcutViewModel> Projects { get; } = [];
    public ObservableCollection<ShortcutViewModel> AllItems { get; } = [];
    public ObservableCollection<string> Groups { get; } = ["全部分组", "未分组"];
    public ObservableCollection<DriveSnapshot> Drives { get; } = [];
    public ICollectionView AppsView { get; }
    public ICollectionView ProjectsView { get; }
    public ICollectionView ManagementView { get; }
    private string _appsSearch = "", _projectsSearch = "", _managementSearch = "", _appGroup = "全部分组", _projectGroup = "全部分组";
    public string AppsSearch { get => _appsSearch; set { _appsSearch = value; AppsView.Refresh(); Changed(); } }
    public string ProjectsSearch { get => _projectsSearch; set { _projectsSearch = value; ProjectsView.Refresh(); Changed(); } }
    public string ManagementSearch { get => _managementSearch; set { _managementSearch = value; ManagementView.Refresh(); Changed(); } }
    public string AppGroup { get => _appGroup; set { _appGroup = value ?? "全部分组"; AppsView.Refresh(); Changed(); Changed(nameof(AppGroupIndex)); } }
    public string ProjectGroup { get => _projectGroup; set { _projectGroup = value ?? "全部分组"; ProjectsView.Refresh(); Changed(); Changed(nameof(ProjectGroupIndex)); } }
    public int AppGroupIndex { get => Groups.IndexOf(AppGroup); set { if (value >= 0 && value < Groups.Count) AppGroup = Groups[value]; } }
    public int ProjectGroupIndex { get => Groups.IndexOf(ProjectGroup); set { if (value >= 0 && value < Groups.Count) ProjectGroup = Groups[value]; } }
    public int SelectedCount => AllItems.Count(i => i.IsSelected);
    public string SelectionText => $"已选 {SelectedCount} 项 / 共 {AllItems.Count} 项";
    public string AppCountText => $"{Apps.Count} 个入口";
    public string ProjectCountText => $"{Projects.Count} 个入口";
    public string DriveCountText => $"全部磁盘 · {Drives.Count} 个卷";
    public MainViewModel()
    {
        AppsView = new ListCollectionView(Apps) { Filter = value => LibraryRules.Matches(((ShortcutViewModel)value).Item, AppsSearch, AppGroup) };
        ProjectsView = new ListCollectionView(Projects) { Filter = value => LibraryRules.Matches(((ShortcutViewModel)value).Item, ProjectsSearch, ProjectGroup) };
        ManagementView = new ListCollectionView(AllItems) { Filter = value => LibraryRules.Matches(((ShortcutViewModel)value).Item, ManagementSearch, "全部分组") };
        AppsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ShortcutViewModel.Group)));
        ProjectsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ShortcutViewModel.Group)));
    }
    public void RebuildLibrary()
    {
        string appGroup = AppGroup, projectGroup = ProjectGroup;
        foreach (var item in AllItems) item.PropertyChanged -= ItemChanged;
        AllItems.Clear();
        foreach (var item in Apps.Concat(Projects)) { AllItems.Add(item); item.PropertyChanged += ItemChanged; }
        var names = AllItems.Select(i => i.Group).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(g => g).ToList();
        names.Insert(0, "全部分组"); if (!names.Contains("未分组")) names.Add("未分组");
        if (!Groups.SequenceEqual(names)) { Groups.Clear(); foreach (string name in names) Groups.Add(name); }
        AppGroup = Groups.Contains(appGroup) ? appGroup : "全部分组";
        ProjectGroup = Groups.Contains(projectGroup) ? projectGroup : "全部分组";
        AppsView.Refresh(); ProjectsView.Refresh(); ManagementView.Refresh();
        Changed(nameof(SelectionText)); Changed(nameof(SelectedCount)); Changed(nameof(AppCountText)); Changed(nameof(ProjectCountText));
    }
    private void ItemChanged(object? sender, PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(ShortcutViewModel.IsSelected)) { Changed(nameof(SelectedCount)); Changed(nameof(SelectionText)); } }
    public void UpdateDrives(IReadOnlyList<DriveSnapshot> drives)
    { Drives.Clear(); foreach (var drive in drives) Drives.Add(drive); Changed(nameof(DriveCountText)); }
    public string ClockText => DateTime.Now.ToString("HH:mm");
    public string DateText => DateTime.Now.ToString("yyyy年M月d日 dddd");
    public string BatteryText => DesktopControlService.BatteryText();
    public void RefreshClock() { Changed(nameof(ClockText)); Changed(nameof(DateText)); Changed(nameof(BatteryText)); }
    private MonitorSnapshot? _snapshot;
    public string CpuText => _snapshot?.Cpu is double cpu ? $"{cpu:0}%" : _snapshot == null ? "采样中" : "暂不可用";
    public double CpuValue => _snapshot?.Cpu ?? 0;
    public string MemoryPercent => _snapshot?.MemoryPercent is double percent ? $"{percent:0}%" : "—";
    public double MemoryValue => _snapshot?.MemoryPercent ?? 0;
    public string MemoryText => _snapshot?.MemoryText ?? "读取物理内存";
    public string DownloadText => "↓ " + (_snapshot?.Download ?? "—");
    public string UploadText => "↑ " + (_snapshot?.Upload ?? "—");
    public string NetworkName => _snapshot?.NetworkName ?? "连接状态";
    public double? DownloadRate => _snapshot?.DownloadRate;
    public double? UploadRate => _snapshot?.UploadRate;
    public string DiskText => _snapshot?.DiskFreeGb is double free ? $"{free:0.0} GB 可用" : "读取系统盘";
    public string DiskLabel => (_snapshot?.DiskName ?? "系统盘") + " · 存储";
    public double DiskValue => _snapshot?.DiskUsedPercent ?? 0;
    public bool DiskLow => DiskValue > 90;
    private string _status = "拖入应用或文件，建立你的桌面入口";
    public string Status { get => _status; set { _status = value; Changed(); } }
    private bool _awake;
    public bool IsAwake { get => _awake; set { _awake = value; Changed(); Changed(nameof(AwakeLabel)); } }
    public string AwakeLabel => IsAwake ? "● 常亮已开启" : "防息屏常亮";
    public void Update(MonitorSnapshot snapshot)
    {
        _snapshot = snapshot;
        foreach (var name in new[] { nameof(CpuText), nameof(CpuValue), nameof(MemoryPercent), nameof(MemoryValue), nameof(MemoryText), nameof(DownloadText), nameof(UploadText), nameof(NetworkName), nameof(DiskText), nameof(DiskLabel), nameof(DiskValue), nameof(DiskLow) }) Changed(name);
    }
}
