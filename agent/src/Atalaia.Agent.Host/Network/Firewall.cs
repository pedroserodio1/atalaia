// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using Atalaia.Agent.Host.Settings;
using Serilog;

namespace Atalaia.Agent.Host.Network;

/// <summary>Perfis de rede do Firewall do Windows (máscara de bits, como na API <c>INetFwPolicy2</c>).</summary>
[Flags]
internal enum FwProfiles { None = 0, Domain = 1, Private = 2, Public = 4, All = 0x7FFFFFFF }

/// <summary>Uma regra do Firewall, só com o que importa para saber se a porta do agente está liberada.</summary>
internal sealed record FwRule(string Name, bool Enabled, bool Inbound, bool Allow, int Protocol, string LocalPorts, int Profiles, string? Program);

internal enum FirewallState
{
    /// <summary>Há uma regra de entrada ativa que libera a porta na rede atual.</summary>
    Open,
    /// <summary>Nenhuma regra libera a porta.</summary>
    NoRule,
    /// <summary>Há regra, mas ela não vale para o tipo de rede em que o PC está agora (por exemplo, só para "Pública").</summary>
    OtherProfile,
    /// <summary>Há uma regra de bloqueio para a porta.</summary>
    Blocked,
}

/// <summary>O Firewall como o Windows o informou: as regras e os perfis em uso agora (<c>null</c> = não deu para ler).</summary>
internal sealed record FirewallSnapshot(IReadOnlyList<FwRule> Rules, FwProfiles CurrentProfiles);

/// <summary>O que o agente quer do Firewall (definição da regra que ele cria, sem executar nada).</summary>
internal static class FirewallRuleSpec
{
    /// <summary>Um nome só, para a regra trocar de porta sem deixar sobras. Sem espaços (aparece assim no painel do Windows).</summary>
    public const string RuleName = "Atalaia-Agente";

    public const string Description = "Atalaia: permite o tablet acessar o agente por esta porta, só na rede local (perfil Privado).";

    /// <summary>A regra: entrada, TCP, só a porta do agente, só perfil Privado, só de endereços da própria sub-rede.</summary>
    public static FwRule Create(int port)
    {
        if (port is < PortValidator.Min or > PortValidator.Max) throw new ArgumentOutOfRangeException(nameof(port), "Porta fora de 1024 a 65535.");
        return new FwRule(RuleName, Enabled: true, Inbound: true, Allow: true, Protocol: 6,
            LocalPorts: port.ToString(CultureInfo.InvariantCulture), Profiles: (int)FwProfiles.Private, Program: null);
    }

    /// <summary>Restringe a regra ao endereço local da rede (<c>LocalSubnet</c>): o tablet está na mesma rede, a internet não.</summary>
    public const string RemoteAddresses = "LocalSubnet";
}

/// <summary>Decide, sobre as regras que o Windows informou, se a porta do agente está liberada. Pura: sem COM, testável.</summary>
internal static class FirewallEvaluator
{
    public static FirewallState Evaluate(IEnumerable<FwRule> rules, int port, FwProfiles current, string? exePath)
    {
        var open = false;
        var other = false;
        foreach (var r in rules)
        {
            if (!r.Enabled || !r.Inbound || !CoversProtocol(r.Protocol) || !PortMatches(r.LocalPorts, port) || !CoversProgram(r.Program, exePath)) continue;
            var applies = ((FwProfiles)r.Profiles & current) != 0;
            if (!r.Allow) { if (applies) return FirewallState.Blocked; continue; }   // bloqueio vence a permissão
            if (applies) open = true; else other = true;
        }
        return open ? FirewallState.Open : other ? FirewallState.OtherProfile : FirewallState.NoRule;
    }

    /// <summary>TCP (6) ou qualquer protocolo (256).</summary>
    private static bool CoversProtocol(int protocol) => protocol is 6 or 256;

    /// <summary>Regra de outro programa não libera o nosso. Sem programa, vale para qualquer um.</summary>
    private static bool CoversProgram(string? program, string? exePath) =>
        string.IsNullOrWhiteSpace(program) || program == "*"
        || (exePath is not null && string.Equals(Path.GetFullPath(Environment.ExpandEnvironmentVariables(program)), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase));

    /// <summary><c>*</c>, uma porta, uma lista (<c>80,8765</c>) ou faixas (<c>8000-9000</c>).</summary>
    public static bool PortMatches(string? localPorts, int port)
    {
        if (string.IsNullOrWhiteSpace(localPorts)) return true;   // sem restrição de porta
        foreach (var part in localPorts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part == "*") return true;
            var dash = part.IndexOf('-');
            if (dash > 0 && int.TryParse(part[..dash], NumberStyles.None, CultureInfo.InvariantCulture, out var lo)
                && int.TryParse(part[(dash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out var hi))
            { if (port >= lo && port <= hi) return true; }
            else if (int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var single) && single == port) return true;
        }
        return false;
    }
}

/// <summary>Costura do Firewall para testar sem o Windows.</summary>
internal interface IFirewall
{
    /// <summary>As regras e o perfil de agora; <c>null</c> se o Windows não deixou ler.</summary>
    FirewallSnapshot? Read();

