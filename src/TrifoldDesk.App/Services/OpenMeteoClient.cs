using System.Net.Http;

namespace TrifoldDesk.Services;

public sealed class OpenMeteoClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 1024 * 1024 };
    public async Task<IReadOnlyList<WeatherCity>> SearchAsync(string query, CancellationToken cancellationToken) =>
        WeatherRules.ParseCities(await Http.GetStringAsync(WeatherRules.SearchUri(query), cancellationToken));
    public async Task<WeatherObservation> ForecastAsync(WeatherCity city, CancellationToken cancellationToken) =>
        WeatherRules.ParseForecast(await Http.GetStringAsync(WeatherRules.ForecastUri(city), cancellationToken), DateTime.UtcNow);
}
