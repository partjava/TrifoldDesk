using System.Globalization;
using System.Text.Json;

namespace TrifoldDesk.Core;

public sealed class TodoTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public bool IsCompleted { get; set; }
    public bool IsArchived { get; set; }
}
public sealed class WorkbenchData
{
    public string View { get; set; } = "notes";
    public string Notes { get; set; } = "";
    public List<TodoTask> Tasks { get; set; } = [];
}
public sealed class ProductivityConfig
{
    public int SchemaVersion { get; set; } = 1;
    public Dictionary<string, WorkbenchData> Widgets { get; set; } = [];
}
public static class WorkbenchRules
{
    public static WorkbenchData AddTask(WorkbenchData state, string text, string? id = null)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 500) throw new ArgumentException("待办内容需要1到500字。", nameof(text));
        var next = Clone(state); next.Tasks ??= [];
        string taskId = id ?? Guid.NewGuid().ToString("N");
        if (string.IsNullOrWhiteSpace(taskId) || next.Tasks.Any(t => t != null && t.Id == taskId)) throw new ArgumentException("待办标识无效或重复。", nameof(id));
        next.Tasks.Add(new() { Id = taskId, Text = text.Trim() }); return next;
    }
    public static WorkbenchData SetCompleted(WorkbenchData state, string id, bool completed)
    {
        var next = Clone(state); var task = next.Tasks?.FirstOrDefault(t => t != null && t.Id == id); if (task != null) task.IsCompleted = completed; return next;
    }
    public static WorkbenchData SetArchived(WorkbenchData state, string id, bool archived)
    {
        var next = Clone(state); var task = next.Tasks?.FirstOrDefault(t => t != null && t.Id == id); if (task != null) task.IsArchived = archived; return next;
    }
    public static IReadOnlyList<TodoTask> VisibleTasks(WorkbenchData state, bool archived) => (Clone(state).Tasks ?? []).Where(t => t != null && t.IsArchived == archived).ToArray();
    public static WorkbenchData Clone(WorkbenchData state) => JsonSerializer.Deserialize<WorkbenchData>(JsonSerializer.Serialize(state))!;
}

