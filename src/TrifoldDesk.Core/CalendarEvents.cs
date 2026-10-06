using System.Globalization;
using System.Text.Json;

namespace TrifoldDesk.Core;

public enum EventRecurrence { None, Weekly, Yearly }
public sealed class CalendarEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public DateTime StartLocal { get; set; } = DateTime.Today;
    public EventRecurrence Recurrence { get; set; }
    public bool ReminderEnabled { get; set; }
    public int ReminderLeadMinutes { get; set; }
}
public sealed class EventConfig
{
    public int SchemaVersion { get; set; } = 1;
    public List<CalendarEvent> Items { get; set; } = [];
    public Dictionary<string, DateTime> DeliveredReminders { get; set; } = [];
    public List<HolidayYear> HolidayYears { get; set; } = [];
}
public sealed record CalendarOccurrence(CalendarEvent Event, DateTime StartLocal, string Identity);
public sealed record CalendarWeekDay(DateTime Date, IReadOnlyList<CalendarOccurrence> Events, HolidayStatus Holiday);

public static class CalendarEventRules
{
    public static string? Validate(CalendarEvent item)
    {
        if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Title) || item.Title.Length > 200) return "日程需要有效标识和1到200字的名称。";
        if (item.StartLocal.Year is < 1900 or > 9998 || item.StartLocal.Kind == DateTimeKind.Utc) return "日程需要有效的本地日期与时间。";
        if (!Enum.IsDefined(item.Recurrence)) return "不支持的重复方式。";
        if (item.ReminderLeadMinutes is < 0 or > 10080) return "提前提醒分钟数必须在0到10080之间。";
        return null;
    }

    public static IReadOnlyList<CalendarOccurrence> Occurrences(CalendarEvent item, DateTime fromLocal, DateTime untilLocal)
    {
        var result = new List<CalendarOccurrence>();
        if (Validate(item) != null || untilLocal <= fromLocal) return result;
        var isolated = JsonSerializer.Deserialize<CalendarEvent>(JsonSerializer.Serialize(item))!;
        void Add(DateTime start)
        {
            if (start >= item.StartLocal && start >= fromLocal && start < untilLocal)
                result.Add(new(isolated, start, item.Id + ":" + start.Ticks.ToString(CultureInfo.InvariantCulture)));
        }
        switch (item.Recurrence)
        {
            case EventRecurrence.None: Add(item.StartLocal); break;
            case EventRecurrence.Weekly:
                int days = Math.Max(0, (fromLocal.Date - item.StartLocal.Date).Days);
                int weeks = days / 7;
                var candidate = item.StartLocal.AddDays(weeks * 7);
                if (candidate < fromLocal) candidate = AddClamped(candidate, TimeSpan.FromDays(7));
                while (candidate < untilLocal)
                {
                    Add(candidate);
                    if (candidate > DateTime.MaxValue.AddDays(-7)) break;
                    candidate = candidate.AddDays(7);
                }
                break;
            case EventRecurrence.Yearly:
                for (int year = Math.Max(item.StartLocal.Year, fromLocal.Year); year <= untilLocal.Year; year++)
                {
                    if (item.StartLocal.Month == 2 && item.StartLocal.Day == 29 && !DateTime.IsLeapYear(year)) continue;
                    Add(new DateTime(year, item.StartLocal.Month, item.StartLocal.Day).AddTicks(item.StartLocal.TimeOfDay.Ticks));
                }
                break;
        }
        return result;
    }

    public static IReadOnlyList<CalendarWeekDay> Week(EventConfig config, DateTime selectedDate)
    {
        var monday = selectedDate.Date.AddDays(-((int)selectedDate.DayOfWeek + 6) % 7);
        var end = monday.AddDays(7);
        var occurrences = (config.Items ?? []).Where(e => e != null).SelectMany(e => Occurrences(e, monday, end)).OrderBy(e => e.StartLocal).ThenBy(e => e.Event.Title, StringComparer.Ordinal).ToList();
        return Enumerable.Range(0, 7).Select(offset => {
            var date = monday.AddDays(offset);
            return new CalendarWeekDay(date, occurrences.Where(e => e.StartLocal.Date == date).ToArray(), HolidayRules.Status(config.HolidayYears ?? [], date));
        }).ToArray();
    }

    public static IReadOnlyList<CalendarOccurrence> DueReminders(EventConfig config, DateTime nowLocal, DateTime? lastCheckLocal = null)
    {
        var earliest = AddClamped(nowLocal, TimeSpan.FromHours(-24));
        if (lastCheckLocal is { } last && last <= nowLocal && last > earliest) earliest = last;
        var due = new List<CalendarOccurrence>();
        foreach (var item in config.Items ?? [])
        {
            if (item == null || !item.ReminderEnabled || Validate(item) != null) continue;
            var lead = TimeSpan.FromMinutes(item.ReminderLeadMinutes);
            foreach (var occurrence in Occurrences(item, AddClamped(earliest, lead), AddClamped(AddClamped(nowLocal, lead), TimeSpan.FromTicks(1))))
                if (config.DeliveredReminders == null || !config.DeliveredReminders.ContainsKey(occurrence.Identity)) due.Add(occurrence);
        }
        return due.OrderBy(o => o.StartLocal).ThenBy(o => o.Event.Id, StringComparer.Ordinal).DistinctBy(o => o.Identity).ToArray();
    }

    public static EventConfig MarkDelivered(EventConfig config, IEnumerable<CalendarOccurrence> occurrences, DateTime deliveredLocal)
    {
        var next = JsonSerializer.Deserialize<EventConfig>(JsonSerializer.Serialize(config))!;
        next.DeliveredReminders ??= [];
        var cutoff = AddClamped(deliveredLocal, TimeSpan.FromDays(-32));
        foreach (var key in next.DeliveredReminders.Where(pair => pair.Value < cutoff).Select(pair => pair.Key).ToArray()) next.DeliveredReminders.Remove(key);
        foreach (var occurrence in occurrences) next.DeliveredReminders[occurrence.Identity] = deliveredLocal;
        return next;
    }

    private static DateTime AddClamped(DateTime value, TimeSpan offset)
    {
        if (offset.Ticks > 0 && value.Ticks > DateTime.MaxValue.Ticks - offset.Ticks) return DateTime.MaxValue;
        if (offset.Ticks < 0 && value.Ticks < -offset.Ticks) return DateTime.MinValue;
        return value.Add(offset);
    }
}

