// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Atalaia.Agent.Host.Settings;

/// <summary>Cifra e decifra bytes. Costura do cofre: no Windows é a DPAPI; nos testes, uma versão falsa.</summary>
internal interface ISecretProtector
{
    byte[] Protect(byte[] plain);
    /// <exception cref="CryptographicException">Dado que não é deste usuário (ou que foi alterado).</exception>
    byte[] Unprotect(byte[] protectedBytes);
}

/// <summary>
/// DPAPI do Windows, escopo do <b>usuário atual</b>: só o mesmo usuário, neste PC, decifra. A entropia extra amarra o arquivo ao Atalaia
/// (outro programa do mesmo usuário não o decifra por engano).
/// </summary>
internal sealed class DpapiProtector : ISecretProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Atalaia.Agent.SecretVault.v1");

    public byte[] Protect(byte[] plain) => ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
    public byte[] Unprotect(byte[] protectedBytes) => ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
}

/// <summary>
/// Os segredos que o usuário digita na janela (chave da Steam, <c>clientSecret</c> do Discord), em <c>%LOCALAPPDATA%\AtalaiaAgent\secrets.dat</c>:
/// um dicionário <c>"Plugins:steam:apiKey" → valor</c> inteiro cifrado com a DPAPI. Fica fora do <c>settings.json</c> e do repositório. O valor
/// nunca volta para a página nem vai para o log: quem usa só pergunta se existe (<see cref="Has"/>) e o agente os lê ao montar a configuração
/// (<c>HostConfig.Compose</c>), abaixo do <c>.env</c> e das variáveis <c>ATALAIA_*</c>, que continuam mandando.
/// </summary>
internal sealed class SecretVault(string path, ISecretProtector protector)
{
    private readonly object _lock = new();

    public static SecretVault Default { get; } = new(Path.Combine(AppPaths.Dir, "secrets.dat"), new DpapiProtector());

    public string FilePath => path;

    /// <summary>Uma cópia dos segredos. Arquivo ausente ou ilegível (outro usuário, outro PC, estragado) = vazio.</summary>
    public IReadOnlyDictionary<string, string> Read()
    {
        lock (_lock) return TryRead(out var all) ? all : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    public bool Has(string key) => Read().TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v);

    /// <summary>Lê, aplica a mudança e grava de uma vez (escrita atômica). Um valor <c>null</c> ou vazio remove a chave.</summary>
    public void Update(Action<Dictionary<string, string?>> change)
    {
        lock (_lock)
        {
            var current = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            if (TryRead(out var all)) foreach (var (k, v) in all) current[k] = v;
            change(current);
            Write(current.Where(p => !string.IsNullOrEmpty(p.Value)).ToDictionary(p => p.Key, p => p.Value!, StringComparer.OrdinalIgnoreCase));
        }
    }

    /// <summary>Volta o cofre para um conteúdo anterior (usado quando uma mudança de configuração não pôde ser aplicada).</summary>
    public void Replace(IReadOnlyDictionary<string, string> content)
    {
        lock (_lock) Write(content);
    }

    /// <summary>
    /// Se o arquivo existe mas não dá para decifrar, guarda-o ao lado (<c>secrets.dat.ilegivel-aaaammdd-hhmmss</c>) e segue com o cofre vazio:
    /// o usuário digita os segredos de novo, e o arquivo antigo não é perdido. Devolve onde ficou a cópia, ou <c>null</c> se estava bom.
    /// </summary>
    public string? QuarantineIfUnreadable()
    {
        lock (_lock)
        {
            if (!File.Exists(path) || TryRead(out _)) return null;
            var saved = $"{path}.ilegivel-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(path, saved, overwrite: true);
            return saved;
        }
    }

    private bool TryRead(out Dictionary<string, string> all)
    {
        all = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return true;
        try
        {
            var plain = protector.Unprotect(File.ReadAllBytes(path));
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(plain);
            if (parsed is null) return false;
            foreach (var (k, v) in parsed) all[k] = v;
            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException or NotSupportedException) { return false; }
    }

    private void Write(IReadOnlyDictionary<string, string> content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, protector.Protect(JsonSerializer.SerializeToUtf8Bytes(content)));
        if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null);
        else File.Move(tmp, path);
    }
}
