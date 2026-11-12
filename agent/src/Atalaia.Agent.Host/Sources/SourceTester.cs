// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Globalization;
using System.Text.Json;
using Atalaia.Agent.Plugins.Weather;

namespace Atalaia.Agent.Host.Sources;

/// <param name="Ok"><c>true</c> = a fonte deve funcionar com esses valores. O texto sempre diz a causa, em português.</param>
internal sealed record SourceTestResult(bool Ok, string Message);

/// <summary>
/// "Testar conexão": valida a credencial na hora, sem ligar a fonte nem gravar nada, e devolve a causa em português.
/// Só lê: uma chamada de rede curta por fonte. <b>Nunca</b> põe no texto o valor digitado, a URL (a chave da Steam vai na URL) nem o
/// texto de uma exceção. O <see cref="HttpClient"/> e as leituras do sistema entram por costuras, e o teste troca todas.
/// </summary>
internal sealed class SourceTester(HttpClient http, Func<string, SourceProbe> probe, Func<bool> discordIsOpen, Func<DateTimeOffset> now)
{
    /// <summary>O prazo de cada chamada. Menor que o da página (15 s), senão ela desiste antes do agente.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    /// <summary>As fontes que têm teste.</summary>
    public static bool CanTest(string sourceId) => sourceId is "steam" or "discord" or "weather" or "ups";

    /// <summary>O Discord para computador abre o pipe <c>discord-ipc-N</c> quando está de pé. Só lista os nomes dos pipes, não conecta em nenhum.</summary>
    public static bool DiscordIsOpen()
    {
        try { return Directory.GetFiles(@"\\.\pipe\").Any(p => Path.GetFileName(p).StartsWith("discord-ipc-", StringComparison.Ordinal)); }
        catch (IOException) { return false; }
    }

    private const string NoNetwork = "Não consegui falar com o serviço (sem internet, ou ele está fora do ar). Tente de novo em instantes.";

    /// <param name="values">Nome do campo → valor já conferido (<see cref="SourceCredentials"/>). Vazio para a fonte sem campos.</param>
    public async Task<SourceTestResult> RunAsync(string sourceId, IReadOnlyDictionary<string, string> values, CancellationToken ct)
    {
        try
        {
            return sourceId switch
            {
                "steam" => await SteamAsync(values["apiKey"], values["steamId64"], ct),
                "weather" => await WeatherAsync(values["latitude"], values["longitude"], ct),
                "discord" => await DiscordAsync(values["clientId"], values["clientSecret"], ct),
                "ups" => Ups(),
                _ => new SourceTestResult(false, "Esta fonte não tem teste."),
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException or JsonException or InvalidDataException)
        {
            return new SourceTestResult(false, NoNetwork);   // o texto da exceção pode carregar a URL (com a chave): nunca vai para a tela
        }
    }

    private async Task<SourceTestResult> SteamAsync(string apiKey, string steamId, CancellationToken ct)
    {
        var url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={Uri.EscapeDataString(apiKey)}&steamids={Uri.EscapeDataString(steamId)}";
        using var res = await http.GetAsync(url, ct);
        var code = (int)res.StatusCode;
        if (code is 401 or 403) return new SourceTestResult(false, "A Steam recusou a chave. Confira se copiou inteira, sem espaços, e se a conta pode gerar chaves.");
        if (!res.IsSuccessStatusCode) return new SourceTestResult(false, $"A Steam respondeu com erro ({code}). Tente de novo mais tarde.");

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("response", out var r) || !r.TryGetProperty("players", out var players) || players.GetArrayLength() == 0)
            return new SourceTestResult(false, "A chave foi aceita, mas esse SteamID64 não existe. Confira os 17 dígitos.");

        var visibility = players[0].TryGetProperty("communityvisibilitystate", out var v) && v.TryGetInt32(out var n) ? n : 0;
        return visibility == 3
            ? new SourceTestResult(true, "Tudo certo: a chave foi aceita e o perfil foi encontrado.")
            : new SourceTestResult(true, "A chave e o SteamID64 estão certos, mas o perfil não é público. Deixe Detalhes do jogo como Público para ver biblioteca, jogos recentes e amigos.");
    }

    private async Task<SourceTestResult> WeatherAsync(string lat, string lon, CancellationToken ct)
    {
        var url = string.Create(CultureInfo.InvariantCulture,
            $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,relative_humidity_2m,wind_speed_10m,weather_code");
        using var res = await http.GetAsync(url, ct);
        if (!res.IsSuccessStatusCode)
            return new SourceTestResult(false, (int)res.StatusCode == 400
                ? "O Open-Meteo recusou essas coordenadas. Confira a latitude e a longitude (com ponto, não vírgula)."
                : $"O Open-Meteo respondeu com erro ({(int)res.StatusCode}). Tente de novo mais tarde.");

        var data = WeatherPlugin.Parse(await res.Content.ReadAsStringAsync(ct), now());   // lança InvalidDataException se a resposta não tem "current"
        return new SourceTestResult(true, data.TryGetProperty("tempC", out var t) && t.ValueKind == JsonValueKind.Number
            ? $"Tudo certo: o Open-Meteo respondeu para esse local ({t.GetInt32()} °C agora)."
            : "Tudo certo: o Open-Meteo respondeu para esse local.");
    }

    /// <summary>
    /// O teste leve: o Discord para computador está aberto? E o aplicativo aceita o Client ID e o Client Secret? Não pede a autorização (que abre um
    /// pop-up no Discord) e não mexe no que a fonte guardou.
    /// </summary>
    private async Task<SourceTestResult> DiscordAsync(string clientId, string clientSecret, CancellationToken ct)
    {
        if (!discordIsOpen())
            return new SourceTestResult(false, "O Discord para computador está fechado (o navegador não vale). Abra-o e teste de novo.");

        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials", ["scope"] = "identify", ["client_id"] = clientId, ["client_secret"] = clientSecret,
        });
        using var res = await http.PostAsync("https://discord.com/api/oauth2/token", body, ct);
        var code = (int)res.StatusCode;
        if (res.IsSuccessStatusCode)
            return new SourceTestResult(true, "Tudo certo: o Discord está aberto e o Client ID e o Client Secret foram aceitos. A autorização só é pedida quando a fonte liga.");
        if (code is 400 or 401)
            return new SourceTestResult(false, "O Discord recusou o Client ID ou o Client Secret. Confira o Client ID e gere o segredo de novo com Reset Secret.");
        return new SourceTestResult(false, $"O Discord respondeu com erro ({code}). Tente de novo mais tarde.");
    }

    private SourceTestResult Ups()
    {
        var missing = SourceCatalog.Missing("ups", probe("ups"));
        return missing.Count == 0
            ? new SourceTestResult(true, "Tudo certo: o banco do software do nobreak foi encontrado.")
            : new SourceTestResult(false, missing[0]);
    }
}
