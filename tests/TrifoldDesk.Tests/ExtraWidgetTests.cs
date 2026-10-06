using System.Globalization;
using System.Text.Json;
using TrifoldDesk.Core;

public static class ExtraWidgetTests
{
    public static void Run(Action<bool, string> check)
    {
        var work = new WorkbenchData { Notes = "中文便签" };
        var task = WorkbenchRules.AddTask(work, "  完成项目  ", "task-id");
        check(work.Tasks.Count == 0 && task.Tasks.Single().Text == "完成项目" && task.Notes == "中文便签", "task addition trims text and retains isolated notes");
        var completed = WorkbenchRules.SetCompleted(task, "task-id", true);
        var archived = WorkbenchRules.SetArchived(completed, "task-id", true);
        check(archived.Tasks.Single().IsCompleted && archived.Tasks.Single().IsArchived && WorkbenchRules.VisibleTasks(archived, false).Count == 0, "completed task archive preserves history without deleting content");
        check(WorkbenchRules.SetArchived(archived, "task-id", false).Tasks.Single().Id == "task-id" && !task.Tasks.Single().IsCompleted, "task recovery retains identity and pure updates never mutate source");
        bool rejected = false; try { WorkbenchRules.AddTask(work, " ", "invalid"); } catch (ArgumentException) { rejected = true; }
        check(rejected, "empty todo is rejected");
        var weather = new WeatherData();
        var now = new DateTime(2026, 10, 6, 0, 0, 0, DateTimeKind.Utc);
        check(!WeatherRules.NeedsRefresh(weather, now), "unconfigured weather never requests location or API");
        weather.City = new WeatherCity { Name = "北京", Latitude = 39.9, Longitude = 116.4 };
        check(WeatherRules.NeedsRefresh(weather, now), "explicitly selected city permits initial weather request");
        weather.LastAttemptUtc = now;
        check(!WeatherRules.NeedsRefresh(weather, now.AddMinutes(29)) && WeatherRules.NeedsRefresh(weather, now.AddMinutes(30)), "weather requests including failures are limited to once each thirty minutes");
        weather.Cache = new WeatherObservation { DataLocal = new(2026, 10, 6, 8, 0, 0), RetrievedUtc = now, TemperatureC = 21 };
        var movedCity = WeatherRules.SelectCity(weather, new() { Name = "上海", Latitude = 31.2, Longitude = 121.5 });
        check(movedCity.Cache == null && movedCity.LastAttemptUtc == default && weather.Cache != null, "changing selected city clears mismatched weather cache without altering source");
        var previousCulture = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR"); check(WeatherRules.ForecastUri(weather.City).Query.Contains("latitude=39.9") && !WeatherRules.ForecastUri(weather.City).Query.Contains("39,9"), "weather coordinate URL formatting is culture invariant"); }
        finally { CultureInfo.CurrentCulture = previousCulture; }
        var observation = WeatherRules.ParseForecast("""{"timezone":"Asia/Shanghai","current":{"time":"2026-10-06T08:00","temperature_2m":19.5,"relative_humidity_2m":null,"weather_code":63,"wind_speed_10m":6.4}}""", now);
        check(observation.TemperatureC == 19.5 && observation.RelativeHumidity == null && observation.DataLocal == new DateTime(2026, 10, 6, 8, 0, 0) && observation.Timezone == "Asia/Shanghai", "weather parser retains source timestamp and missing fields as unavailable");
        check(WeatherRules.Description(observation.WeatherCode) == "中雨" && WeatherRules.Description(999).Contains("未知"), "weather codes map documented conditions and unknown values stay explicit");
        var cities = WeatherRules.ParseCities("""{"results":[{"name":"北京","latitude":39.9,"longitude":116.4,"country":"中国","admin1":"北京","timezone":"Asia/Shanghai"},{"name":"invalid","latitude":999,"longitude":1}]}""");
        check(cities.Count == 1 && cities[0].Name == "北京" && WeatherRules.ParseCities("{}").Count == 0, "geocoding preserves city alternatives and excludes invalid coordinates");
        rejected = false; try { WeatherRules.ParseForecast("{\"current\":{\"time\":\"bad\"}}", now); } catch (JsonException) { rejected = true; }
        check(rejected, "malformed weather timestamps cannot replace last usable cache");
        var statuses = GitStatusRules.ParsePorcelain(" M 中文 文件.txt\0?? untracked/\0R  新 文件.txt\0旧 文件.txt\0");
        check(statuses.Count == 3 && statuses[0].Path == "中文 文件.txt" && statuses[2].Path == "新 文件.txt" && statuses[2].OriginalPath == "旧 文件.txt", "git porcelain parser preserves Unicode spaces and null-separated rename paths");
        check(GitStatusRules.ParsePorcelain("").Count == 0, "clean git repository has no synthetic changes");
        rejected = false; try { GitStatusRules.ParsePorcelain("R  new.txt\0"); } catch (FormatException) { rejected = true; }
        check(rejected, "truncated git rename output is rejected");
        var productivity = new ProductivityConfig { Widgets = new() { ["one"] = task, ["two"] = new() { Notes = "independent" } } };
        var roundtrip = JsonSerializer.Deserialize<ProductivityConfig>(JsonSerializer.Serialize(productivity))!;
        check(roundtrip.Widgets["one"].Tasks.Single().Id == "task-id" && roundtrip.Widgets["two"].Notes == "independent", "optional widget configuration persists per-instance task and note data");
    }
}
