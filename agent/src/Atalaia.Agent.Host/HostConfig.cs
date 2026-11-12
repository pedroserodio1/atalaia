// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Atalaia.Agent.Host.Settings;

namespace Atalaia.Agent.Host;

internal static class AppPaths
{
    /// <summary>
    /// Onde o agente guarda logs, <c>settings.json</c>, downloads e os dados do WebView2: <c>%LOCALAPPDATA%\AtalaiaAgent</c>, ou a pasta de
    /// <c>ATALAIA_DATA_DIR</c> (caminho completo), para testar sem mexer na configuração de verdade (VM, outro usuário, pasta descartável).
    /// </summary>
    public static string Dir { get; } = Resolve(Environment.GetEnvironmentVariable("ATALAIA_DATA_DIR"),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    internal static string Resolve(string? overrideDir, string localAppData) =>
        !string.IsNullOrWhiteSpace(overrideDir) && Path.IsPathFullyQualified(overrideDir.Trim())
            ? Path.GetFullPath(overrideDir.Trim())
            : Path.Combine(localAppData, "AtalaiaAgent");
    public static string Logs { get; } = Path.Combine(Dir, "logs");
    /// <summary>Pasta de dados do WebView2 (cache, cookies). Fica fora da pasta de instalação, que não é gravável.</summary>
    public static string WebView2Data { get; } = Path.Combine(Dir, "webview2");
}

internal static class HostConfig
{
    /// <summary>appsettings.json → appsettings.Local.json → settings.json (a janela do agente) → cofre de segredos → .env → variáveis ATALAIA_*.</summary>
    public static IConfiguration Build()
    {
        LoadDotEnv();
        SettingsQuarantined = SettingsStore.Default.QuarantineIfInvalid();   // settings.json estragado não pode impedir o agente de subir
        SecretsQuarantined = SecretVault.Default.QuarantineIfUnreadable();   // idem o cofre: ilegível não impede de subir
        return Compose(AppContext.BaseDirectory, SettingsStore.Default, SecretVault.Default);
    }

    /// <summary>Se o <c>settings.json</c> estava inválido na partida, onde a cópia dele foi guardada (o <c>Program</c> avisa no log).</summary>
    public static string? SettingsQuarantined { get; private set; }

    /// <summary>Se o cofre de segredos não pôde ser decifrado na partida, onde a cópia dele foi guardada (o <c>Program</c> avisa no log, sem conteúdo).</summary>
    public static string? SecretsQuarantined { get; private set; }

    /// <summary>
    /// A ordem de carga, sem o <c>.env</c> (que mexe nas variáveis do processo). O <c>.env</c> entra em <see cref="Build"/> por variável
    /// de ambiente, então vale a mesma precedência: a variável vence o <c>settings.json</c> e o cofre de segredos.
    /// </summary>
    internal static IConfiguration Compose(string baseDirectory, SettingsStore settings, SecretVault? vault = null)
    {
        var settingsDir = Path.GetDirectoryName(settings.FilePath)!;
        Directory.CreateDirectory(settingsDir);   // o provedor de arquivos exige que a pasta exista
        var builder = new ConfigurationBuilder()
            .SetBasePath(baseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddJsonFile(new PhysicalFileProvider(settingsDir), Path.GetFileName(settings.FilePath), optional: true, reloadOnChange: false);
        // Os segredos entram só na memória do processo (chave "Plugins:steam:apiKey" → valor), acima do settings.json e abaixo do ambiente.
        if (vault is not null) builder.AddInMemoryCollection(vault.Read().Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)));
        return builder.AddEnvironmentVariables("ATALAIA_").Build();
    }

    /// <summary>
    /// Como o LoadEnv do PcProbe: variável já definida no ambiente tem prioridade. Só lê chaves ATALAIA_*,
    /// para um .env alheio, encontrado subindo as pastas, não vazar outras variáveis para o processo.
    /// </summary>
    private static void LoadDotEnv()
    {
        var path = FindDotEnv();
        if (path is null) return;
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var i = line.IndexOf('=');
            if (i <= 0) continue;
            var key = line[..i].Trim();
            var value = line[(i + 1)..].Trim().Trim('"');
            if (!key.StartsWith("ATALAIA_", StringComparison.Ordinal) || value.Length == 0) continue;
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static string? FindDotEnv()
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            for (var d = new DirectoryInfo(start); d is not null; d = d.Parent)
            {
                var candidate = Path.Combine(d.FullName, ".env");
                if (File.Exists(candidate)) return candidate;
            }
        return null;
    }
}

public static class TokenStore
{
    /// <param name="Created"><c>true</c> se o código foi gerado agora (e por isso pode ser mostrado); <c>false</c> se veio de ATALAIA_TOKEN.</param>
    public sealed record Result(string Token, bool Created);

    /// <summary>Chave (no <c>settings.json</c>) da opção "manter o mesmo código entre reinícios".</summary>
    internal const string KeepKey = "Access:KeepCode";
    /// <summary>Chave (no cofre, DPAPI) do código mantido.</summary>
    internal const string SavedKey = "Access:Code";

    /// <summary>
    /// ATALAIA_TOKEN fixa o token. Com "manter o código" ligado, o código de 6 dígitos vem do cofre (e é sorteado e guardado se ainda não
    /// existe ou está estragado). Sem nenhum dos dois, gera um <b>código novo a cada início</b>, só em memória.
    /// </summary>
    internal static Result Load(IConfiguration config, SecretVault? vault = null)
    {
        var fromEnv = config["TOKEN"]?.Trim();
        if (!string.IsNullOrEmpty(fromEnv)) return new Result(fromEnv, false);
        if (!config.GetValue(KeepKey, false)) return new Result(NewCode(), true);
        var saved = config[SavedKey]?.Trim();
        if (IsCode(saved)) return new Result(saved!, true);
        var code = NewCode();
        vault?.Update(d => d[SavedKey] = code);
        return new Result(code, true);
    }

    internal static bool IsCode(string? s) => s is { Length: 6 } && s.All(char.IsAsciiDigit);

    /// <summary>Seis dígitos sorteados com gerador criptográfico, com zeros à esquerda (ex.: <c>042917</c>).</summary>
    public static string NewCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
}
