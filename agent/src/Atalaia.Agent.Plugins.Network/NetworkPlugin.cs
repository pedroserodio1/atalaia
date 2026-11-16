// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Atalaia.Agent.Core;

namespace Atalaia.Agent.Plugins.Network;

/// <summary>Um alvo do ping: o nome que o usuário escolheu e o endereço (<c>gateway</c> ou um IPv4 literal). Só o nome vai para o tablet.</summary>
public sealed record NetworkTarget(string Name, string Address);

/// <summary>
/// Latência, jitter e perda de pacotes até alguns alvos (por padrão, o roteador e um resolvedor público), com <c>ping</c> (ICMP). Só mede; não pede
/// administrador, chave nem dependência nova. Os alvos vêm de <c>Plugins:network:targets</c>: <c>Nome endereço</c>, separados por <c>;</c>. Perda de 100% é
/// uma medida válida (a internet caiu), não um erro do plugin. Nenhum endereço vai para o log nem para o protocolo.
/// </summary>
public sealed class NetworkPlugin : IPlugin
{
    public const int MaxTargets = 4;
    public const int MaxName = 24;
    public const int WindowSize = 30;          // ~1 min a 2 s por coleta
    public const int TimeoutMs = 1000;
    public const string GatewayWord = "gateway";

    private static readonly TimeSpan GatewayRefresh = TimeSpan.FromSeconds(30);

    private IReadOnlyList<NetworkTarget> _targets = [];
    private List<int?>[] _windows = [];
    private IPAddress? _gateway;
    private DateTime _gatewayAt = DateTime.MinValue;

    public string Id => "network";
    public string Name => "Rede";
    public int DefaultIntervalMs => 2000;
    public IReadOnlyList<ActionDescriptor> Actions { get; } = [];

    public Task StartAsync(IPluginContext ctx, CancellationToken ct)
    {
        _targets = ParseTargets(ctx.Options["targets"], out var error);
        if (error is not null) throw new InvalidOperationException($"Configure Plugins:network:targets. {error}");
        _windows = [.. _targets.Select(_ => new List<int?>())];
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<JsonElement?> CollectAsync(CancellationToken ct)
    {
        var gateway = _targets.Any(t => IsGateway(t.Address)) ? Gateway() : null;
        var samples = await Task.WhenAll(_targets.Select(t => PingAsync(IsGateway(t.Address) ? gateway : IPAddress.Parse(t.Address), ct)));
        for (var i = 0; i < _targets.Count; i++) Push(_windows[i], samples[i]);
        return Build(_targets, _windows);
    }

    /// <summary>Esta fonte só mede: não há ação.</summary>
    public Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct) =>
        Task.FromResult(new ActionResult(false, "unknown_action"));

    // ---------------------------------------------------------------- medição (cola de rede; provada ao vivo, não nos testes)

