using System.Text.Json;
using TrifoldDesk.Core;

public static class CalendarEventsTests
{
    public static void Run(Action<bool, string> check)
    {
        var official=HolidayRules.BuiltIn(2026)!;
        check(HolidayRules.Validate(official)==null && official.Days.Count==39,"built in official 2026 holiday schedule contains 33 rest days and 6 adjusted workdays");
        check(HolidayRules.Status([],new(2026,10,10)).IsWorkday==true && HolidayRules.Status([],new(2026,10,6)).IsWorkday==false,"calendar uses verified built in holidays without requiring an import");
        var weekly = new CalendarEvent { Id = "weekly", Title = "每周会议", StartLocal = new(2026, 10, 5, 9, 30, 0), Recurrence = EventRecurrence.Weekly, ReminderEnabled = true, ReminderLeadMinutes = 30 };
        var occurrences = CalendarEventRules.Occurrences(weekly, new(2026, 10, 1), new(2026, 10, 20));
        check(occurrences.Select(o => o.StartLocal).SequenceEqual([new DateTime(2026, 10, 5, 9, 30, 0), new(2026, 10, 12, 9, 30, 0), new(2026, 10, 19, 9, 30, 0)]), "weekly recurrence retains weekday and local time across weeks");
        check(CalendarEventRules.Occurrences(weekly, new(2026, 10, 5, 9, 30, 1), new(2026, 10, 12, 9, 30, 0)).Count == 0, "occurrence range uses inclusive start and exclusive end");
        var leap = new CalendarEvent { Id = "leap", Title = "闰日", StartLocal = new(2024, 2, 29, 18, 15, 0), Recurrence = EventRecurrence.Yearly };
        check(CalendarEventRules.Occurrences(leap, new(2025, 1, 1), new(2029, 1, 1)).Single().StartLocal == new DateTime(2028, 2, 29, 18, 15, 0), "yearly February 29 skips non-leap years");
        check(CalendarEventRules.Occurrences(leap, new(2020, 1, 1), new(2024, 1, 1)).Count == 0, "recurrence never invents occurrences before original start");
        var once = new CalendarEvent { Id = "once", Title = "一次", StartLocal = new(2026, 10, 6, 13, 0, 0) };
        check(CalendarEventRules.Occurrences(once, new(2026, 10, 1), new(2026, 11, 1)).Count == 1, "nonrecurring event appears once");
        var config = new EventConfig { Items = [weekly, once] };
        var week = CalendarEventRules.Week(config, new(2026, 10, 7));
        check(week.Count == 7 && week[0].Date == new DateTime(2026, 10, 5) && week[6].Date == new DateTime(2026, 10, 11) && week.Sum(d => d.Events.Count) == 2, "week view provides concrete Monday-through-Sunday occurrences");

        var now = new DateTime(2026, 10, 5, 9, 5, 0);
        var before = JsonSerializer.Serialize(config);
        var due = CalendarEventRules.DueReminders(config, now, now.AddHours(-2));
        check(due.Count == 1 && due[0].Event.Id == "weekly" && due[0].StartLocal == weekly.StartLocal, "reminder lead time triggers before event starts");
        var delivered = CalendarEventRules.MarkDelivered(config, due, now);
        check(CalendarEventRules.DueReminders(delivered, now.AddMinutes(1), now.AddHours(-2)).Count == 0 && JsonSerializer.Serialize(config) == before, "delivery acknowledgement is pure and deduplicates occurrence identity");
        var restored = JsonSerializer.Deserialize<EventConfig>(JsonSerializer.Serialize(delivered))!;
        check(CalendarEventRules.DueReminders(restored, now.AddMinutes(2)).Count == 0, "persisted reminder identities suppress repeat delivery after restart");
        var nextWeek = new DateTime(2026, 10, 12, 9, 5, 0);
        check(CalendarEventRules.DueReminders(restored, nextWeek).Single().Identity != due[0].Identity, "recurring future occurrence receives independent reminder identity");
        check(CalendarEventRules.DueReminders(config, now.AddHours(20), now.AddDays(-3)).Count == 1 && CalendarEventRules.DueReminders(config, now.AddHours(25), now.AddDays(-3)).Count == 0, "sleep catchup delivers only reminders from the recent 24 hours");
        var disabled = EventClone(config); disabled.Items[0].ReminderEnabled = false;
        check(CalendarEventRules.DueReminders(disabled, now).Count == 0, "disabled reminders do not deliver");
        var boundaryEvent = new CalendarEvent { Id = "boundary", Title = "lead crosses year", StartLocal = new(2027, 1, 1, 0, 5, 0), ReminderEnabled = true, ReminderLeadMinutes = 10 };
        check(CalendarEventRules.DueReminders(new EventConfig { Items = [boundaryEvent] }, new(2026, 12, 31, 23, 58, 0)).Count == 1, "reminder lead correctly crosses day and year boundaries");
        var invalid = new CalendarEvent { Title = "", ReminderLeadMinutes = -1 };
        check(CalendarEventRules.Validate(invalid) != null && CalendarEventRules.Occurrences(invalid, DateTime.Today, DateTime.Today.AddDays(7)).Count == 0, "invalid event fields are rejected instead of silently scheduled");

        var imported = HolidayRules.Import("""
            {"SchemaVersion":1,"Year":2026,"Source":"https://www.gov.cn/zhengce/","UpdatedLocal":"2026-01-01T00:00:00","Days":[{"Date":"2026-10-01T00:00:00","Name":"示例休息日","IsWorkday":false},{"Date":"2026-10-10T00:00:00","Name":"示例调休","IsWorkday":true}]}
            """);
        check(imported.Year == 2026 && HolidayRules.Status([imported], new(2026, 10, 10)).IsWorkday == true && HolidayRules.Status([imported], new(2026, 10, 1)).IsWorkday == false, "imported schedule distinguishes explicit holiday and adjusted workday");
        check(!HolidayRules.Status([imported], new(2027, 1, 1)).Available && HolidayRules.Status([imported], new(2027, 1, 1)).Text.Contains("未提供"), "missing official year data is explicitly unavailable");
        check(HolidayRules.Status([imported], new(2026, 10, 9)).IsWorkday == null, "unlisted date never guesses official workday status");
        bool rejected = false;
        try { HolidayRules.Import("{\"SchemaVersion\":9,\"Year\":2026}"); } catch (JsonException) { rejected = true; }
        check(rejected, "holiday import rejects future schema");
        var badYear = JsonSerializer.Deserialize<HolidayYear>(JsonSerializer.Serialize(imported))!; badYear.Days[0].Date = new(2027, 1, 1);
        check(HolidayRules.Validate(badYear) != null, "holiday import validates all dates belong to declared year");
        badYear = JsonSerializer.Deserialize<HolidayYear>(JsonSerializer.Serialize(imported))!; badYear.Source = "";
        check(HolidayRules.Validate(badYear) != null, "holiday data requires source metadata");
        badYear = JsonSerializer.Deserialize<HolidayYear>(JsonSerializer.Serialize(imported))!; badYear.Days.Add(badYear.Days[0]);
        check(HolidayRules.Validate(badYear) != null, "duplicate holiday dates are rejected");
        var temp = Path.Combine(Environment.CurrentDirectory, ".test-data", "events-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        var store = new ConfigManager(temp); store.Save("events.json", restored);
        check(CalendarEventRules.DueReminders(store.Load<EventConfig>("events.json").Value, now).Count == 0, "event file roundtrip retains reminder delivery deduplication");
    }
    private static EventConfig EventClone(EventConfig config) => JsonSerializer.Deserialize<EventConfig>(JsonSerializer.Serialize(config))!;
}
