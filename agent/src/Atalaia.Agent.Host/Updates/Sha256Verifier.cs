// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Atalaia.Agent.Host.Updates;

/// <summary>
/// Um arquivo cuja soma SHA-256 foi conferida contra o <c>SHA256SUMS.txt</c> da mesma release. <b>Só o <see cref="Sha256Verifier"/> cria
/// esta classe</b>: quem instala ou executa algo baixado recebe um <see cref="VerifiedFile"/>, então o compilador impede usar um arquivo
/// que ninguém conferiu.
/// </summary>
internal sealed class VerifiedFile
{
    public string Path { get; }
    public string Sha256 { get; }
    internal VerifiedFile(string path, string sha256) { Path = path; Sha256 = sha256; }
}

internal enum ChecksumProblem
{
    /// <summary>O arquivo não aparece (ou aparece com somas diferentes) no <c>SHA256SUMS.txt</c>.</summary>
    NoSum,
    /// <summary>A soma do arquivo baixado não é a esperada: arquivo corrompido ou adulterado.</summary>
    Mismatch,
    FileMissing,
}

internal sealed class ChecksumException(ChecksumProblem problem, string message) : Exception(message)
{
    public ChecksumProblem Problem => problem;
}

internal static partial class Sha256Verifier
{
    [GeneratedRegex(@"^([0-9A-Fa-f]{64})[ \t]+\*?(.+?)\s*$")]
    private static partial Regex Line();

    /// <summary>
    /// <c>SHA256SUMS.txt</c> no formato do <c>sha256sum</c>: <c>&lt;64 hex&gt;  nome</c> (ou <c> *nome</c> em modo binário). Ignora linhas vazias,
    /// comentários e o que não é soma. Nome repetido com somas diferentes é ambíguo e fica de fora: nunca se confia em nenhuma das duas.
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseSums(string text)
    {
        var seen = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var m = Line().Match(line);
            if (!m.Success) continue;
            var name = m.Groups[2].Value;
            if (name.Contains('/') || name.Contains('\\')) continue;   // só nomes de arquivo, nunca caminho
            if (!seen.TryGetValue(name, out var set)) seen[name] = set = [];
            set.Add(m.Groups[1].Value.ToLowerInvariant());
        }
        return seen.Where(p => p.Value.Count == 1).ToDictionary(p => p.Key, p => p.Value.Single(), StringComparer.Ordinal);
    }

    public static string ComputeHex(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    /// <summary>Confere o arquivo contra a soma de <paramref name="name"/> em <paramref name="sums"/>. Falhou: lança, e quem chamou apaga o arquivo.</summary>
    public static VerifiedFile Verify(string filePath, string name, IReadOnlyDictionary<string, string> sums)
    {
        if (!File.Exists(filePath)) throw new ChecksumException(ChecksumProblem.FileMissing, "O arquivo baixado não foi encontrado.");
        if (!sums.TryGetValue(name, out var expected))
            throw new ChecksumException(ChecksumProblem.NoSum, $"O SHA256SUMS.txt da release não tem a soma de {name}: por segurança, o arquivo não será usado.");

        var actual = ComputeHex(filePath);
        // Comparação em tempo constante (o arquivo vem de fora; a soma esperada também).
        if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(expected)))
            throw new ChecksumException(ChecksumProblem.Mismatch, "A soma de verificação do arquivo não confere: ele está corrompido ou foi adulterado. Nada foi instalado.");
        return new VerifiedFile(filePath, actual);
    }
}
