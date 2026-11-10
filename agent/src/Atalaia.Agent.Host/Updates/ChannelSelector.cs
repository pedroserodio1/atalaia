// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

namespace Atalaia.Agent.Host.Updates;

internal enum UpdateChannel { Stable, Beta }

internal static class UpdateChannels
{
    public static string Name(UpdateChannel c) => c == UpdateChannel.Beta ? "beta" : "stable";

    public static UpdateChannel? Parse(string? text) => text switch { "stable" => UpdateChannel.Stable, "beta" => UpdateChannel.Beta, _ => null };

    /// <summary>Sem escolha do usuário: quem instalou um beta continua no canal Beta; o resto, no Estável.</summary>
    public static UpdateChannel DefaultFor(AtalaiaVersion installed) => installed.IsBeta ? UpdateChannel.Beta : UpdateChannel.Stable;
}

/// <summary>Uma release que dá para instalar: versão válida, com o instalador e o <c>SHA256SUMS.txt</c> dela.</summary>
internal sealed record UpdateCandidate(AtalaiaVersion Version, ReleaseInfo Release, ReleaseAsset Installer, ReleaseAsset Sums)
{
    public const string SumsFile = "SHA256SUMS.txt";
    public static string InstallerName(AtalaiaVersion v) => $"Atalaia-Agent-Setup-{v}.exe";
}

/// <summary>
/// Qual release oferecer (regras em <c>docs/versionamento-e-releases.md</c>, seção 7): <b>Estável</b> = não rascunho e não pré-lançamento;
/// <b>Beta</b> = não rascunho (inclui pré-lançamento). Escolhe a maior versão pela precedência do SemVer e só se for <b>estritamente maior</b>
/// que a instalada: nunca há downgrade, e quem está no <c>0.1.1-beta.2</c> recebe a <c>0.1.1</c>.
/// </summary>
internal static class ChannelSelector
{
    /// <param name="skipped">"Lembrar depois": a versão que o usuário dispensou (não é oferecida de novo; uma mais nova é).</param>
    public static UpdateCandidate? Select(IEnumerable<ReleaseInfo> releases, UpdateChannel channel, AtalaiaVersion installed, string? skipped = null)
    {
        UpdateCandidate? best = null;
        foreach (var release in releases)
        {
            if (Eligible(release, channel) is not { } candidate) continue;
            if (candidate.Version <= installed) continue;
            if (skipped is not null && candidate.Version.ToString() == skipped) continue;
            if (best is null || candidate.Version > best.Version) best = candidate;
        }
        return best;
    }

    /// <summary>A mesma release, ignorando o "lembrar depois": serve para saber se há algo que o usuário dispensou.</summary>
    public static UpdateCandidate? SelectIgnoringSkip(IEnumerable<ReleaseInfo> releases, UpdateChannel channel, AtalaiaVersion installed) =>
        Select(releases, channel, installed, skipped: null);

    private static UpdateCandidate? Eligible(ReleaseInfo release, UpdateChannel channel)
    {
        if (release.Draft) return null;
        if (!AtalaiaVersion.TryParseTag(release.Tag, out var version)) return null;   // tag fora do padrão: ignorada
        // Estável: nem a marca de pré-lançamento nem o sufixo -beta (uma release beta marcada como normal por engano não vale).
        if (channel == UpdateChannel.Stable && (release.Prerelease || version.IsBeta)) return null;
        var installer = release.Find(UpdateCandidate.InstallerName(version));
        var sums = release.Find(UpdateCandidate.SumsFile);
        return installer is null || sums is null ? null : new UpdateCandidate(version, release, installer, sums);
    }
}
