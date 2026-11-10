// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Atalaia.Agent.Host.Network;

/// <summary>Uma placa de rede com endereço de rede local (a que o tablet alcança pelo Wi-Fi).</summary>
internal sealed record LanAdapter(string Name, string Ip, bool HasGateway);

/// <summary>
/// Que endereço mostrar ao usuário como "o endereço do PC na rede". O Kestrel escuta em todas as placas (<c>0.0.0.0</c>); esta escolha só
/// decide o que a tela mostra e o que vai para o tablet. Mesma regra do plugin <c>system</c> para o padrão: a placa com gateway IPv4.
/// </summary>
internal static class LanAddressProvider
{
    /// <summary>Só faixas de rede local: 10/8, 172.16/12 e 192.168/16. Fora link-local (169.254), CGNAT/VPN (100.64/10) e loopback.</summary>
    public static bool IsLanAddress(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168);
    }

    /// <summary>Com gateway primeiro (as que dão acesso à rede de verdade), depois por nome. Placas virtuais (WSL, Docker) ficam por último.</summary>
    public static IReadOnlyList<LanAdapter> Sort(IEnumerable<LanAdapter> adapters) =>
        [.. adapters.OrderByDescending(a => a.HasGateway).ThenBy(a => a.Name, StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// A placa pedida pelo usuário (se ainda existir); senão a primeira com gateway; senão a primeira; senão <c>null</c> (sem rede local).
    /// </summary>
    public static LanAdapter? Choose(IReadOnlyList<LanAdapter> sorted, string? preferredName)
    {
        if (!string.IsNullOrWhiteSpace(preferredName)
            && sorted.FirstOrDefault(a => a.Name.Equals(preferredName, StringComparison.OrdinalIgnoreCase)) is { } wanted)
            return wanted;
        return sorted.FirstOrDefault(a => a.HasGateway) ?? sorted.FirstOrDefault();
    }

    /// <summary>Lê as placas do Windows. Só placas ativas, que não sejam loopback nem túnel (VPN), com endereço de rede local.</summary>
    public static IReadOnlyList<LanAdapter> Read()
    {
        var list = new List<LanAdapter>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var props = nic.GetIPProperties();
            var gateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.Any.Equals(g.Address));
            foreach (var u in props.UnicastAddresses)
                if (IsLanAddress(u.Address)) list.Add(new LanAdapter(nic.Name, u.Address.ToString(), gateway));
        }
        return Sort(list);
    }
}
