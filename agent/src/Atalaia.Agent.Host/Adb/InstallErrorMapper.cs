// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Text.RegularExpressions;

namespace Atalaia.Agent.Host.Adb;

internal enum InstallOutcome
{
    Success,
    /// <summary>O app instalado foi assinado com outra chave (por exemplo, o de debug da versão 0.0.1): só desinstalando.</summary>
    SignatureMismatch,
    /// <summary>O tablet já tem uma versão mais nova do app.</summary>
    Downgrade,
    OlderSdk,
    NoSpace,
    /// <summary>Sem aparelho, sem autorização ou offline.</summary>
    DeviceUnavailable,
    Timeout,
    Other,
}

internal sealed record InstallResult(InstallOutcome Outcome, string Message, string? AdbCode = null)
{
    public bool Ok => Outcome == InstallOutcome.Success;
}

/// <summary>Traduz o que o <c>adb install</c> imprime para uma mensagem que o usuário entende e uma ação (desinstalar, liberar espaço...).</summary>
internal static partial class InstallErrorMapper
{
    [GeneratedRegex(@"Failure \[([A-Z0-9_]+)")]
    private static partial Regex FailureCode();

    public static InstallResult Map(int exitCode, string output)
    {
        if (output.Contains("tempo esgotado", StringComparison.OrdinalIgnoreCase))
            return new(InstallOutcome.Timeout, "A instalação demorou demais e foi interrompida. Confira o cabo e a tela do tablet e tente de novo.");

        // O adb imprime "Success" mesmo com avisos antes; o código de saída é o que manda, junto com a palavra.
        if (exitCode == 0 && Regex.IsMatch(output, @"^\s*Success\s*$", RegexOptions.Multiline))
            return new(InstallOutcome.Success, "App instalado no tablet.");

        var code = FailureCode().Match(output) is { Success: true } m ? m.Groups[1].Value : null;
        bool Has(string s) => output.Contains(s, StringComparison.OrdinalIgnoreCase);

        if (code == "INSTALL_FAILED_UPDATE_INCOMPATIBLE" || Has("signatures do not match"))
            return new(InstallOutcome.SignatureMismatch,
                "O app que está no tablet foi assinado com outra chave e não pode ser atualizado por cima. É preciso desinstalá-lo antes, o que apaga a configuração dele no tablet (endereços e código de acesso).", code);
        if (code == "INSTALL_FAILED_VERSION_DOWNGRADE")
            return new(InstallOutcome.Downgrade, "O tablet já tem uma versão mais nova do app. O Atalaia não volta para uma versão mais antiga sozinho.", code);
        if (code == "INSTALL_FAILED_OLDER_SDK")
            return new(InstallOutcome.OlderSdk, "Este app precisa do Android 8.1 ou mais novo, e o tablet tem uma versão mais antiga.", code);
        if (code == "INSTALL_FAILED_INSUFFICIENT_STORAGE")
            return new(InstallOutcome.NoSpace, "O tablet está sem espaço. Libere espaço e tente de novo.", code);
        if (Has("unauthorized"))
            return new(InstallOutcome.DeviceUnavailable, "O tablet ainda não autorizou este PC. Toque em Permitir no aviso de depuração USB, na tela dele.", code);
        if (Has("no devices") || Has("offline") || Has("not found") || Has("more than one device"))
            return new(InstallOutcome.DeviceUnavailable, "O tablet não está respondendo pelo cabo. Confira a conexão e a depuração USB.", code);

        return new(InstallOutcome.Other, code is null ? "A instalação falhou. Veja o log do agente." : $"A instalação falhou ({code}).", code);
    }
}
