// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Atalaia.Agent.Core;

namespace Atalaia.Agent.Plugins.System;

/// <summary>CPU, RAM, discos fixos e rede. Origem: <c>Metrics</c> e <c>PickNic</c> do PcProbe.</summary>
public sealed class SystemPlugin : IPlugin
{
    private static readonly TimeSpan DiskRefresh = TimeSpan.FromSeconds(30);

    private IPluginContext _ctx = null!;
    private PerformanceCounter? _cpu, _available;
    private PerformanceCounter[]? _cores;   // um contador por núcleo lógico; nulo = sem leitura (contadores indisponíveis)
    private NetworkInterface? _nic;
    private string? _mac;   // MAC de cabo da placa escolhida; nulo no Wi-Fi ou sem placa
    private string _preferredNic = "";
    private long _rx, _tx, _netStamp;
    private bool _netBaseline;
    private object[] _disks = [];
    private long _disksStamp;

    public string Id => "system";
    public string Name => "Sistema";
    public int DefaultIntervalMs => 1000;
    public IReadOnlyList<ActionDescriptor> Actions => [];

    public Task StartAsync(IPluginContext ctx, CancellationToken ct)
    {
        _ctx = ctx;
        _preferredNic = ctx.Options["netInterface"]?.Trim() ?? "";
        _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");
        _available = new PerformanceCounter("Memory", "Available MBytes");
        _cpu.NextValue();   // a primeira leitura vem 0
        _available.NextValue();
        _cores = OpenCoreCounters();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _cpu?.Dispose();
        _available?.Dispose();
        foreach (var c in _cores ?? []) c.Dispose();
        return Task.CompletedTask;
    }

    public Task<JsonElement?> CollectAsync(CancellationToken ct)
    {
        var cpu = Math.Clamp((int)Math.Round(_cpu!.NextValue()), 0, 100);

        var totalMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1048576.0;
        var usedMb = Math.Max(0, totalMb - _available!.NextValue());

        var (rx, tx) = ReadNetwork();
        return Task.FromResult<JsonElement?>(Json.ToElement(new
        {
            cpu = new { percent = cpu, cores = ReadCores() },
            ram = new { usedMb = (int)Math.Round(usedMb), totalMb = (int)Math.Round(totalMb) },
            disks = ReadDisks(),
            net = new { rxKbps = rx, txKbps = tx, mac = _mac },
        }));
    }