public sealed class HolidayDay
{
    public DateTime Date { get; set; }
    public string Name { get; set; } = "";
    public bool IsWorkday { get; set; }
}
public sealed class HolidayYear
{
    public int SchemaVersion { get; set; } = 1;
    public int Year { get; set; }
    public string Source { get; set; } = "";
    public DateTime UpdatedLocal { get; set; }
    public List<HolidayDay> Days { get; set; } = [];
}
public sealed record HolidayStatus(bool Available, string Text, bool? IsWorkday, string Source = "", DateTime? UpdatedLocal = null);
public static class HolidayRules
{
    public static HolidayYear? BuiltIn(int year)
    {
        if(year!=2026)return null;
        var result=new HolidayYear{Year=2026,Source="https://www.beijing.gov.cn/cs/gncs/zcwj/202603/t20260327_4568275.html",UpdatedLocal=new(2025,11,4)};
        void Rest(int month,int start,int end,string name){for(int day=start;day<=end;day++)result.Days.Add(new(){Date=new(2026,month,day),Name=name});}
        void Work(int month,int day,string name)=>result.Days.Add(new(){Date=new(2026,month,day),Name=name,IsWorkday=true});
        Rest(1,1,3,"元旦");Work(1,4,"元旦调休");
        Rest(2,15,23,"春节");Work(2,14,"春节调休");Work(2,28,"春节调休");
        Rest(4,4,6,"清明节");Rest(5,1,5,"劳动节");Work(5,9,"劳动节调休");
        Rest(6,19,21,"端午节");Rest(9,25,27,"中秋节");Rest(10,1,7,"国庆节");Work(9,20,"国庆节调休");Work(10,10,"国庆节调休");
        return result;
    }
    public static string? Validate(HolidayYear year)
    {
        if (year.SchemaVersion != 1) return "节假日数据版本不受支持。";
        if (year.Year is < 1900 or > 9998) return "节假日数据年份无效。";
        if (!Uri.TryCreate(year.Source, UriKind.Absolute, out var source) || source.Scheme is not ("http" or "https")) return "需要节假日调休安排的来源网址。";
        if (year.UpdatedLocal.Year < 1900) return "需要数据更新日期。";
        if (year.Days == null || year.Days.Any(d => d == null || d.Date.Year != year.Year || d.Date.TimeOfDay != TimeSpan.Zero || string.IsNullOrWhiteSpace(d.Name))) return "节假日条目需要同一年份的日期与名称。";
        if (year.Days.Select(d => d.Date).Distinct().Count() != year.Days.Count) return "节假日日期不能重复。";
        return null;
    }
    public static HolidayYear Import(string json)
    {
        var imported = JsonSerializer.Deserialize<HolidayYear>(json) ?? throw new JsonException("节假日数据不能为空。");
        string? error = Validate(imported);
        if (error != null) throw new JsonException(error);
        return imported;
    }
    public static HolidayStatus Status(IEnumerable<HolidayYear> years, DateTime date)
    {
        var year = years.FirstOrDefault(y => y != null && y.Year == date.Year) ?? BuiltIn(date.Year);
        if (year == null) return new(false, $"{date.Year}年节假日调休数据未提供", null);
        var error = Validate(year);
        if (error != null) return new(false, "节假日数据不可用：" + error, null);
        var day = year.Days.FirstOrDefault(d => d.Date == date.Date);
        return new(true, day == null ? "该日期未标注调休安排" : day.Name + (day.IsWorkday ? " · 调休工作日" : " · 休息日"), day?.IsWorkday, year.Source, year.UpdatedLocal);
    }
}
