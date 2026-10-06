using System.Globalization;
using System.Text.Json;

namespace TrifoldDesk;

public sealed class AgendaWindow : Window
{
    private readonly ConfigManager _store;
    private readonly ListBox _events = new() { DisplayMemberPath = nameof(CalendarEvent.Title), MinHeight = 170 };
    private readonly TextBox _title = new() { MaxLength = 200 };
    private readonly DatePicker _date = new() { SelectedDate = DateTime.Today };
    private readonly TextBox _time = new() { Text = "09:00" };
    private readonly ComboBox _repeat = new() { ItemsSource = new[] { "不重复", "每周", "每年（2月29日仅闰年）" }, SelectedIndex = 0 };
    private readonly CheckBox _reminder = new() { Content = "启用提醒", Margin = new Thickness(0, 10, 0, 4) };
    private readonly TextBox _lead = new() { Text = "0" };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), Foreground = Brushes.DarkRed };
    private readonly DatePicker _weekDate = new() { SelectedDate = DateTime.Today };
    private readonly StackPanel _week = new();
    private string? _editingId;
    public AgendaWindow(ConfigManager store)
    {
        _store = store;
        Title = "日程与周历"; Width = 780; Height = 650; MinWidth = 620; MinHeight = 500;
        WindowStartupLocation = WindowStartupLocation.CenterOwner; Background = Brushes.WhiteSmoke; Foreground = Brushes.Black;
        var tabs = new TabControl { Margin = new Thickness(16) };
        var root = new DockPanel(); Content = root; _status.Margin = new Thickness(16, 0, 16, 12);
        DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status); root.Children.Add(tabs);
        var editor = new Grid { Margin = new Thickness(12) };
        editor.ColumnDefinitions.Add(new ColumnDefinition()); editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); editor.ColumnDefinitions.Add(new ColumnDefinition());
        tabs.Items.Add(new TabItem { Header = "日程", Content = editor });
        var library = new DockPanel(); editor.Children.Add(library);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) }; DockPanel.SetDock(actions, Dock.Bottom); library.Children.Add(actions);
        actions.Children.Add(Button("新建", ResetEditor)); actions.Children.Add(Button("删除所选", DeleteSelected));
        library.Children.Add(_events);
        _events.SelectionChanged += (_, _) => { if (_events.SelectedItem is CalendarEvent item) Edit(item); };
        var fields = new StackPanel(); Grid.SetColumn(fields, 2); editor.Children.Add(fields);
        Field(fields, "名称", _title); Field(fields, "日期", _date); Field(fields, "本地时间（HH:mm）", _time); Field(fields, "重复", _repeat);
        fields.Children.Add(_reminder); Field(fields, "提前分钟数（0到10080）", _lead);
        fields.Children.Add(Button("保存日程", SaveEvent));
        var weekly = new DockPanel { Margin = new Thickness(12) };
        tabs.Items.Add(new TabItem { Header = "周历 / 节假日", Content = weekly });
        var top = new StackPanel { Orientation = Orientation.Horizontal }; DockPanel.SetDock(top, Dock.Top); weekly.Children.Add(top);
        _weekDate.Width = 150; top.Children.Add(_weekDate); top.Children.Add(Button("刷新", Refresh)); top.Children.Add(Button("导入节假日JSON", ImportHolidays));
        _weekDate.SelectedDateChanged += (_, _) => RenderWeek();
        weekly.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _week });
        Refresh();
    }
    private static Button Button(string title, Action action)
    {
        var button = new Button { Content = title, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 4, 8, 4) };
        button.Click += (_, _) => action(); return button;
    }
    private static void Field(Panel panel, string label, Control control)
    {
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 3) }); panel.Children.Add(control);
    }
    private EventConfig Read()
    {
        var loaded = _store.Load<EventConfig>("events.json");
        if (loaded.Warning != null) _status.Text = loaded.Warning;
        loaded.Value.Items ??= []; loaded.Value.HolidayYears ??= [];
        return loaded.Value;
    }
    private void Edit(CalendarEvent item)
    {
        _editingId = item.Id; _title.Text = item.Title; _date.SelectedDate = item.StartLocal.Date; _time.Text = item.StartLocal.ToString("HH:mm");
        _repeat.SelectedIndex = (int)item.Recurrence; _reminder.IsChecked = item.ReminderEnabled; _lead.Text = item.ReminderLeadMinutes.ToString(CultureInfo.InvariantCulture);
    }
    private void ResetEditor()
    {
        _events.SelectedItem = null; _editingId = null; _title.Text = ""; _date.SelectedDate = DateTime.Today; _time.Text = "09:00";
        _repeat.SelectedIndex = 0; _reminder.IsChecked = false; _lead.Text = "0"; _status.Text = ""; _title.Focus();
    }
    private void SaveEvent()
    {
        if (_date.SelectedDate is not { } date || !TimeSpan.TryParseExact(_time.Text.Trim(), @"hh\:mm", CultureInfo.InvariantCulture, out var time) || time.TotalDays >= 1 || !int.TryParse(_lead.Text, out int lead))
        { _status.Text = "请选择日期，填写HH:mm时间与整数提醒分钟数。"; return; }
        var item = new CalendarEvent { Id = _editingId ?? Guid.NewGuid().ToString("N"), Title = _title.Text.Trim(), StartLocal = DateTime.SpecifyKind(date.Date + time, DateTimeKind.Unspecified), Recurrence = (EventRecurrence)_repeat.SelectedIndex, ReminderEnabled = _reminder.IsChecked == true, ReminderLeadMinutes = lead };
        var error = CalendarEventRules.Validate(item);
        if (error != null) { _status.Text = error; return; }
        Execute(() => {
            var config = Read(); int index = config.Items.FindIndex(e => e.Id == item.Id);
            if (index >= 0) config.Items[index] = item; else config.Items.Add(item);
            _store.Save("events.json", config); _editingId = item.Id; _status.Text = "日程已保存。"; Refresh();
        });
    }
    private void DeleteSelected()
    {
        if (_events.SelectedItem is not CalendarEvent item) return;
        Execute(() => {
            var config = Read(); config.Items.RemoveAll(e => e.Id == item.Id); _store.Save("events.json", config); ResetEditor(); Refresh();
        });
    }
    private void Refresh() => Execute(() => {
        var config = Read(); _events.ItemsSource = config.Items.OrderBy(e => e.StartLocal).ThenBy(e => e.Title).ToArray();
        RenderWeek(config);
    });
    private void RenderWeek(EventConfig? config = null) => Execute(() => {
        config ??= Read(); _week.Children.Clear();
        foreach (var day in CalendarEventRules.Week(config, _weekDate.SelectedDate ?? DateTime.Today))
        {
            var text = new TextBlock { Text = day.Date.ToString("M月d日 dddd") + "  ·  " + day.Holiday.Text, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 14, 0, 5) };
            if (day.Holiday.Available) text.ToolTip = "节假日安排\n来源：" + day.Holiday.Source + "\n更新：" + day.Holiday.UpdatedLocal?.ToString("yyyy-MM-dd");
            _week.Children.Add(text);
            if (day.Events.Count == 0) _week.Children.Add(new TextBlock { Text = "暂无日程", Foreground = Brushes.Gray });
            foreach (var occurrence in day.Events) _week.Children.Add(new TextBlock { Text = occurrence.StartLocal.ToString("HH:mm") + "  " + occurrence.Event.Title, TextWrapping = TextWrapping.Wrap });
        }
    });
    private void ImportHolidays()
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "节假日数据 (*.json)|*.json", Title = "导入按年份维护的节假日调休数据" };
        if (picker.ShowDialog(this) != true) return;
        Execute(() => {
            if (new FileInfo(picker.FileName).Length > 1024 * 1024) throw new IOException("节假日JSON不能超过1MB。");
            var year = HolidayRules.Import(File.ReadAllText(picker.FileName)); var config = Read();
            config.HolidayYears.RemoveAll(y => y.Year == year.Year); config.HolidayYears.Add(year);
            _store.Save("events.json", config); _status.Text = $"已导入{year.Year}年数据；来源：{year.Source}"; RenderWeek(config);
        });
    }
    private void Execute(Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException or InvalidOperationException)
        { _status.Text = "操作失败：" + ex.Message; }
    }
}