    public Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct) =>
        Task.FromResult(new ActionResult(false, "unknown_action"));

    /// <summary>
    /// Os contadores de uso por núcleo lógico, na ordem dos núcleos. Se a categoria não existe ou o Windows está com os contadores de desempenho
    /// corrompidos, devolve <c>null</c>: o uso por núcleo fica sem leitura e o resto da fonte segue funcionando.
    /// </summary>
    private static PerformanceCounter[]? OpenCoreCounters()
    {
        try
        {
            var names = CoreInstances(new PerformanceCounterCategory("Processor").GetInstanceNames());
            if (names.Count == 0) return null;
            var counters = names.Select(n => new PerformanceCounter("Processor", "% Processor Time", n)).ToArray();
            foreach (var c in counters) c.NextValue();   // a primeira leitura vem 0
            return counters;
        }
        catch (Exception e) when (e is InvalidOperationException or UnauthorizedAccessException or Win32Exception) { return null; }
    }

    private int[]? ReadCores()
    {
        if (_cores is null) return null;
        try { return ToPercents(_cores.Select(c => c.NextValue())); }
        catch (InvalidOperationException) { return null; }   // um núcleo sumiu nesta rodada: sem leitura agora, tenta de novo na próxima
    }

    /// <summary>
    /// Os nomes de instância da categoria <c>Processor</c> que são núcleos (só dígitos), em ordem numérica. Fica de fora o <c>_Total</c> e, em máquinas com mais
    /// de 64 núcleos lógicos, os nomes de grupo (<c>0,1</c>): o uso por núcleo passa a ser só dos núcleos que o Windows lista assim.
    /// </summary>
    public static IReadOnlyList<string> CoreInstances(IEnumerable<string> instanceNames) =>
        [.. instanceNames.Where(n => n.Length is > 0 and <= 4 && n.All(char.IsAsciiDigit)).OrderBy(n => int.Parse(n, CultureInfo.InvariantCulture))];

    /// <summary>Uso de cada núcleo em porcentagem inteira, de 0 a 100 (o contador pode passar de 100 por arredondamento).</summary>
    public static int[] ToPercents(IEnumerable<float> values) =>
        [.. values.Select(v => Math.Clamp((int)Math.Round(float.IsFinite(v) ? v : 0, MidpointRounding.AwayFromZero), 0, 100))];

    private object[] ReadDisks()
    {
        if (_disks.Length == 0 || Stopwatch.GetElapsedTime(_disksStamp) >= DiskRefresh)
        {
            _disksStamp = Stopwatch.GetTimestamp();
            _disks = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .Select(d => (object)new
                {
                    name = d.Name.TrimEnd('\\'),
                    usedGb = Math.Round((d.TotalSize - d.AvailableFreeSpace) / 1073741824.0, 1),
                    totalGb = Math.Round(d.TotalSize / 1073741824.0, 1),
                }).ToArray();
        }
        return _disks;
    }

    /// <summary>Diferença de bytes entre duas coletas ÷ tempo real decorrido, em kilobits por segundo.</summary>
    private (int Rx, int Tx) ReadNetwork()
    {
        try
        {
            if (_nic is null)
            {
                _nic = PickNic(_preferredNic, _ctx.Logger);
                _mac = WiredMac(_nic);   // o MAC não muda entre coletas e nunca vai para o log
            }
            if (_nic is null) { _netBaseline = false; return (0, 0); }

            var s = _nic.GetIPv4Statistics();
            long rx = s.BytesReceived, tx = s.BytesSent, now = Stopwatch.GetTimestamp();
            var result = (0, 0);
            if (_netBaseline)
            {
                var seconds = Stopwatch.GetElapsedTime(_netStamp, now).TotalSeconds;
                if (seconds > 0)
                    result = ((int)Math.Round(Math.Max(0, rx - _rx) * 8 / 1000.0 / seconds),
                              (int)Math.Round(Math.Max(0, tx - _tx) * 8 / 1000.0 / seconds));
            }
            (_rx, _tx, _netStamp, _netBaseline) = (rx, tx, now, true);   // primeira coleta: 0
            return result;
        }
        catch (NetworkInformationException)
        {
            _nic = null;   // a placa sumiu; escolhe de novo na próxima coleta
            _mac = null;
            _netBaseline = false;
            return (0, 0);
        }
    }

    /// <summary>
    /// <paramref name="preferred"/> (pelo nome) se informado; senão a primeira ativa, nem loopback nem túnel,
    /// com gateway IPv4; sem nenhuma assim, a regra antiga do PcProbe (primeira ativa).
    /// </summary>
    public static NetworkInterface? PickNic(string preferred, ILogger? logger = null)
    {
        var active = NetworkInterface.GetAllNetworkInterfaces().Where(n =>
            n.OperationalStatus == OperationalStatus.Up
            && n.NetworkInterfaceType != NetworkInterfaceType.Loopback
            && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel).ToList();

        var chosen = (preferred.Length > 0 ? active.FirstOrDefault(n => n.Name == preferred) : null)
            ?? active.FirstOrDefault(HasIPv4Gateway)
            ?? active.FirstOrDefault();
        logger?.LogInformation("Placa de rede escolhida: {Nic}", chosen?.Name ?? "(nenhuma)");
        return chosen;
    }

    /// <summary>MAC em hexadecimal maiúsculo separado por <c>:</c>; nulo se não tiver 6 bytes ou for tudo zero.</summary>
    public static string? FormatMac(byte[]? bytes)
    {
        if (bytes is not { Length: 6 } || bytes.All(b => b == 0)) return null;
        return string.Join(':', bytes.Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
    }

    /// <summary>Só placa de cabo: o Wake-on-LAN não funciona pelo Wi-Fi.</summary>
    public static bool IsWired(NetworkInterfaceType t) => t is NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet
        or NetworkInterfaceType.FastEthernetT or NetworkInterfaceType.FastEthernetFx or NetworkInterfaceType.Ethernet3Megabit;

    /// <summary>MAC da placa se ela for de cabo; senão <c>null</c>.</summary>
    public static string? WiredMac(NetworkInterface? nic) =>
        nic is not null && IsWired(nic.NetworkInterfaceType) ? FormatMac(nic.GetPhysicalAddress().GetAddressBytes()) : null;

    private static bool HasIPv4Gateway(NetworkInterface n) =>
        n.GetIPProperties().GatewayAddresses.Any(g =>
            g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
}