    /// <summary>Cria a regra do agente para a porta (pede permissão de administrador ao usuário). Falha com <see cref="FirewallException"/>, cujo texto já é para o usuário.</summary>
    Task CreateRuleAsync(int port, CancellationToken ct);
}

/// <summary>
/// O Firewall de verdade, pela API COM <c>HNetCfg.FwPolicy2</c> (não depende do idioma do Windows, ao contrário de ler a saída do
/// <c>netsh</c>). Ler não exige administrador; criar exige, por isso a criação roda num processo próprio elevado (<c>--firewall-rule</c>).
/// </summary>
internal sealed class WindowsFirewall : IFirewall
{
    private const string PolicyProgId = "HNetCfg.FwPolicy2";
    private const string RuleProgId = "HNetCfg.FWRule";

    public FirewallSnapshot? Read()
    {
        try
        {
            var type = Type.GetTypeFromProgID(PolicyProgId);
            if (type is null) return null;
            dynamic policy = Activator.CreateInstance(type)!;
            var rules = new List<FwRule>();
            foreach (dynamic r in policy.Rules)
            {
                try
                {
                    rules.Add(new FwRule((string?)r.Name ?? "", (bool)r.Enabled, (int)r.Direction == 1, (int)r.Action == 1,
                        (int)r.Protocol, (string?)r.LocalPorts ?? "", (int)r.Profiles, (string?)r.ApplicationName));
                }
                catch (COMException) { /* uma regra ilegível não impede de avaliar as outras */ }
                catch (Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
            }
            return new FirewallSnapshot(rules, (FwProfiles)(int)policy.CurrentProfileTypes);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            Log.Warning(ex, "Não consegui ler o Firewall do Windows.");
            return null;
        }
    }

    public async Task CreateRuleAsync(int port, CancellationToken ct)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Não sei onde está o programa.");
        var psi = new ProcessStartInfo(exe) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden };
        psi.ArgumentList.Add(FirewallHelper.Flag);
        psi.ArgumentList.Add(port.ToString(CultureInfo.InvariantCulture));
        try
        {
            using var p = Process.Start(psi) ?? throw new InvalidOperationException("Não consegui abrir o assistente de permissão.");
            await p.WaitForExitAsync(ct).WaitAsync(TimeSpan.FromSeconds(90), ct);
            if (p.ExitCode != 0) throw new FirewallException(FirewallHelper.Explain(p.ExitCode));
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)   // ERROR_CANCELLED: o usuário recusou o aviso do Windows
        {
            throw new FirewallException("Você cancelou o pedido de permissão do Windows. Sem ele o Firewall não é alterado.");
        }
        catch (TimeoutException) { throw new FirewallException("O Windows não respondeu ao pedido de permissão a tempo."); }
    }

    /// <summary>Roda dentro do processo elevado: troca a regra do agente pela da porta pedida.</summary>
    internal static void ApplyRule(int port)
    {
        var def = FirewallRuleSpec.Create(port);
        dynamic policy = Activator.CreateInstance(Type.GetTypeFromProgID(PolicyProgId)!)!;
        // Remove a regra antiga (de qualquer porta): um nome só, sem sobras.
        for (var i = 0; i < 10; i++)
        {
            var exists = false;
            foreach (dynamic r in policy.Rules) if (string.Equals((string?)r.Name, FirewallRuleSpec.RuleName, StringComparison.Ordinal)) { exists = true; break; }
            if (!exists) break;
            policy.Rules.Remove(FirewallRuleSpec.RuleName);
        }
        dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID(RuleProgId)!)!;
        rule.Name = def.Name;
        rule.Description = FirewallRuleSpec.Description;
        rule.Protocol = def.Protocol;
        rule.LocalPorts = def.LocalPorts;
        rule.Direction = 1;   // entrada
        rule.Action = 1;      // permitir
        rule.Profiles = def.Profiles;
        rule.RemoteAddresses = FirewallRuleSpec.RemoteAddresses;
        rule.Enabled = true;
        policy.Rules.Add(rule);
    }
}

internal sealed class FirewallException(string message) : Exception(message);

/// <summary>O modo <c>AtalaiaAgent.exe --firewall-rule &lt;porta&gt;</c>: roda elevado (UAC), sem janela, faz só isso e termina.</summary>
internal static class FirewallHelper
{
    public const string Flag = "--firewall-rule";
    private const int Ok = 0, BadArguments = 2, Failed = 3;

    /// <returns>O código de saída do processo: 0 ok; 2 argumento inválido; 3 o Windows recusou.</returns>
    public static int Run(string[] args)
    {
        var i = Array.IndexOf(args, Flag);
        if (i < 0 || i + 1 >= args.Length
            || !int.TryParse(args[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || port is < PortValidator.Min or > PortValidator.Max)
            return BadArguments;
        try
        {
            WindowsFirewall.ApplyRule(port);
            return Ok;
        }
        catch (Exception) { return Failed; }
    }

    public static string Explain(int exitCode) => exitCode switch
    {
        BadArguments => "A porta pedida é inválida (use de 1024 a 65535).",
        Failed => "O Windows recusou criar a regra do Firewall. Se a rede é gerenciada por uma organização, a política pode impedir.",
        _ => $"O assistente de permissão terminou com erro ({exitCode}).",
    };
}
