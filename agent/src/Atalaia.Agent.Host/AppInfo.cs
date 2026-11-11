// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Reflection;

namespace Atalaia.Agent.Host;

/// <summary>A versão do programa, como foi construída (<c>VERSION</c> + sufixo): <c>0.1.1-beta.1</c>, <c>0.1.1</c> ou <c>0.1.1-dev</c>.</summary>
internal static class AppInfo
{
    /// <summary>Versão de produto sem o <c>+commit</c> que o SDK acrescenta.</summary>
    public static string Version { get; } = Clean(
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);

    internal static string Clean(string? informational)
    {
        if (string.IsNullOrWhiteSpace(informational)) return "0.0.0";
        var plus = informational.IndexOf('+');
        return plus < 0 ? informational : informational[..plus];
    }
}
