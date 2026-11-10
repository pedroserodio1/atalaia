// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

namespace Atalaia.Agent.Host.Updates;

/// <summary>
/// Baixa o instalador de uma release, confere o SHA-256 contra o <c>SHA256SUMS.txt</c> <b>da mesma release</b> e só então o executa.
/// <see cref="Launch"/> aceita apenas um <see cref="VerifiedFile"/> (que só o <see cref="Sha256Verifier"/> cria) e ainda reconfere o arquivo
/// logo antes de executar, para ninguém poder trocá-lo no intervalo. Falha em qualquer etapa <b>antes</b> de lançar o instalador: nada muda
/// e a versão instalada continua valendo. Sem assinatura de código (decisão do projeto): a integridade depende só da soma.
/// </summary>
internal sealed class UpdateApplier(GitHubReleaseClient client, string updatesDir, Action<string, string> launch)
{
    /// <summary>Instalação silenciosa, fechando o agente em execução; <c>/REABRIR=1</c> faz o <c>.iss</c> reabri-lo no fim.</summary>
    public const string InstallerArguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /REABRIR=1";

    private string DirFor(AtalaiaVersion v) => Path.Combine(Path.GetFullPath(updatesDir), v.ToString());   // v já é uma versão válida: sem ../

    /// <param name="phase">"download" ou "verify": em que etapa está (a tela mostra o texto de cada uma).</param>
    public async Task<VerifiedFile> DownloadAsync(UpdateCandidate candidate, IProgress<double>? progress, Action<string>? phase, CancellationToken ct)
    {
        var dir = DirFor(candidate.Version);
        RemoveOtherVersions(dir);
        Directory.CreateDirectory(dir);

        phase?.Invoke("download");
        var sumsPath = Path.Combine(dir, UpdateCandidate.SumsFile);
        await client.DownloadAsync(candidate.Sums, sumsPath, null, 1024 * 1024, ct);   // a lista de somas é pequena
        var sums = Sha256Verifier.ParseSums(await File.ReadAllTextAsync(sumsPath, ct));

        var name = UpdateCandidate.InstallerName(candidate.Version);
        var path = Path.Combine(dir, name);
        if (File.Exists(path))
        {
            try { return Sha256Verifier.Verify(path, name, sums); }   // já baixado antes e ainda confere
            catch (ChecksumException) { File.Delete(path); }
        }

        await client.DownloadAsync(candidate.Installer, path, progress, GitHubReleaseClient.DefaultMaxDownloadBytes, ct);
        phase?.Invoke("verify");
        try { return Sha256Verifier.Verify(path, name, sums); }
        catch (ChecksumException) { File.Delete(path); throw; }   // nunca deixa em disco um instalador que não conferiu
    }

    /// <summary>Executa o instalador (silencioso). Quem chama fecha o agente em seguida.</summary>
    public void Launch(VerifiedFile file, AtalaiaVersion version)
    {
        var expected = Path.Combine(DirFor(version), UpdateCandidate.InstallerName(version));
        if (!string.Equals(Path.GetFullPath(file.Path), expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Este arquivo não é o instalador baixado para esta versão. Nada foi executado.");
        if (!File.Exists(file.Path) || !string.Equals(Sha256Verifier.ComputeHex(file.Path), file.Sha256, StringComparison.Ordinal))
        {
            try { File.Delete(file.Path); } catch (IOException) { }
            throw new ChecksumException(ChecksumProblem.Mismatch, "O instalador mudou depois de conferido. Por segurança, ele não foi executado.");
        }
        launch(file.Path, InstallerArguments);
    }

    /// <summary>Instaladores de outras versões (de tentativas antigas) não ficam acumulando em disco.</summary>
    private void RemoveOtherVersions(string keep)
    {
        if (!Directory.Exists(updatesDir)) return;
        foreach (var dir in Directory.EnumerateDirectories(updatesDir))
        {
            if (string.Equals(Path.GetFullPath(dir), keep, StringComparison.OrdinalIgnoreCase)) continue;
            if (!AtalaiaVersion.TryParse(Path.GetFileName(dir), out _)) continue;   // só mexe nas pastas que são nossas
            try { Directory.Delete(dir, recursive: true); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }
}
