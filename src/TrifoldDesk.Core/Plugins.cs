using System.Globalization;
namespace TrifoldDesk.Core;

public sealed record PluginDefinition(string Id, string Name, string Description, int Pane, bool Multiple = false);
public sealed class WidgetInstance
{
    public string InstanceId { get; set; } = Guid.NewGuid().ToString("N");
    public string PluginId { get; set; } = "";
    public int PaneIndex { get; set; } = 2;
    public string Style { get; set; } = "glass";
    public string Title { get; set; } = "";
    public double X { get; set; } = -1;
    public double Y { get; set; } = -1;
    public double Width { get; set; }
    public double Height { get; set; }
    public bool IsCollapsed { get; set; }
    public bool IsLocked { get; set; }
    public bool AutoCollapse { get; set; }
    public bool FolderAutoHeight { get; set; } = true;
    public List<string> Modules { get; set; } = [];
    public List<DateCounter> Dates {get;set;}=[];
}
public sealed class DateCounter
{
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Name {get;set;}="纪念日";
    public DateTime Date {get;set;}=DateTime.Today;
    public int DaysFrom(DateTime today)=>(Date.Date-today.Date).Days;
}
public sealed class WidgetConfig
{
    public int SchemaVersion { get; set; } = 2;
    public List<WidgetInstance> Items { get; set; } = [];
}
public static class PluginRules
{
    public static IReadOnlyList<PluginDefinition> Catalog { get; } = [
        new("system-summary", "系统概览 · 组合", "CPU、内存、GPU、网络、电池同框；右键选择显示内容。", 2),
        new("apps", "应用集合", "常用应用、分组与拖入添加。移除插件保留入口。", 0),
        new("projects", "项目资料", "项目、文件夹和文档快捷入口。", 1),
        new("calendar", "日历", "完整月历、农历、日期选择；可添加多个，放在任意一折。", 2, true),
        new("folder", "应用文件夹", "把多个软件放进一个可展开的虚拟文件夹；拖动右下角调整图标显示数量。", 0, true),
        new("clock", "桌面时钟", "大数字时间、日期与秒钟进度；可添加多个主题。", 2, true),
        new("battery", "电池", "真实电量与供电状态；台式机明确显示没有电池。", 2),
        new("gpu", "GPU · 3D", "Windows GPU 3D引擎占用曲线；计数器不支持时明确提示。", 2),
        new("cpu", "CPU 状态", "实时 CPU 总占用。与其他状态插件共享采样。", 2),
        new("memory", "内存状态", "物理内存占用与已用容量。", 2),
        new("network", "网络速率", "所选网卡的即时上传与下载。", 2),
        new("disks", "全部磁盘", "所有逻辑卷的容量、可用空间与打开入口。", 2),
        new("controls", "快捷控制", "音量、亮度、常亮与 Windows 设置入口。", 2)
    ];
    public static bool CanAdd(WidgetConfig config, string id, int pane)
    {
        var definition = Catalog.FirstOrDefault(p => p.Id == id);
        return definition != null && pane is >= 0 and <= 2
            && config.Items.Count < 24 && (definition.Multiple || !config.Items.Any(w => w.PluginId == id));
    }
    public static WidgetInstance Add(WidgetConfig config, string id, int pane, string style = "glass")
    {
        if (!CanAdd(config, id, pane)) throw new InvalidOperationException("插件不可重复添加、位置不匹配，或已达24个实例上限。");
        var item = new WidgetInstance { PluginId = id, PaneIndex = pane, Style = style is "paper" or "neon" ? style : "glass" };
        config.Items.Add(item); return item;
    }
    public static WidgetConfig Defaults(bool legacy)
    {
        var result = new WidgetConfig(); Add(result, "apps", 0); Add(result, "projects", 1); Add(result, "calendar", 2);
        if (legacy) foreach (var id in new[] { "cpu", "memory", "network", "disks", "controls" }) Add(result, id, 2);
        return result;
    }
    public static WidgetConfig Normalize(WidgetConfig source)
    {
        var clean = new WidgetConfig();
        foreach (var item in Clone(source).Items ?? [])
            if (item != null && CanAdd(clean, item.PluginId, item.PaneIndex) && !clean.Items.Any(w => w.InstanceId == item.InstanceId))
            { if (string.IsNullOrWhiteSpace(item.InstanceId)) item.InstanceId = Guid.NewGuid().ToString("N"); if (item.PluginId == "system-summary") item.Modules = SummaryRules.Modules(item).ToList(); clean.Items.Add(item); }
        return clean;
    }
    public static WidgetConfig Clone(WidgetConfig source) => System.Text.Json.JsonSerializer.Deserialize<WidgetConfig>(System.Text.Json.JsonSerializer.Serialize(source))!;
}
public static class SummaryRules
{
    public static readonly string[] Available = ["cpu", "memory", "gpu", "network", "battery", "disks"];
    public static IReadOnlyList<string> Modules(WidgetInstance item)
    {
        var selected = (item.Modules ?? []).Where(Available.Contains).Distinct().ToArray();
        return selected.Length == 0 ? Available : selected;
    }
    public static bool Needs(WidgetConfig config, string id) => config.Items.Any(w => w.PluginId == id || w.PluginId == "system-summary" && Modules(w).Contains(id));
}
public readonly record struct WidgetBounds(double X, double Y, double Width, double Height);
public static class WidgetLayout
{
    public static int NearestPane(double x, double width, double availableWidth)
    {
        double pane = Math.Max(1, double.IsFinite(availableWidth) ? availableWidth / 3 : 1);
        double center = (double.IsFinite(x) ? x : 0) + (double.IsFinite(width) ? Math.Max(0, width) : 0) / 2;
        return (int)Math.Clamp(Math.Floor(center / pane), 0, 2);
    }
    public static WidgetBounds ClampToPane(double x, double y, double width, double height, int paneIndex, double availableWidth, double availableHeight, double minimumWidth = 200, double minimumHeight = 120)
    {
        double paneWidth = Math.Max(1, double.IsFinite(availableWidth) ? availableWidth / 3 : 1);
        double left = Math.Clamp(paneIndex, 0, 2) * paneWidth;
        var local = Clamp(x - left, y, width, height, paneWidth, availableHeight, minimumWidth, minimumHeight);
        return local with { X = local.X + left };
    }
    public static WidgetBounds Clamp(double x, double y, double width, double height, double availableWidth, double availableHeight, double minimumWidth = 200, double minimumHeight = 120)
    {
        double w = Math.Max(1, double.IsFinite(availableWidth) ? availableWidth : 1), h = Math.Max(1, double.IsFinite(availableHeight) ? availableHeight : 1);
        width = Math.Clamp(double.IsFinite(width) ? width : 320, Math.Min(minimumWidth, w), w);
        height = Math.Clamp(double.IsFinite(height) ? height : 300, Math.Min(minimumHeight, h), h);
        return new(Math.Clamp(double.IsFinite(x) ? x : 0, 0, w - width), Math.Clamp(double.IsFinite(y) ? y : 0, 0, h - height), width, height);
    }
}
public sealed record CalendarDay(DateTime Date, bool InMonth);
public static class CalendarMonth
{
    public static IReadOnlyList<CalendarDay> Days(DateTime month)
    {
        var first = new DateTime(month.Year, month.Month, 1);
        var start = first.AddDays(-((int)first.DayOfWeek + 6) % 7);
        return Enumerable.Range(0, 42).Select(i => { var date = start.AddDays(i); return new CalendarDay(date, date.Month == month.Month && date.Year == month.Year); }).ToArray();
    }
    public static string LunarText(DateTime date)
    {
        var lunar = new ChineseLunisolarCalendar();
        if (date < lunar.MinSupportedDateTime || date > lunar.MaxSupportedDateTime) return "农历超出支持范围";
        int month = lunar.GetMonth(date), leap = lunar.GetLeapMonth(lunar.GetYear(date)); bool isLeap = leap == month;
        if (leap > 0 && month >= leap) month--;
        string[] months = ["正", "二", "三", "四", "五", "六", "七", "八", "九", "十", "冬", "腊"];
        string[] digits = ["一", "二", "三", "四", "五", "六", "七", "八", "九", "十"];
        int day = lunar.GetDayOfMonth(date);
        string dayName = day switch { 10 => "初十", 20 => "二十", 30 => "三十", < 10 => "初" + digits[day - 1], < 20 => "十" + digits[day - 11], _ => "廿" + digits[day - 21] };
        return (isLeap ? "闰" : "") + months[month - 1] + "月" + dayName;
    }
}
