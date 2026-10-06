using System.Diagnostics;
using System.Text.Json;
using System.Windows.Threading;
using TrifoldDesk.Services;

namespace TrifoldDesk;

public sealed class ProjectBrowserWidget : Grid, IProfileReplacementParticipant
{
    private readonly string _instanceId;
    private readonly ConfigManager _store;
    private string _rootPath = "";
    private readonly TreeView _tree = new() { Background = Brushes.Transparent, Foreground = Brushes.WhiteSmoke, BorderThickness = new Thickness(0) };
    private readonly TextBlock _status = new() { Foreground = Brushes.LightGray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 4, 4, 8) };
    private readonly TextBlock _git = new() { Foreground = Brushes.LightGray, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 6, 4, 4) };
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(60) };
    private readonly HashSet<TreeViewItem> _pending = [];
    private CancellationTokenSource? _lifetime;
    private DateTime _lastGitAttempt;
    private bool _gitBusy;
    private bool _retired;
    public ProjectBrowserWidget(string instanceId, ConfigManager store)
    {
        _instanceId = instanceId; _store = store;
        RowDefinitions.Add(new() { Height = GridLength.Auto }); RowDefinitions.Add(new()); RowDefinitions.Add(new() { Height = GridLength.Auto });
        Children.Add(_status); Grid.SetRow(_tree, 1); Children.Add(_tree); Grid.SetRow(_git, 2); Children.Add(_git);
        try { var loaded = store.Load<ProjectBrowserConfig>("optional-projects.json"); if (loaded.Value.Widgets?.TryGetValue(instanceId, out var data) == true) _rootPath = data.RootPath ?? ""; _status.Text = loaded.Warning ?? ""; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { _status.Text = "读取失败：" + ex.Message; }
        var menu = new ContextMenu(); Menu(menu, "选择项目目录…", ChooseRoot); Menu(menu, "刷新目录 / Git", async () => { await LoadRoot(); await RefreshGit(); }); ContextMenu = menu;
        _clock.Tick += async (_, _) => await RefreshGit();
        Loaded += async (_, _) => { if(_retired)return;_lifetime?.Dispose(); _lifetime = new(); _clock.Start(); await LoadRoot(); await RefreshGit(); };
        Unloaded += (_, _) => { _clock.Stop(); _lifetime?.Cancel(); _pending.Clear(); };
        if (string.IsNullOrWhiteSpace(_rootPath)) { _status.Text = "尚未选择项目目录 · 右键选择"; _git.Text = "Git：未配置项目"; }
        else { _status.Text = Path.GetFileName(_rootPath); _git.Text = "Git：等待读取"; }
    }
    private static void Menu(ContextMenu menu, string title, Func<Task> action)
    {
        var item = new MenuItem { Header = title }; item.Click += async (_, _) => await action(); menu.Items.Add(item);
    }
    private async Task ChooseRoot()
    {
        var picker = new Microsoft.Win32.OpenFolderDialog { Title = "选择项目目录", Multiselect = false };
        var owner = Window.GetWindow(this); if (owner == null || picker.ShowDialog(owner) != true) return;
        try
        {
            var config = _store.Load<ProjectBrowserConfig>("optional-projects.json").Value; config.Widgets ??= [];
            config.Widgets[_instanceId] = new() { RootPath = picker.FolderName }; _store.Save("optional-projects.json", config);
            _lifetime?.Cancel(); _lifetime?.Dispose(); _lifetime = new(); _rootPath = picker.FolderName; _lastGitAttempt = default; _git.Text = "Git：等待读取";
            await LoadRoot(); await RefreshGit();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { _status.Text = "选择目录失败：" + ex.Message; }
    }
    private async Task LoadRoot()
    {
        if (_lifetime == null || _lifetime.IsCancellationRequested || string.IsNullOrWhiteSpace(_rootPath)) return;
        string path = _rootPath; var token = _lifetime.Token; _tree.Items.Clear(); _status.Text = "读取目录…";
        try
        {
            var snapshot = await Task.Run(() => MappedDirectoryScanner.Scan(path, 500, token), token);
            if (token.IsCancellationRequested || !IsLoaded || path != _rootPath) return;
            _status.Text = snapshot.Error == null ? Path.GetFileName(path) : "目录不可用：" + snapshot.Error; _status.ToolTip = path;
            foreach (var entry in snapshot.Entries) _tree.Items.Add(Node(entry, token));
            if (snapshot.IsTruncated) _tree.Items.Add(new TreeViewItem { Header = "超过500个条目 · 请打开系统文件夹查看", IsEnabled = false });
        }
        catch (OperationCanceledException) { }
    }
    private TreeViewItem Node(MappedDirectoryEntry entry, CancellationToken token)
    {
        var node = new TreeViewItem { Header = entry.Name, Tag = entry, ToolTip = entry.Path, Foreground = Brushes.WhiteSmoke };
        var menu = new ContextMenu(); Menu(menu, "打开", () => { Open(entry.Path); return Task.CompletedTask; }); node.ContextMenu = menu;
        if (entry.IsDirectory && !entry.IsReparsePoint)
        {
            node.Items.Add(new TreeViewItem { Header = "加载中…" });
            node.Expanded += async (_, e) => {
                if (e.OriginalSource != node || _pending.Contains(node) || node.Items.Count != 1 || node.Items[0] is not TreeViewItem placeholder || placeholder.Tag != null || (string?)placeholder.Header != "加载中…") return;
                _pending.Add(node);
                try
                {
                    var scanned = await Task.Run(() => MappedDirectoryScanner.Scan(entry.Path, 500, token), token);
                    if (token.IsCancellationRequested || !IsLoaded) return; node.Items.Clear();
                    foreach (var child in scanned.Entries) node.Items.Add(Node(child, token));
                    if (scanned.Error != null) node.Items.Add(new TreeViewItem { Header = "无法读取：" + scanned.Error, IsEnabled = false });
                    if (scanned.IsTruncated) node.Items.Add(new TreeViewItem { Header = "仅显示前500个条目", IsEnabled = false });
                }
                catch (OperationCanceledException) { }
                finally { _pending.Remove(node); }
            };
        }
        else if (entry.IsDirectory) node.ToolTip = entry.Path + "\n目录链接：使用右键打开，避免循环展开";
        return node;
    }
    private void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException) { _status.Text = "打开失败：" + ex.Message; }
    }
    private async Task RefreshGit()
    {
        if (_gitBusy || _lifetime == null || _lifetime.IsCancellationRequested || string.IsNullOrWhiteSpace(_rootPath) || DateTime.UtcNow - _lastGitAttempt < TimeSpan.FromSeconds(60)) return;
        _gitBusy = true; _lastGitAttempt = DateTime.UtcNow; string path = _rootPath; var token = _lifetime.Token;
        try
        {
            var snapshot = await GitReadService.ReadAsync(path, token);
            if (token.IsCancellationRequested || !IsLoaded || path != _rootPath) return;
            _git.Text = snapshot.Error ?? (snapshot.Changes.Count == 0 ? "Git：工作区干净" : $"Git：{snapshot.Changes.Count} 个变更");
            _git.ToolTip = snapshot.Error ?? string.Join("\n", snapshot.Changes.Take(30).Select(c => c.Status + " " + c.Path)) + (snapshot.Changes.Count > 30 ? "\n…" : "");
        }
        catch (OperationCanceledException) { }
        finally { _gitBusy = false; }
    }
    public void PrepareForProfileReplacement(){_retired=true;_clock.Stop();_lifetime?.Cancel();_pending.Clear();}
}
