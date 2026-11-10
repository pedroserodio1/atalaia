using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PcAgent.Core;

namespace PcAgent.Plugins.System;

/// <summary>CPU, RAM, discos fixos e rede. Origem: <c>Metrics</c> e <c>PickNic</c> do PcProbe.</summary>
public sealed class SystemPlugin : IPlugin
{
    private static readonly TimeSpan DiskRefresh = TimeSpan.FromSeconds(30);

    private IPluginContext _ctx = null!;
    private PerformanceCounter? _cpu, _available;
    private NetworkInterface? _nic;
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
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct)
    {
        _cpu?.Dispose();
        _available?.Dispose();
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
            cpu = new { percent = cpu },
            ram = new { usedMb = (int)Math.Round(usedMb), totalMb = (int)Math.Round(totalMb) },
            disks = ReadDisks(),
            net = new { rxKbps = rx, txKbps = tx },
        }));
    }

    public Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct) =>
        Task.FromResult(new ActionResult(false, "unknown_action"));

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
            _nic ??= PickNic(_preferredNic, _ctx.Logger);
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

    private static bool HasIPv4Gateway(NetworkInterface n) =>
        n.GetIPProperties().GatewayAddresses.Any(g =>
            g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
}
