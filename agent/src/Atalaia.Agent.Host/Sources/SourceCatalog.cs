// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Globalization;

namespace Atalaia.Agent.Host.Sources;

/// <summary>Uma fonte de dados (plugin) como a janela a descreve.</summary>
internal sealed record SourceInfo(string Id, string Name, string Description);

/// <summary>
/// O que o ambiente tem, para saber se uma fonte está pronta para funcionar. Tudo por costuras (arquivos, variáveis, pastas), então o
/// teste não depende do seu Windows.
/// </summary>
internal sealed record SourceProbe(
    Func<string, string?> Option,            // opção da fonte: Option("latitude") lê Plugins:<id>:latitude
    Func<string, bool> FileExists,
    Func<string, bool> DirectoryExists,
    Func<string, string?> Env,
    string AppData,                          // %APPDATA%
    string UserHome);                        // %USERPROFILE%

/// <summary>
/// As onze fontes que o agente publica, na ordem do registro, com a descrição para o usuário e o que falta configurar. A lista é a do
/// <c>PluginRegistry</c> (um teste confere que são as mesmas): fonte nova no agente entra aqui também.
/// </summary>
internal static class SourceCatalog
{
    public static readonly IReadOnlyList<SourceInfo> All =
    [
        new("system", "Sistema", "CPU, RAM, disco e rede"),
        new("gpu", "GPU", "temperatura e uso, via nvidia-smi"),
        new("media", "Mídia", "música tocando e capa"),
        new("audio", "Áudio", "volume geral e por aplicativo"),
        new("activeWindow", "Janela ativa", "jogo ou programa em foco"),
        new("ups", "Nobreak", "nobreak Intelbras, lido do banco do software dele"),
        new("weather", "Clima", "temperatura e condição, via Open-Meteo"),
        new("notifications", "Notificações", "avisos do Windows: só o nome do app e a contagem"),
        new("claude", "Claude", "limites de sessão e semanais"),
        new("steam", "Steam", "jogo atual, biblioteca e amigos"),
        new("discord", "Discord", "chamada de voz"),
    ];

    /// <summary>O que falta para a fonte funcionar, em português (vazio = pronta). Nunca devolve o valor de uma chave, só diz que falta.</summary>
    public static IReadOnlyList<string> Missing(string id, SourceProbe p)
    {
        switch (id)
        {
            case "gpu":
                return NvidiaSmiFound(p) ? [] : ["O nvidia-smi não foi encontrado (a fonte só funciona com placa NVIDIA). Se ele está em outro lugar, informe o caminho em Plugins:gpu:nvidiaSmiPath."];
            case "ups":
                {
                    var path = Blank(p.Option("dbPath")) ?? Path.Combine(p.AppData, "Intelbras", "ups_desktop", "intelbras_nobreak_management_service.db");
                    return p.FileExists(path) ? [] : ["O banco do software do nobreak não foi encontrado no lugar padrão. Informe o caminho em Plugins:ups:dbPath."];
                }
            case "weather":
                return Coordinate(p.Option("latitude")) && Coordinate(p.Option("longitude"))
                    ? [] : ["Faltam a latitude e a longitude do seu local (Plugins:weather:latitude e Plugins:weather:longitude, em graus decimais com ponto)."];
            case "claude":
                {
                    var dir = Blank(p.Option("configDir")) ?? Blank(p.Env("CLAUDE_CONFIG_DIR")) ?? Path.Combine(p.UserHome, ".claude");
                    return p.DirectoryExists(dir) ? [] : ["A pasta do Claude Code não foi encontrada. Se ela está em outro lugar, informe em Plugins:claude:configDir."];
                }
            case "steam":
                return Blank(p.Option("apiKey")) is not null && Blank(p.Option("steamId64")) is not null
                    ? [] : ["Faltam a chave da API e o SteamID64: defina as variáveis de ambiente ATALAIA_Plugins__steam__apiKey e ATALAIA_Plugins__steam__steamId64 (chaves nunca ficam em arquivo de configuração)."];
            case "discord":
                return Blank(p.Option("clientId")) is not null && Blank(p.Option("clientSecret")) is not null
                    ? [] : ["Faltam o clientId e o clientSecret do aplicativo do Discord: defina as variáveis de ambiente ATALAIA_Plugins__discord__clientId e ATALAIA_Plugins__discord__clientSecret."];
            default:
                return [];
        }
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static bool Coordinate(string? s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v);

    private static bool NvidiaSmiFound(SourceProbe p)
    {
        var configured = Blank(p.Option("nvidiaSmiPath"));
        if (configured is not null && (configured.Contains('\\') || configured.Contains('/'))) return p.FileExists(configured);
        var name = configured ?? "nvidia-smi";
        if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
        foreach (var dir in (p.Env("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (p.FileExists(Path.Combine(dir.Trim('"'), name))) return true;
        return false;
    }
}
