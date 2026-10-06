using System.Text.Json;
using System.Windows.Threading;

namespace TrifoldDesk;

public sealed class ProductivityWidget : Grid, IProfileReplacementParticipant
{
    private readonly string _instanceId;
    private readonly ConfigManager _store;
    private WorkbenchData _data = new();
    private readonly TextBox _notes = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.Transparent, Foreground = Brushes.WhiteSmoke, BorderThickness = new Thickness(0), MaxLength = 100000, Padding = new Thickness(4) };
    private readonly StackPanel _tasks = new();
    private readonly ScrollViewer _taskScroll;
    private readonly TextBlock _status = new() { FontSize = 11, Foreground = Brushes.LightGray, Margin = new Thickness(4, 6, 4, 0), TextWrapping = TextWrapping.Wrap };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };
    private bool _dirty;
    private bool _retired;
    private bool _showArchived;
    public ProductivityWidget(string instanceId, ConfigManager store)
    {
        _instanceId = instanceId; _store = store;
        RowDefinitions.Add(new()); RowDefinitions.Add(new() { Height = GridLength.Auto });
        try { var loaded = store.Load<ProductivityConfig>("optional-workbench.json"); if (loaded.Value.Widgets?.TryGetValue(instanceId, out var saved) == true) _data = saved; _status.Text = loaded.Warning ?? "右键切换便签 / 待办"; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { _status.Text = "读取失败：" + ex.Message; }
        _data.Tasks ??= []; _notes.Text = _data.Notes ?? ""; _taskScroll = new ScrollViewer { Content = _tasks, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Children.Add(_notes); Children.Add(_taskScroll); Grid.SetRow(_status, 1); Children.Add(_status);
        _notes.TextChanged += (_, _) => { _dirty = true; _saveTimer.Stop(); _saveTimer.Start(); };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Flush(); };
        Unloaded += (_, _) => { _saveTimer.Stop(); Flush(); };
        var menu = new ContextMenu();
        Menu(menu, "便签", () => Switch("notes")); Menu(menu, "待办", () => { _showArchived = false; Switch("todo"); });
        Menu(menu, "添加待办", AddTask); Menu(menu, "归档记录", () => { _showArchived = true; Switch("todo"); });
        Menu(menu, "归档已完成", () => Change(data => { var next = WorkbenchRules.Clone(data); foreach (var task in next.Tasks.Where(t => t != null && t.IsCompleted)) task.IsArchived = true; return next; }));
        ContextMenu = menu; Render();
    }
    private static void Menu(ContextMenu menu, string title, Action action)
    {
        var item = new MenuItem { Header = title }; item.Click += (_, _) => action(); menu.Items.Add(item);
    }
    private void Switch(string view) { Flush(); _data.View = view; _dirty = true; Flush(); Render(); }
    private void AddTask()
    {
        var owner = Window.GetWindow(this); if (owner == null) return;
        string? text = TextInputDialog.Ask(owner, "添加待办", ""); if (text == null) return;
        _showArchived = false; _data.View = "todo"; Change(data => WorkbenchRules.AddTask(data, text));
    }
    private void Change(Func<WorkbenchData, WorkbenchData> update)
    {
        _data = update(_data); _dirty = true; Flush(); Render();
    }
    private void Render()
    {
        bool todo = _data.View == "todo"; _notes.Visibility = todo ? Visibility.Collapsed : Visibility.Visible; _taskScroll.Visibility = todo ? Visibility.Visible : Visibility.Collapsed;
        _tasks.Children.Clear(); if (!todo) return;
        var tasks = WorkbenchRules.VisibleTasks(_data, _showArchived);
        if (tasks.Count == 0) _tasks.Children.Add(new TextBlock { Text = _showArchived ? "暂无归档待办" : "暂无待办 · 右键添加", Foreground = Brushes.LightGray, Margin = new Thickness(4, 10, 4, 4) });
        foreach (var task in tasks)
        {
            var row = new CheckBox { Content = new TextBlock { Text = task.Text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.WhiteSmoke, Opacity = task.IsCompleted ? .6 : 1 }, IsChecked = task.IsCompleted, Margin = new Thickness(4, 5, 4, 5), IsEnabled = !task.IsArchived };
            row.Checked += (_, _) => { if (!task.IsArchived) Change(data => WorkbenchRules.SetCompleted(data, task.Id, true)); }; row.Unchecked += (_, _) => { if (!task.IsArchived) Change(data => WorkbenchRules.SetCompleted(data, task.Id, false)); };
            var itemMenu = new ContextMenu(); Menu(itemMenu, task.IsArchived ? "恢复" : "归档", () => Change(data => WorkbenchRules.SetArchived(data, task.Id, !task.IsArchived)));
            // An archived row remains enabled for its recovery context menu; completion is ignored there.
            if (task.IsArchived) { row.IsEnabled = true; row.IsHitTestVisible = true; row.Focusable = false; row.PreviewMouseLeftButtonDown += (_, e) => e.Handled = true; }
            row.ContextMenu = itemMenu; _tasks.Children.Add(row);
        }
    }
    public void Flush()
    {
        if (_retired || !_dirty) return;
        try
        {
            _data.Notes = _notes.Text;
            var config = _store.Load<ProductivityConfig>("optional-workbench.json").Value; config.Widgets ??= [];
            config.Widgets[_instanceId] = WorkbenchRules.Clone(_data); _store.Save("optional-workbench.json", config);
            _dirty = false; _status.Text = _showArchived ? "归档记录 · 右键恢复" : "已自动保存 · 右键切换视图";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        { _status.Text = "保存失败，内容仍保留在当前组件：" + ex.Message; }
    }
    public void PrepareForProfileReplacement()
    {
        FlushBeforeProfileReplacement();_retired=true;
    }
    public void FlushBeforeProfileReplacement()
    {
        _saveTimer.Stop();Flush();
        if(_dirty)throw new IOException("便签尚未成功保存，请处理保存错误后重试导入。");
    }
}
