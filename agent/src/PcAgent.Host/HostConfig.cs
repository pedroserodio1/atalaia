using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace PcAgent.Host;

internal static class AppPaths
{
    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcAgent");
    public static string Logs { get; } = Path.Combine(Dir, "logs");
}

internal static class HostConfig
{
    /// <summary>appsettings.json → appsettings.Local.json → .env → variáveis PCAGENT_*.</summary>
    public static IConfiguration Build()
    {
        LoadDotEnv();
        return new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddEnvironmentVariables("PCAGENT_")
            .Build();
    }

    /// <summary>
    /// Como o LoadEnv do PcProbe: variável já definida no ambiente tem prioridade. Só lê chaves PCAGENT_*,
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
            if (!key.StartsWith("PCAGENT_", StringComparison.Ordinal) || value.Length == 0) continue;
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
    /// <param name="Created"><c>true</c> se o código foi gerado agora (e por isso pode ser mostrado); <c>false</c> se veio de PCAGENT_TOKEN.</param>
    public sealed record Result(string Token, bool Created);

    /// <summary>
    /// PCAGENT_TOKEN fixa o token. Sem ele, o agente gera um <b>código de 6 dígitos novo a cada início</b>, só em memória: não
    /// grava arquivo, então o tablet precisa do código novo sempre que o agente reinicia.
    /// </summary>
    public static Result Load(IConfiguration config)
    {
        var fromEnv = config["TOKEN"]?.Trim();
        if (!string.IsNullOrEmpty(fromEnv)) return new Result(fromEnv, false);
        return new Result(NewCode(), true);
    }

    /// <summary>Seis dígitos sorteados com gerador criptográfico, com zeros à esquerda (ex.: <c>042917</c>).</summary>
    public static string NewCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);
}
