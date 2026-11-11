// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Net;
using System.Net.Sockets;

namespace Atalaia.Agent.Host.Network;

internal enum PortProblem { None, OutOfRange, InUse }

/// <summary>A porta do agente: de 1024 a 65535 (abaixo disso o Windows reserva) e livre. A porta que o próprio agente já usa conta como livre.</summary>
internal static class PortValidator
{
    public const int Min = 1024;
    public const int Max = 65535;

    public static PortProblem Check(int port, int currentPort, Func<int, bool> isFree)
    {
        if (port is < Min or > Max) return PortProblem.OutOfRange;
        if (port == currentPort) return PortProblem.None;
        return isFree(port) ? PortProblem.None : PortProblem.InUse;
    }

    /// <summary>Tenta abrir a porta em 127.0.0.1 (se outro programa a usa em qualquer endereço, o Windows recusa). Não aciona o Firewall.</summary>
    public static bool IsFreeOnLoopback(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException) { return false; }
    }

    public static string Message(PortProblem problem) => problem switch
    {
        PortProblem.OutOfRange => $"Use uma porta entre {Min} e {Max}.",
        PortProblem.InUse => "Essa porta já está em uso por outro programa. Escolha outra.",
        _ => "",
    };
}