    /// <summary>Um ping. Sem resposta, timeout, rede fora ou sem gateway: <c>null</c> (é a própria medição, a perda). Nunca lança por causa da rede.</summary>
    private static async Task<int?> PingAsync(IPAddress? address, CancellationToken ct)
    {
        if (address is null) return null;
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, TimeoutMs).WaitAsync(ct);
            return reply.Status == IPStatus.Success ? (int)Math.Max(0, reply.RoundtripTime) : null;
        }
        catch (PingException) { return null; }
        catch (SocketException) { return null; }
    }

    /// <summary>O roteador desta máquina: o primeiro gateway IPv4 de uma interface ativa que não seja loopback nem túnel. Refeito a cada 30 s (o Wi-Fi muda).</summary>
    private IPAddress? Gateway()
    {
        if (DateTime.UtcNow - _gatewayAt < GatewayRefresh) return _gateway;
        _gatewayAt = DateTime.UtcNow;
        try
        {
            _gateway = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
                .SelectMany(n => n.GetIPProperties().GatewayAddresses)
                .Select(g => g.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !a.Equals(IPAddress.Any));
        }
        catch (NetworkInformationException) { _gateway = null; }
        return _gateway;
    }

    // ---------------------------------------------------------------- funções puras (é o que os testes chamam)

    public static bool IsGateway(string address) => address.Equals(GatewayWord, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A configuração dos alvos: <c>Nome endereço</c> separados por <c>;</c> ou por linha (até 4). O endereço é a última palavra: <c>gateway</c> ou um IPv4 com os
    /// quatro números (sem nome de host, que obrigaria a resolver DNS). O nome é opcional (sem ele vale "Roteador" para o gateway e "Alvo N" para o resto).
    /// Os erros dizem qual alvo e nunca repetem o que foi digitado.
    /// </summary>
    public static IReadOnlyList<NetworkTarget> ParseTargets(string? text, out string? error)
    {
        error = null;
        var parts = (text ?? "").Split([';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0) { error = "Informe pelo menos um alvo (por exemplo, Roteador gateway; Internet 1.1.1.1)."; return []; }
        if (parts.Length > MaxTargets) { error = $"No máximo {MaxTargets} alvos."; return []; }

        var targets = new List<NetworkTarget>();
        for (var i = 0; i < parts.Length; i++)
        {
            var label = $"Alvo {i + 1}";
            var cut = parts[i].LastIndexOfAny([' ', '\t']);
            var address = (cut < 0 ? parts[i] : parts[i][(cut + 1)..]).Trim();
            var name = (cut < 0 ? "" : parts[i][..cut]).Trim();
            if (!IsGateway(address) && !IsPublicOrLanIPv4(address))
            { error = $"{label}: o endereço precisa ser \"{GatewayWord}\" ou um IPv4 com os quatro números (por exemplo, 1.1.1.1)."; return []; }
            if (name.Length > MaxName) { error = $"{label}: o nome tem no máximo {MaxName} caracteres."; return []; }
            if (name.Any(char.IsControl)) { error = $"{label}: o nome não pode ter quebra de linha."; return []; }
            if (name.Length == 0) name = IsGateway(address) ? "Roteador" : label;
            targets.Add(new NetworkTarget(name, IsGateway(address) ? GatewayWord : address));
        }
        return targets;
    }

    // Os quatro números escritos (IPAddress.TryParse aceita "1" como 0.0.0.1) e nada de 0.0.0.0, de multicast ou de broadcast.
    private static bool IsPublicOrLanIPv4(string text)
    {
        if (text.Split('.').Length != 4 || !IPAddress.TryParse(text, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.Broadcast) && b[0] is < 224;
    }

    /// <summary>Acrescenta uma amostra (<c>null</c> = sem resposta) e guarda só as últimas <see cref="WindowSize"/>.</summary>
    public static void Push(List<int?> window, int? sample)
    {
        window.Add(sample);
        if (window.Count > WindowSize) window.RemoveRange(0, window.Count - WindowSize);
    }

    /// <summary>
    /// Resume a janela (da mais antiga para a mais nova; <c>null</c> = sem resposta). Tudo inteiro: média e jitter arredondados; a perda é a porcentagem de
    /// amostras sem resposta. O jitter é a média da diferença entre respostas seguidas (as perdas ficam de fora); precisa de pelo menos duas respostas.
    /// Sem nenhuma resposta, os tempos ficam <c>null</c> (nunca 0 fingindo leitura) e a perda é 100.
    /// </summary>
    public static (int? Last, int? Avg, int? Min, int? Max, int? Jitter, int? Loss) Summarize(IReadOnlyList<int?> window)
    {
        if (window.Count == 0) return (null, null, null, null, null, null);
        var ok = window.Where(s => s.HasValue).Select(s => s!.Value).ToList();
        var loss = (int)Math.Round(100.0 * (window.Count - ok.Count) / window.Count, MidpointRounding.AwayFromZero);
        if (ok.Count == 0) return (null, null, null, null, null, loss);
        int? jitter = ok.Count < 2 ? null
            : (int)Math.Round(Enumerable.Range(1, ok.Count - 1).Average(i => Math.Abs(ok[i] - ok[i - 1])), MidpointRounding.AwayFromZero);
        return (window[^1], (int)Math.Round(ok.Average(), MidpointRounding.AwayFromZero), ok.Min(), ok.Max(), jitter, loss);
    }

    /// <summary>Os alvos e as janelas → <c>data</c> do protocolo. Só o nome do alvo sai (e se ele é o roteador), nunca o endereço.</summary>
    public static JsonElement Build(IReadOnlyList<NetworkTarget> targets, IReadOnlyList<List<int?>> windows) => Json.ToElement(new
    {
        targets = targets.Select((t, i) =>
        {
            var s = Summarize(windows[i]);
            return new
            {
                name = t.Name,
                gateway = IsGateway(t.Address),   // só diz se é o roteador (para o tablet separar "internet fora" de "sem rede"); o endereço não sai
                rttMs = s.Last,
                avgMs = s.Avg,
                minMs = s.Min,
                maxMs = s.Max,
                jitterMs = s.Jitter,
                lossPercent = s.Loss,
                samples = windows[i].ToList(),
            };
        }).ToList(),
    });
}
