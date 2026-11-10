// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using Atalaia.Agent.Core;

namespace Atalaia.Agent.Plugins.Audio;

/// <summary>
/// Volume e mudo do dispositivo de saída padrão, mais o mixer por aplicativo (sessões de áudio do Windows).
/// Origem: <c>Audio</c> do PcProbe.
/// </summary>
public sealed class AudioPlugin : IPlugin
{
    /// <summary>Um aplicativo no mixer. Sessões do mesmo executável viram uma linha só, como no mixer do Windows.</summary>
    public sealed record AppEntry(string Id, string Name, string Process, int Volume, bool Muted, bool Active);

    private readonly ConcurrentDictionary<uint, (string Name, string Exe)> _apps = new();

    public string Id => "audio";
    public string Name => "Áudio";
    public int DefaultIntervalMs => 500;
    public IReadOnlyList<ActionDescriptor> Actions { get; } =
    [
        new("set_volume", "args.value de 0 a 100"), new("toggle_mute"),
        new("set_session_volume", "args.id (do mixer) e args.value de 0 a 100"), new("toggle_session_mute", "args.id (do mixer)"),
    ];

    public Task StartAsync(IPluginContext ctx, CancellationToken ct) => Task.CompletedTask;
    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<JsonElement?> CollectAsync(CancellationToken ct)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = DefaultRender(enumerator);   // o dispositivo padrão pode mudar entre coletas
        return Task.FromResult<JsonElement?>(Json.ToElement(new
        {
            output = device.FriendlyName,
            volume = (int)Math.Round(device.AudioEndpointVolume.MasterVolumeLevelScalar * 100),
            muted = device.AudioEndpointVolume.Mute,
            sessions = ReadMixer(device),
        }));
    }

    public Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct)
    {
        switch (action)
        {
            case "set_volume":
                if (!TryParseVolume(args, out var percent)) return Task.FromResult(new ActionResult(false, "invalid_args"));
                using (var enumerator = new MMDeviceEnumerator())
                using (var device = DefaultRender(enumerator))
                    device.AudioEndpointVolume.MasterVolumeLevelScalar = percent / 100f;
                return Task.FromResult(new ActionResult(true));

            case "toggle_mute":
                using (var enumerator = new MMDeviceEnumerator())
                using (var device = DefaultRender(enumerator))
                    device.AudioEndpointVolume.Mute = !device.AudioEndpointVolume.Mute;
                return Task.FromResult(new ActionResult(true));

            case "set_session_volume":
                if (!TryParseSession(args, true, out var sid, out var level)) return Task.FromResult(new ActionResult(false, "invalid_args"));
                return Task.FromResult(WithSessions(sid, s => s.SimpleAudioVolume.Volume = level / 100f));

            case "toggle_session_mute":
                if (!TryParseSession(args, false, out var mid, out _)) return Task.FromResult(new ActionResult(false, "invalid_args"));
                return Task.FromResult(ToggleSessionMute(mid));

            default:
                return Task.FromResult(new ActionResult(false, "unknown_action"));
        }
    }

    private static MMDevice DefaultRender(MMDeviceEnumerator e) =>
        e.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

    // ---------------------------------------------------------------- mixer

    private List<AppEntry> ReadMixer(MMDevice device)
    {
        var manager = device.AudioSessionManager;
        manager.RefreshSessions();   // sem isso, apps que começaram a tocar depois não aparecem
        var raw = new List<(string Key, string Name, string Process, float Volume, bool Muted, bool Active)>();
        var seen = new HashSet<uint>();
        var sessions = manager.Sessions;
        for (var i = 0; i < sessions.Count; i++)
        {
            using var s = sessions[i];
            if (s.State == AudioSessionState.AudioSessionStateExpired) continue;
            var (key, name, process) = Identify(s, seen);
            if (key is null) continue;
            raw.Add((key, name, process, s.SimpleAudioVolume.Volume, s.SimpleAudioVolume.Mute,
                     s.State == AudioSessionState.AudioSessionStateActive));
        }
        foreach (var gone in _apps.Keys.Where(k => !seen.Contains(k)).ToList()) _apps.TryRemove(gone, out _);
        return Group(raw);
    }

    private ActionResult WithSessions(string id, Action<AudioSessionControl> apply)
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = DefaultRender(enumerator);
        var manager = device.AudioSessionManager;
        manager.RefreshSessions();
        var found = 0;
        var seen = new HashSet<uint>();
        var sessions = manager.Sessions;
        for (var i = 0; i < sessions.Count; i++)
        {
            using var s = sessions[i];
            if (s.State == AudioSessionState.AudioSessionStateExpired) continue;
            var (key, _, _) = Identify(s, seen);
            if (key is null || GroupId(key) != id) continue;
            apply(s);
            found++;
        }
        return found > 0 ? new ActionResult(true) : new ActionResult(false, "unknown_session");
    }

    private ActionResult ToggleSessionMute(string id)
    {
        // todas mudas -> liga o som de todas; senão muta todas (igual a uma linha só no mixer)
        var allMuted = true;
        var probe = WithSessions(id, s => allMuted &= s.SimpleAudioVolume.Mute);
        return probe.Ok ? WithSessions(id, s => s.SimpleAudioVolume.Mute = !allMuted) : probe;
    }

    /// <summary>Chave do aplicativo (executável em minúsculas), nome para exibir e executável. Chave nula = ignorar.</summary>
    private (string? Key, string Name, string Process) Identify(AudioSessionControl s, HashSet<uint> seen)
    {
        if (s.IsSystemSoundsSession) return ("system-sounds", "Sons do sistema", "");
        var pid = s.GetProcessID;
        if (pid == 0) return (null, "", "");
        seen.Add(pid);
        var (name, exe) = _apps.GetOrAdd(pid, ResolveApp);
        return (exe.ToLowerInvariant(), name, exe);
    }

    private static (string Name, string Exe) ResolveApp(uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            var exe = p.ProcessName + ".exe";
            string? description = null;
            try { description = p.MainModule?.FileVersionInfo.FileDescription; }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { }   // processo protegido
            // a descrição do executável às vezes é uma frase inteira: nesse caso vale o nome do processo
            return (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 32 ? p.ProcessName : description.Trim(), exe);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return ("Processo " + pid, "pid" + pid);   // saiu entre a leitura e a consulta
        }
    }

    /// <summary>Sessões do mesmo aplicativo viram uma entrada: volume = o maior, mudo = todas mudas, ativo = alguma ativa.</summary>
    public static List<AppEntry> Group(IEnumerable<(string Key, string Name, string Process, float Volume, bool Muted, bool Active)> raw) =>
        raw.GroupBy(r => r.Key)
            .Select(g => new AppEntry(
                GroupId(g.Key), g.First().Name, g.First().Process,
                (int)Math.Round(Math.Clamp(g.Max(r => r.Volume), 0f, 1f) * 100),
                g.All(r => r.Muted), g.Any(r => r.Active)))
            .OrderByDescending(a => a.Active).ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Id opaco e estável do aplicativo no mixer (8 hex do SHA-256 da chave), para não expor caminhos.</summary>
    public static string GroupId(string key) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..8].ToLowerInvariant();

    // ---------------------------------------------------------------- argumentos

    /// <summary><c>args.value</c> numérico, limitado a 0..100.</summary>
    public static bool TryParseVolume(JsonElement args, out int percent)
    {
        percent = 0;
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("value", out var v)
            || v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var d) || !double.IsFinite(d))
            return false;
        percent = (int)Math.Round(Math.Clamp(d, 0, 100));
        return true;
    }

    /// <summary><c>args.id</c> (texto) e, se <paramref name="needValue"/>, <c>args.value</c> de 0 a 100.</summary>
    public static bool TryParseSession(JsonElement args, bool needValue, out string id, out int percent)
    {
        id = "";
        percent = 0;
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("id", out var i)
            || i.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(i.GetString())) return false;
        id = i.GetString()!;
        return !needValue || TryParseVolume(args, out percent);
    }
}
