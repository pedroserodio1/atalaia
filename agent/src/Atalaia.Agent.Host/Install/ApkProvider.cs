// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.IO.Compression;
using Atalaia.Agent.Host.Updates;
using QRCoder;

namespace Atalaia.Agent.Host.Install;

/// <summary>
/// O APK do tablet vem <b>da release da mesma versão do agente</b> (agente e app saem juntos), baixado do GitHub e conferido contra o
/// <c>SHA256SUMS.txt</c> da própria release antes de qualquer uso. Build de desenvolvimento (<c>-dev</c>) não tem release: nele o usuário
/// escolhe um APK local (<see cref="LocalApk"/>).
/// </summary>
internal sealed class ApkProvider(GitHubReleaseClient client, string downloadsDir, string appVersion)
{
    public const string SumsFile = "SHA256SUMS.txt";

    public string Tag => "v" + appVersion;
    public string ApkName => $"Atalaia-Tablet-{appVersion}.apk";

    /// <summary>Só versões publicadas têm release (<c>0.1.1</c>, <c>0.1.1-beta.2</c>); <c>-dev</c> e o que não é uma versão válida, não.</summary>
    public bool CanDownload => ReleaseNames.IsValidTag(Tag);

    /// <summary>Baixa o APK e a soma da release, confere, e devolve o arquivo verificado. Reaproveita um APK já baixado que ainda confere.</summary>
    public async Task<VerifiedFile> DownloadAsync(IProgress<double>? progress, CancellationToken ct)
    {
        if (!CanDownload) throw new GitHubException(GitHubFailure.NotFound, "Esta versão do agente não tem release publicada.");

        var release = await client.GetByTagAsync(Tag, ct);
        if (release.Draft) throw new GitHubException(GitHubFailure.NotFound, "Essa release ainda não foi publicada.");
        var apk = release.Find(ApkName) ?? throw new GitHubException(GitHubFailure.NotFound, $"A release {Tag} não tem o arquivo {ApkName}.");
        var sumsAsset = release.Find(SumsFile) ?? throw new GitHubException(GitHubFailure.NotFound, $"A release {Tag} não tem o {SumsFile}: sem ele não dá para conferir o APK.");

        var dir = Path.Combine(downloadsDir, appVersion);   // appVersion já foi validada como versão: sem ../
        Directory.CreateDirectory(dir);
        var sumsPath = Path.Combine(dir, SumsFile);
        await client.DownloadAsync(sumsAsset, sumsPath, null, 1024 * 1024, ct);   // a lista de somas é pequena
        var sums = Sha256Verifier.ParseSums(await File.ReadAllTextAsync(sumsPath, ct));

        var apkPath = Path.Combine(dir, ApkName);
        if (File.Exists(apkPath))
        {
            try { return Sha256Verifier.Verify(apkPath, ApkName, sums); }
            catch (ChecksumException) { File.Delete(apkPath); }   // o de antes está ruim: baixa de novo
        }

        await client.DownloadAsync(apk, apkPath, progress, GitHubReleaseClient.DefaultMaxDownloadBytes, ct);
        try { return Sha256Verifier.Verify(apkPath, ApkName, sums); }
        catch (ChecksumException) { File.Delete(apkPath); throw; }   // nunca deixa um arquivo não conferido em disco
    }
}

/// <summary>
/// Um APK que pode ser instalado: ou foi conferido contra a soma da release (<see cref="VerifiedFile"/>) ou foi escolhido à mão pelo
/// usuário e passou em <see cref="LocalApk.Validate"/>. Não existe outro jeito de criar um, então a instalação nunca recebe um caminho solto.
/// </summary>
internal sealed class ApkFile
{
    public string Path { get; }
    public bool Verified { get; }
    private ApkFile(string path, bool verified) { Path = path; Verified = verified; }

    public static ApkFile From(VerifiedFile file) => new(file.Path, true);

    /// <summary>Um APK escolhido pelo usuário (sem soma para conferir). Lança se não parece um APK.</summary>
    public static ApkFile FromUserChoice(string path) =>
        LocalApk.Validate(path) is { } problem ? throw new InvalidOperationException(problem) : new ApkFile(System.IO.Path.GetFullPath(path), false);
}

/// <summary>Um APK que o usuário escolheu à mão (build de desenvolvimento). Não tem soma para conferir: só se checa que parece um APK.</summary>
internal static class LocalApk
{
    private const long MaxBytes = 200L * 1024 * 1024;

    /// <returns><c>null</c> se o arquivo parece um APK; senão o motivo, em português.</returns>
    public static string? Validate(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return "O arquivo não existe.";
        if (!path.EndsWith(".apk", StringComparison.OrdinalIgnoreCase)) return "Escolha um arquivo .apk.";
        var length = new FileInfo(path).Length;
        if (length == 0 || length > MaxBytes) return "O tamanho do arquivo não é o de um APK do Atalaia.";
        try
        {
            using var zip = ZipFile.OpenRead(path);
            return zip.GetEntry("AndroidManifest.xml") is null ? "O arquivo não parece um APK (sem AndroidManifest.xml)." : null;
        }
        catch (InvalidDataException) { return "O arquivo não é um APK válido."; }
    }
}

/// <summary>O link da release que vai no QR (para instalar no tablet sem cabo) e o QR como imagem.</summary>
internal static class ReleaseLink
{
    /// <summary>A página da release desta versão; build de desenvolvimento aponta para a lista de releases.</summary>
    public static string Page(string repository, string appVersion)
    {
        var tag = "v" + appVersion;
        return ReleaseNames.IsValidTag(tag) ? $"https://github.com/{repository}/releases/tag/{tag}" : $"https://github.com/{repository}/releases";
    }

    /// <summary>O QR como imagem SVG em <c>data:</c> (escuro sobre branco, com margem: é o que as câmeras leem melhor).</summary>
    public static string QrDataUri(string text)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.M);
        var svg = new SvgQRCode(data).GetGraphic(8, "#0B0D10", "#FFFFFF", drawQuietZones: true);
        return "data:image/svg+xml;base64," + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(svg));
    }
}
