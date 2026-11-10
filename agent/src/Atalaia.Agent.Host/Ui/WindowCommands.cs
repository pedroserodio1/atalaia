// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Atalaia.Agent.Core;
using Atalaia.Agent.Host.Settings;

namespace Atalaia.Agent.Host.Ui;

/// <summary>Os comandos que a janela pode pedir ao agente (a lista inteira: ver <see cref="Register"/>).</summary>
internal sealed class WindowCommands(
    AgentRuntime runtime,
    OnboardingStore onboarding,
    Func<string> repository,
    Action closeWindow,
    Action<string> openExternal)
{
    public void Register(CommandRouter router)
    {
        router.Register("state", _ => Task.FromResult<JsonNode?>(State()));
        router.Register("onboarding.go", a => Onboard(s => s.Go(CommandRouter.Int(a, "step"))));
        router.Register("onboarding.complete", a => Onboard(s => s.Complete(CommandRouter.Int(a, "step"))));
        router.Register("onboarding.skip", a => Onboard(s => s.Skip(CommandRouter.Int(a, "step"))));
        router.Register("onboarding.back", _ => Onboard(s => s.Back()));
        router.Register("onboarding.finish", _ => Onboard(s => s.Finish()));
        router.Register("onboarding.dismiss", _ => Onboard(s => s.Dismiss()));
        router.Register("onboarding.restart", _ => Onboard(s => s.Restart()));
        router.Register("window.close", _ => { closeWindow(); return Task.FromResult<JsonNode?>(null); });
        router.Register("link.open", a => OpenLink(CommandRouter.Text(a, "url")));
    }

    /// <summary>O que a janela desenha: versão, endereço, quem está conectado e o onboarding. Nunca o código de acesso.</summary>
    public JsonObject State() => new()
    {
        ["version"] = AppInfo.Version,
        ["address"] = runtime.Address,
        ["port"] = runtime.Port,
        ["listenOnLan"] = runtime.ListenOnLan,
        ["simulate"] = runtime.Simulate,
        ["clients"] = new JsonArray([.. runtime.Clients.Select(ClientJson)]),
        ["onboarding"] = OnboardingJson(onboarding.State),
    };

    /// <summary>Loopback é o USB (<c>adb reverse</c> chega ao agente como 127.0.0.1); qualquer outro endereço é Wi-Fi/rede local.</summary>
    public static string ChannelOf(string remote) =>
        IPAddress.TryParse(remote, out var ip) && IPAddress.IsLoopback(ip) ? "usb" : "wifi";

    internal static JsonNode ClientJson(ClientInfo c) => new JsonObject
    {
        ["remote"] = c.Remote,
        ["channel"] = ChannelOf(c.Remote),
        ["client"] = c.Client,
        ["version"] = c.Version,
        ["since"] = c.ConnectedAt.ToUnixTimeMilliseconds(),
    };

    public static JsonObject OnboardingJson(OnboardingState s)
    {
        var steps = new JsonObject();
        for (var i = OnboardingState.FirstStep; i <= OnboardingState.LastStep; i++)
            steps[OnboardingState.StepKeys[i]] = s.StatusOf(i).ToString().ToLowerInvariant();
        return new JsonObject
        {
            ["completed"] = s.Completed,
            ["dismissed"] = s.Dismissed,
            ["autoOpen"] = s.AutoOpen,
            ["current"] = s.Current,
            ["steps"] = steps,
        };
    }

    private Task<JsonNode?> Onboard(Func<OnboardingState, OnboardingState> transition) =>
        Task.FromResult<JsonNode?>(OnboardingJson(onboarding.Apply(transition)));

    private Task<JsonNode?> OpenLink(string url)
    {
        if (!LinkPolicy.IsAllowed(url, repository()))
            throw new CommandException("link_not_allowed", "Este endereço não pode ser aberto pela janela.");
        openExternal(url);
        return Task.FromResult<JsonNode?>(null);
    }
}
