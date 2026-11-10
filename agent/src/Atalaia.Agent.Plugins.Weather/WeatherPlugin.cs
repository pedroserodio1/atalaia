using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Atalaia.Agent.Core;

namespace Atalaia.Agent.Plugins.Weather;

/// <summary>Clima atual pelo Open-Meteo (sem chave de API). Origem: <c>Weather</c> do PcProbe.</summary>
public sealed class WeatherPlugin : IPlugin
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private string _url = "";

    public string Id => "weather";
    public string Name => "Clima";
    public int DefaultIntervalMs => 600_000;
    public IReadOnlyList<ActionDescriptor> Actions { get; } = [new("refresh", "coleta de novo agora")];

    public Task StartAsync(IPluginContext ctx, CancellationToken ct)
    {
        if (!double.TryParse(ctx.Options["latitude"], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(ctx.Options["longitude"], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
            throw new InvalidOperationException("Configure Plugins:weather:latitude e longitude (números com ponto decimal).");
        _url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,relative_humidity_2m,wind_speed_10m,weather_code");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _http.Dispose();
        return Task.CompletedTask;
    }

    public async Task<JsonElement?> CollectAsync(CancellationToken ct) =>
        Parse(await _http.GetStringAsync(_url, ct), DateTimeOffset.Now);

    /// <summary><c>refresh</c> só responde ok: o agendador coleta de novo logo em seguida.</summary>
    public Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct) =>
        Task.FromResult(new ActionResult(action == "refresh", action == "refresh" ? null : "unknown_action"));

    /// <summary>Resposta do Open-Meteo (<c>current</c>) → <c>data</c> do protocolo.</summary>
    public static JsonElement Parse(string json, DateTimeOffset now)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("current", out var c)) throw new InvalidDataException("Resposta do Open-Meteo sem 'current'");
        var code = Num(c, "weather_code") is { } w ? (int)w : (int?)null;
        return Json.ToElement(new
        {
            tempC = Int(c, "temperature_2m"),
            humidityPercent = Int(c, "relative_humidity_2m"),
            windKmh = Int(c, "wind_speed_10m"),
            code,
            condition = code is { } k ? Condition(k) : "",
            updatedAt = Json.Iso(now),
        });
    }

    private static double? Num(JsonElement o, string name) =>
        o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) ? d : null;

    private static int? Int(JsonElement o, string name) => Num(o, name) is { } d ? (int)Math.Round(d) : null;

    /// <summary>Códigos WMO usados pelo Open-Meteo.</summary>
    public static string Condition(int code) => code switch
    {
        0 => "Céu limpo",
        1 => "Predominantemente limpo",
        2 => "Parcialmente nublado",
        3 => "Nublado",
        45 or 48 => "Neblina",
        51 or 53 or 55 => "Garoa",
        56 or 57 => "Garoa congelante",
        61 or 63 or 65 => "Chuva",
        66 or 67 => "Chuva congelante",
        71 or 73 or 75 => "Neve",
        77 => "Grãos de neve",
        80 or 81 or 82 => "Pancadas de chuva",
        85 or 86 => "Pancadas de neve",
        95 => "Trovoada",
        96 or 99 => "Trovoada com granizo",
        _ => "Desconhecido",
    };
}
