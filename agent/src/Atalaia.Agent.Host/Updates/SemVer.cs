// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Globalization;
using System.Text.RegularExpressions;

namespace Atalaia.Agent.Host.Updates;

/// <summary>
/// A versão do Atalaia: <c>X.Y.Z</c> ou <c>X.Y.Z-beta.N</c> (só esse sufixo). Mesmas regras e mesmos limites do <c>scripts/versao.ps1</c>
/// (MAJOR até 20, MINOR e PATCH até 999, beta de 1 a 98: o que cabe no <c>versionCode</c> do Android); o teste confere os dois contra a tabela
/// <c>scripts/tests/semver-casos.json</c>. Precedência do SemVer: o beta vem antes da estável do mesmo número e <c>beta.10 &gt; beta.2</c>.
/// </summary>
internal readonly partial record struct AtalaiaVersion(int Major, int Minor, int Patch, int? Beta) : IComparable<AtalaiaVersion>
{
    // [0-9] e \z de propósito: \d aceita dígitos de outros alfabetos e $ aceita uma quebra de linha no fim.
    [GeneratedRegex(@"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-beta\.([1-9][0-9]*))?\z")]
    private static partial Regex Pattern();

    public bool IsBeta => Beta is not null;

    public static bool TryParse(string? text, out AtalaiaVersion version)
    {
        version = default;
        if (text is null || Pattern().Match(text) is not { Success: true } m) return false;
        if (!TryNumber(m.Groups[1].Value, 20, out var major) || !TryNumber(m.Groups[2].Value, 999, out var minor) || !TryNumber(m.Groups[3].Value, 999, out var patch)) return false;
        int? beta = null;
        if (m.Groups[4].Success)
        {
            if (!TryNumber(m.Groups[4].Value, 98, out var n)) return false;
            beta = n;
        }
        version = new AtalaiaVersion(major, minor, patch, beta);
        return true;
    }

    /// <summary>A versão de uma tag <c>vX.Y.Z[-beta.N]</c>; qualquer outra coisa (inclusive sem o <c>v</c>) é <c>false</c>.</summary>
    public static bool TryParseTag(string? tag, out AtalaiaVersion version)
    {
        version = default;
        return tag is { Length: > 1 } && tag[0] == 'v' && TryParse(tag[1..], out version);
    }

    private static bool TryNumber(string digits, int max, out int value) =>
        int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value <= max;

    public int CompareTo(AtalaiaVersion other)
    {
        if (Major != other.Major) return Major.CompareTo(other.Major);
        if (Minor != other.Minor) return Minor.CompareTo(other.Minor);
        if (Patch != other.Patch) return Patch.CompareTo(other.Patch);
        return (Beta, other.Beta) switch
        {
            (null, null) => 0,
            (null, _) => 1,     // estável vem depois do beta de mesmo número
            (_, null) => -1,
            var (a, b) => a.Value.CompareTo(b.Value),
        };
    }

    public static bool operator <(AtalaiaVersion a, AtalaiaVersion b) => a.CompareTo(b) < 0;
    public static bool operator >(AtalaiaVersion a, AtalaiaVersion b) => a.CompareTo(b) > 0;
    public static bool operator <=(AtalaiaVersion a, AtalaiaVersion b) => a.CompareTo(b) <= 0;
    public static bool operator >=(AtalaiaVersion a, AtalaiaVersion b) => a.CompareTo(b) >= 0;

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}.{Patch}{(Beta is { } n ? $"-beta.{n}" : "")}");

    /// <summary><c>((MAJOR*1000+MINOR)*1000+PATCH)*100+PRE</c>, com PRE = N do beta ou 99 (estável): o <c>versionCode</c> do Android.</summary>
    public long VersionCode => ((Major * 1000L + Minor) * 1000 + Patch) * 100 + (Beta ?? 99);
}
