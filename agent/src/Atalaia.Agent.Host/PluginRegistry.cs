// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using Microsoft.Extensions.Configuration;
using Atalaia.Agent.Core;
using Atalaia.Agent.Plugins.ActiveWindow;
using Atalaia.Agent.Plugins.Audio;
using Atalaia.Agent.Plugins.Claude;
using Atalaia.Agent.Plugins.Discord;
using Atalaia.Agent.Plugins.Notifications;
using Atalaia.Agent.Plugins.Steam;
using Atalaia.Agent.Plugins.Weather;
using Atalaia.Agent.Plugins.Gpu;
using Atalaia.Agent.Plugins.Media;
using Atalaia.Agent.Plugins.Simulator;
using Atalaia.Agent.Plugins.System;
using Atalaia.Agent.Plugins.Ups;

namespace Atalaia.Agent.Host;

/// <summary>Raiz de composição: id → fábrica. O Core não conhece nenhuma fonte.</summary>
internal static class PluginRegistry
{
    private static readonly (string Id, Func<IPlugin> Create)[] Real =
    [
        ("system", () => new SystemPlugin()),
        ("gpu", () => new GpuPlugin()),
        ("media", () => new MediaPlugin()),
        ("audio", () => new AudioPlugin()),
        ("activeWindow", () => new ActiveWindowPlugin()),
        ("ups", () => new UpsPlugin()),
        ("weather", () => new WeatherPlugin()),
        ("notifications", () => new NotificationsPlugin()),
        ("claude", () => new ClaudePlugin()),
        ("steam", () => new SteamPlugin()),
        ("discord", () => new DiscordPlugin()),
    ];

    public static List<PluginRegistration> Build(IConfiguration config, bool simulate)
    {
        var section = config.GetSection("Plugins");
        if (simulate)   // os onze ligados, seja qual for a configuração
            return SimulatorPlugins.All()
                .Select(p => PluginRegistration.From(p, section.GetSection(p.Id)) with { Enabled = true, IntervalMs = p.DefaultIntervalMs })   // simulado: ritmo próprio, sem esperar o intervalo real do ups (15 s)
                .ToList();

        return Real.Select(r =>
        {
            var plugin = r.Create();
            return PluginRegistration.From(plugin, section.GetSection(plugin.Id));
        }).ToList();
    }
}
