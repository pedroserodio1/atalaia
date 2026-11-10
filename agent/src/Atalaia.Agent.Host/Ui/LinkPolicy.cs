// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Text.RegularExpressions;

namespace Atalaia.Agent.Host.Ui;

/// <summary>
/// Que endereços a janela pode abrir no navegador: só <c>https://github.com/&lt;dono&gt;/&lt;repositório&gt;</c> (e o que vem depois dele),
/// do repositório configurado em <c>Updates:Repository</c>. Qualquer outra coisa (outro site, <c>http</c>, <c>file:</c>, atalho do
/// Windows) é recusada. A página não é confiável: o texto da release, por exemplo, vem de fora.
/// </summary>
internal static partial class LinkPolicy
{
    public const string DefaultRepository = "pedroserodio1/atalaia";
    private const int MaxLength = 2048;

    [GeneratedRegex(@"^[A-Za-z0-9._-]+/[A-Za-z0-9._-]+\z")]
    private static partial Regex RepositoryPattern();

    public static bool IsValidRepository(string? repository) => repository is not null && RepositoryPattern().IsMatch(repository);

    public static bool IsAllowed(string? url, string? repository)
    {
        if (!IsValidRepository(repository) || string.IsNullOrEmpty(url) || url.Length > MaxLength) return false;
        if (url.Any(c => char.IsControl(c) || c == '\\')) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u)) return false;
        if (u.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(u.UserInfo) || !u.IsDefaultPort) return false;
        if (!u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase)) return false;

        var path = u.AbsolutePath;
        if (path.Contains("..") || path.Contains("%2e", StringComparison.OrdinalIgnoreCase) || path.Contains("%2f", StringComparison.OrdinalIgnoreCase)) return false;
        var root = "/" + repository;
        return path.Equals(root, StringComparison.OrdinalIgnoreCase) || path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase);
    }
}
