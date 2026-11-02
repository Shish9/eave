using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace ClaudeIsland;

/// Current weather from Open-Meteo (free, no account). Opt-in: only runs once a city is set in Settings.
/// Only the city name (for the lookup) and its coordinates (for the forecast) leave the PC.
public class WeatherService
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    public bool Available;
    public double Temp;
    public int Code;
    public bool IsDay = true;
    public string City = "";
    /// Why the last lookup failed, for Eave Studio ("" when it worked or hasn't run yet).
    public string Error = "";
    public event Action? Changed; // raised on a background thread

    DateTime lastFetch = DateTime.MinValue;
    bool busy;

    public void Invalidate() { lastFetch = DateTime.MinValue; Available = false; }

    /// Called about once a second from the UI; fetches at most every 20 minutes (retries after a minute on failure).
    public void Tick()
    {
        var s = AppSettings.Current;
        if (!s.ShowWeather || string.IsNullOrWhiteSpace(s.WeatherCity))
        {
            if (Available) { Available = false; Changed?.Invoke(); }
            return;
        }
        if (busy) return;
        double age = (DateTime.Now - lastFetch).TotalMinutes;
        if (Available ? age < 20 : age < 1) return;
        _ = Fetch();
    }

    async Task Fetch()
    {
        busy = true;
        lastFetch = DateTime.Now;
        try
        {
            var s = AppSettings.Current;
            if (s.WeatherLat == null || s.WeatherLon == null || s.WeatherResolvedFor != s.WeatherCity)
            {
                string url = "https://geocoding-api.open-meteo.com/v1/search?count=1&name=" + Uri.EscapeDataString(s.WeatherCity);
                using var geo = JsonDocument.Parse(await Http.GetStringAsync(url));
                if (!geo.RootElement.TryGetProperty("results", out var results) || results.GetArrayLength() == 0)
                    throw new InvalidOperationException($"Couldn't find \"{s.WeatherCity}\". Try the city name in English, e.g. \"Riyadh\".");
                var hit = results[0];
                s.WeatherLat = hit.GetProperty("latitude").GetDouble();
                s.WeatherLon = hit.GetProperty("longitude").GetDouble();
                s.WeatherName = hit.GetProperty("name").GetString() ?? s.WeatherCity;
                s.WeatherResolvedFor = s.WeatherCity;
                AppSettings.Save();
            }

            string unit = s.Fahrenheit ? "fahrenheit" : "celsius";
            string forecast = string.Format(CultureInfo.InvariantCulture,
                "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,weather_code,is_day&temperature_unit={2}",
                s.WeatherLat, s.WeatherLon, unit);
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(forecast));
            var cur = doc.RootElement.GetProperty("current");
            Temp = cur.GetProperty("temperature_2m").GetDouble();
            Code = cur.GetProperty("weather_code").GetInt32();
            IsDay = cur.GetProperty("is_day").GetInt32() == 1;
            City = s.WeatherName;
            Available = true;
            Error = "";
        }
        catch (InvalidOperationException ex) { Available = false; Error = ex.Message; }
        catch (HttpRequestException) { Available = false; Error = "Can't reach open-meteo.com. Check your internet connection; Eave retries every minute."; }
        catch (TaskCanceledException) { Available = false; Error = "The weather service took too long to answer; Eave retries every minute."; }
        catch (Exception ex) { Available = false; Error = "Weather failed: " + ex.Message; }
        finally { busy = false; }
        Changed?.Invoke();
    }

    public string Icon => Code switch
    {
        0 => IsDay ? "☀" : "☾",
        1 or 2 => IsDay ? "⛅" : "☁",
        3 or 45 or 48 => "☁",
        >= 51 and <= 67 or >= 80 and <= 82 => "☂",
        >= 71 and <= 77 or 85 or 86 => "❄",
        >= 95 => "⚡",
        _ => "☁",
    };

    public string Description => Code switch
    {
        0 => IsDay ? "Clear" : "Clear night",
        1 => "Mostly clear",
        2 => "Partly cloudy",
        3 => "Overcast",
        45 or 48 => "Fog",
        >= 51 and <= 57 => "Drizzle",
        >= 61 and <= 67 => "Rain",
        >= 71 and <= 77 => "Snow",
        >= 80 and <= 82 => "Showers",
        85 or 86 => "Snow showers",
        >= 95 => "Thunderstorm",
        _ => "Cloudy",
    };

    public string Short => $"{Icon} {Math.Round(Temp)}°";
    public string Long => $"{Icon} {Description} · {Math.Round(Temp)}°{(AppSettings.Current.Fahrenheit ? "F" : "C")} · {City}";
}
