using Microsoft.Extensions.Configuration;
using PcAgent.Core;
using PcAgent.Plugins.ActiveWindow;
using PcAgent.Plugins.Audio;
using PcAgent.Plugins.Claude;
using PcAgent.Plugins.Discord;
using PcAgent.Plugins.Notifications;
using PcAgent.Plugins.Steam;
using PcAgent.Plugins.Weather;
using PcAgent.Plugins.Gpu;
using PcAgent.Plugins.Media;
using PcAgent.Plugins.Simulator;
using PcAgent.Plugins.System;
using PcAgent.Plugins.Ups;

namespace PcAgent.Host;

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
