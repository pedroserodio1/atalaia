using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace PcAgent.Host;

internal static class AppPaths
{
    public static string Dir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PcAgent");
    public static string Logs { get; } = Path.Combine(Dir, "logs");
    public static string TokenFile { get; } = Path.Combine(Dir, "agent.json");
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

internal static class TokenStore
{
    public sealed record Result(string Token, bool Created);

    /// <summary>PCAGENT_TOKEN substitui o arquivo; sem ele, lê (ou gera na 1ª vez) %LOCALAPPDATA%\PcAgent\agent.json.</summary>
    public static Result Load(IConfiguration config)
    {
        var fromEnv = config["TOKEN"]?.Trim();
        if (!string.IsNullOrEmpty(fromEnv)) return new Result(fromEnv, false);

        if (File.Exists(AppPaths.TokenFile))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(AppPaths.TokenFile));
                var token = doc.RootElement.GetProperty("token").GetString();
                if (!string.IsNullOrWhiteSpace(token)) return new Result(token, false);
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException) { }
            throw new InvalidDataException($"O arquivo do token está inválido: {AppPaths.TokenFile}. Apague-o para gerar um novo (o tablet precisará do token novo).");
        }

        var bytes = RandomNumberGenerator.GetBytes(32);
        var created = Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');   // base64url
        Directory.CreateDirectory(AppPaths.Dir);
        File.WriteAllText(AppPaths.TokenFile, JsonSerializer.Serialize(new { token = created }));
        return new Result(created, true);
    }
}
