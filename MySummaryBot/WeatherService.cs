using System.Text.Json;
using System.Text.Json.Serialization;

public class WeatherService(HttpClient httpClient)
{
    const double Lat = 50.4501;   // Київ
    const double Lon = 30.5234;

    // Повертає погоду на найближчу дату з потрібним днем тижня + годиною. null якщо не вдалося.
    public async Task<SlotWeather?> TryGetSlotWeather(DayOfWeek day, int hour)
    {
        try
        {
            var today = DateTime.UtcNow.Date; // дату цільового дня рахуємо нижче від "сьогодні"
            // ВАЖЛИВО: цей метод викликають у п'ятницю ввечері (Київ). Беремо найближчий майбутній day.
            var diff = ((int)day - (int)today.DayOfWeek + 7) % 7;
            var target = today.AddDays(diff);
            var key = $"{target:yyyy-MM-dd}T{hour:D2}:00";

            var url = $"https://api.open-meteo.com/v1/forecast?latitude={Lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}&longitude={Lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}&hourly=temperature_2m,precipitation,weather_code&timezone=Europe%2FKyiv&forecast_days=7";
            var raw = await httpClient.GetStringAsync(url);
            var data = JsonSerializer.Deserialize<OpenMeteoResponse>(raw);
            var times = data?.Hourly?.Time;
            if (times == null) return null;
            var idx = Array.IndexOf(times, key);
            if (idx < 0) return null;
            return new SlotWeather(
                data!.Hourly!.Temperature![idx],
                data.Hourly.Precipitation![idx],
                data.Hourly.WeatherCode![idx]);
        }
        catch { return null; }
    }

    public static string Emoji(int code) => code switch
    {
        0 => "☀️",
        1 or 2 or 3 => "⛅",
        45 or 48 => "🌫",
        >= 51 and <= 67 => "🌧",
        >= 71 and <= 77 => "🌨",
        >= 80 and <= 82 => "🌧",
        85 or 86 => "🌨",
        >= 95 => "⛈",
        _ => "🌡"
    };

    public record SlotWeather(double TempC, double PrecipMm, int Code);

    class OpenMeteoResponse
    {
        [JsonPropertyName("hourly")] public HourlyData? Hourly { get; set; }
    }
    class HourlyData
    {
        [JsonPropertyName("time")] public string[]? Time { get; set; }
        [JsonPropertyName("temperature_2m")] public double[]? Temperature { get; set; }
        [JsonPropertyName("precipitation")] public double[]? Precipitation { get; set; }
        [JsonPropertyName("weather_code")] public int[]? WeatherCode { get; set; }
    }
}