public sealed class WeatherCity
{
    public string Name { get; set; } = "";
    public string Country { get; set; } = "";
    public string Region { get; set; } = "";
    public string Timezone { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public override string ToString() => string.Join(" · ", new[] { Name, Region, Country }.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct());
}
public sealed class WeatherObservation
{
    public DateTime DataLocal { get; set; }
    public DateTime RetrievedUtc { get; set; }
    public string Timezone { get; set; } = "";
    public double? TemperatureC { get; set; }
    public double? RelativeHumidity { get; set; }
    public double? WindKmh { get; set; }
    public int? WeatherCode { get; set; }
}
public sealed class WeatherData
{
    public WeatherCity? City { get; set; }
    public WeatherObservation? Cache { get; set; }
    public DateTime LastAttemptUtc { get; set; }
}
public sealed class WeatherConfig
{
    public int SchemaVersion { get; set; } = 1;
    public Dictionary<string, WeatherData> Widgets { get; set; } = [];
}
public static class WeatherRules
{
    public static bool ValidCity(WeatherCity? city) => city != null && !string.IsNullOrWhiteSpace(city.Name) && double.IsFinite(city.Latitude) && city.Latitude is >= -90 and <= 90 && double.IsFinite(city.Longitude) && city.Longitude is >= -180 and <= 180;
    public static bool NeedsRefresh(WeatherData state, DateTime nowUtc) => ValidCity(state.City) && (state.LastAttemptUtc == default || nowUtc - state.LastAttemptUtc >= TimeSpan.FromMinutes(30));
    public static WeatherData SelectCity(WeatherData state, WeatherCity city)
    {
        if (!ValidCity(city)) throw new ArgumentException("城市坐标无效。");
        var next = JsonSerializer.Deserialize<WeatherData>(JsonSerializer.Serialize(state))!;
        if (next.City?.Latitude != city.Latitude || next.City?.Longitude != city.Longitude) { next.Cache = null; next.LastAttemptUtc = default; }
        next.City = JsonSerializer.Deserialize<WeatherCity>(JsonSerializer.Serialize(city)); return next;
    }
    public static Uri ForecastUri(WeatherCity city)
    {
        if (!ValidCity(city)) throw new ArgumentException("城市坐标无效。");
        string coordinates = "latitude=" + city.Latitude.ToString("R", CultureInfo.InvariantCulture) + "&longitude=" + city.Longitude.ToString("R", CultureInfo.InvariantCulture);
        return new("https://api.open-meteo.com/v1/forecast?" + coordinates + "&current=temperature_2m,relative_humidity_2m,weather_code,wind_speed_10m&timezone=auto&forecast_days=1");
    }
    public static Uri SearchUri(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) throw new ArgumentException("请输入城市名称。");
        return new("https://geocoding-api.open-meteo.com/v1/search?name=" + Uri.EscapeDataString(query.Trim()) + "&count=5&language=zh&format=json");
    }
    public static IReadOnlyList<WeatherCity> ParseCities(string json)
    {
        using var doc = JsonDocument.Parse(json); var result = new List<WeatherCity>();
        if (!doc.RootElement.TryGetProperty("results", out var entries) || entries.ValueKind != JsonValueKind.Array) return result;
        foreach (var entry in entries.EnumerateArray())
        {
            double? latitude = Number(entry, "latitude"), longitude = Number(entry, "longitude");
            if (latitude == null || longitude == null) continue;
            var city = new WeatherCity { Name = Text(entry, "name"), Country = Text(entry, "country"), Region = Text(entry, "admin1"), Timezone = Text(entry, "timezone"), Latitude = latitude.Value, Longitude = longitude.Value };
            if (ValidCity(city)) result.Add(city);
        }
        return result;
    }
    public static WeatherObservation ParseForecast(string json, DateTime retrievedUtc)
    {
        using var doc = JsonDocument.Parse(json); var root = doc.RootElement;
        if (!root.TryGetProperty("current", out var current) || current.ValueKind != JsonValueKind.Object || !DateTime.TryParse(Text(current, "time"), CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)) throw new JsonException("天气响应缺少有效的数据时间。");
        double? code = Number(current, "weather_code");
        return new() { DataLocal = time, RetrievedUtc = retrievedUtc, Timezone = Text(root, "timezone"), TemperatureC = Number(current, "temperature_2m"), RelativeHumidity = Number(current, "relative_humidity_2m"), WindKmh = Number(current, "wind_speed_10m"), WeatherCode = code is >= 0 and <= 999 && code == Math.Truncate(code.Value) ? (int)code : null };
    }
    public static string Description(int? code) => code switch {
        0 => "晴", 1 => "大部晴朗", 2 => "多云", 3 => "阴", 45 or 48 => "雾", 51 or 53 or 55 => "毛毛雨", 56 or 57 or 66 or 67 => "冻雨",
        61 => "小雨", 63 => "中雨", 65 => "大雨", 71 or 73 or 75 or 77 => "雪", 80 or 81 or 82 => "阵雨", 85 or 86 => "阵雪", 95 or 97 => "雷暴", 96 or 99 => "雷暴冰雹", _ => "天气状况未知"
    };
    private static string Text(JsonElement value, string property) => value.TryGetProperty(property, out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() ?? "" : "";
    private static double? Number(JsonElement value, string property) => value.TryGetProperty(property, out var number) && number.ValueKind == JsonValueKind.Number && number.TryGetDouble(out double result) && double.IsFinite(result) ? result : null;
}

public sealed class ProjectBrowserData { public string RootPath { get; set; } = ""; }
public sealed class ProjectBrowserConfig
{
    public int SchemaVersion { get; set; } = 1;
    public Dictionary<string, ProjectBrowserData> Widgets { get; set; } = [];
}
public sealed record GitFileStatus(string Status, string Path, string? OriginalPath = null);
public static class GitStatusRules
{
    public static IReadOnlyList<GitFileStatus> ParsePorcelain(string text)
    {
        if (text.Length == 0) return [];
        if (!text.EndsWith('\0')) throw new FormatException("Git状态输出不完整。");
        var fields = text.Split('\0'); var result = new List<GitFileStatus>();
        for (int index = 0; index < fields.Length - 1; index++)
        {
            var field = fields[index];
            if (field.Length < 4 || field[2] != ' ') throw new FormatException("Git状态输出格式无效。");
            string? original = null;
            if (field[0] is 'R' or 'C' || field[1] is 'R' or 'C')
            {
                if (++index >= fields.Length - 1 || fields[index].Length == 0) throw new FormatException("Git重命名记录不完整。");
                original = fields[index];
            }
            result.Add(new(field[..2], field[3..], original));
        }
        return result;
    }
}
