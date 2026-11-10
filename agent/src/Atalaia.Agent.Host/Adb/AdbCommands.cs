// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Globalization;
using System.Text.RegularExpressions;

namespace Atalaia.Agent.Host.Adb;

/// <summary>
/// Os comandos de <c>adb</c> que a janela usa, como listas de argumentos (nada é montado como texto de shell, então não há
/// injeção). Só estes: ler aparelhos e o estado do túnel, ver a versão do app do tablet, instalar e desinstalar o <b>app do Atalaia</b>.
/// </summary>
internal static class AdbCommands
{
    /// <summary>O pacote do app do tablet (<c>appId</c> em <c>app/gradle.properties</c>).</summary>
    public const string TabletPackage = "org.atalaia.app";

    private static readonly Regex SerialPattern = new(@"^[A-Za-z0-9._:\-]{1,64}\z", RegexOptions.Compiled);

    public static IReadOnlyList<string> Devices() => ["devices", "-l"];

    public static IReadOnlyList<string> ReverseList(string serial) => ["-s", CheckSerial(serial), "reverse", "--list"];

    /// <summary>O mesmo que o módulo USB faz: aparelho → PC na mesma porta.</summary>
    public static IReadOnlyList<string> Reverse(string serial, int port) =>
        ["-s", CheckSerial(serial), "reverse", $"tcp:{port.ToString(CultureInfo.InvariantCulture)}", $"tcp:{port.ToString(CultureInfo.InvariantCulture)}"];

    /// <summary>Instala por cima (<c>-r</c>), mantendo os dados do app se a assinatura for a mesma.</summary>
    public static IReadOnlyList<string> Install(string serial, string apkPath) => ["-s", CheckSerial(serial), "install", "-r", apkPath];

    /// <summary>Remove o app do Atalaia (e a configuração dele no tablet). Só o pacote do Atalaia, nunca outro.</summary>
    public static IReadOnlyList<string> UninstallTablet(string serial) => ["-s", CheckSerial(serial), "uninstall", TabletPackage];

    public static IReadOnlyList<string> DumpPackage(string serial) => ["-s", CheckSerial(serial), "shell", "dumpsys", "package", TabletPackage];

    /// <summary>O número de série vem do <c>adb devices</c> (e, depois, da página): só caracteres de série de verdade.</summary>
    public static string CheckSerial(string serial) =>
        SerialPattern.IsMatch(serial) ? serial : throw new ArgumentException("Número de série inválido.", nameof(serial));

    public static bool IsValidSerial(string? serial) => serial is not null && SerialPattern.IsMatch(serial);
}

/// <summary>O que o app do Atalaia mostra no tablet, a partir do <c>dumpsys package</c>.</summary>
internal static class TabletPackage
{
    /// <summary>A <c>versionName</c> instalada (<c>0.1.1-beta.1</c>), ou <c>null</c> se o app não está instalado.</summary>
    public static string? ParseVersionName(string dumpsys)
    {
        if (!dumpsys.Contains($"Package [{AdbCommands.TabletPackage}]", StringComparison.Ordinal)) return null;   // saída de app ausente não tem o bloco
        var m = Regex.Match(dumpsys, @"^\s*versionName=(\S+)", RegexOptions.Multiline);
        return m.Success ? m.Groups[1].Value : null;
    }
}
