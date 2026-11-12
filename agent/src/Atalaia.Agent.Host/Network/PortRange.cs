// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Globalization;
using System.Net.Sockets;

namespace Atalaia.Agent.Host.Network;

/// <summary>
/// A faixa de portas do agente (<c>docs/protocolo-v1.md</c>, "Transporte"): a porta configurada é o início de <see cref="Count"/> portas
/// contíguas. O agente escuta na primeira livre; o app tenta a faixa em ordem; o Firewall libera a faixa toda.
/// </summary>
internal static class PortRange
{
    /// <summary>A porta padrão do agente e a que o app do tablet usa no USB (<c>adb reverse</c>).</summary>
    public const int Default = 8765;

    public const int Count = 10;

    public static int Last(int first) => Math.Min(first + Count - 1, PortValidator.Max);

    /// <summary>As portas a tentar, em ordem. <c>0</c> (porta escolhida pelo sistema, só nos testes) não tem faixa.</summary>
    public static IEnumerable<int> From(int first) => first == 0 ? [0] : Enumerable.Range(first, Last(first) - first + 1);

    /// <summary>Do jeito que o Firewall do Windows entende: <c>8765-8774</c>.</summary>
    public static string Text(int first) => $"{first.ToString(CultureInfo.InvariantCulture)}-{Last(first).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Porta ocupada ou reservada pelo Windows (o Hyper-V reserva faixas): vale tentar a próxima da faixa. Qualquer outro erro, não.</summary>
    public static bool IsPortTaken(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse or SocketError.AccessDenied }) return true;
        return false;
    }
}
