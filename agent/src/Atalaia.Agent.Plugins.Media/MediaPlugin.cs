// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Text.Json;
using Microsoft.Extensions.Logging;
using Atalaia.Agent.Core;
using Windows.Media.Control;

namespace Atalaia.Agent.Plugins.Media;

/// <summary>Mídia tocando via SMTC. Origem: <c>Smtc</c> do PcProbe. Nunca loga título, artista ou álbum.</summary>
public sealed class MediaPlugin : IPlugin
{
    private const int CoverReadsPerTrack = 4;   // na troca de faixa e nas 3 coletas seguintes
    private const int MaxCoverBytes = 8 * 1024 * 1024;
    private static readonly TimeSpan CoverTimeout = TimeSpan.FromSeconds(3);

    private IPluginContext _ctx = null!;
    private GlobalSystemMediaTransportControlsSessionManager _manager = null!;
    private string _trackKey = "";
    private int _coverReadsLeft;
    private string? _coverHash;

    public string Id => "media";
    public string Name => "Mídia";
    public int DefaultIntervalMs => 1000;
    public IReadOnlyList<ActionDescriptor> Actions { get; } =
    [
        new("play_pause"), new("play"), new("pause"), new("next"), new("previous"),
        new("seek", "args.positionSec em segundos, de 0 até a duração"),
    ];

    public async Task StartAsync(IPluginContext ctx, CancellationToken ct)
    {
        _ctx = ctx;
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    /// <summary>A mesma sessão que a coleta mostra: a atual, ou a primeira tocando.</summary>
    private GlobalSystemMediaTransportControlsSession? PickSession() =>
        _manager.GetCurrentSession()
        ?? _manager.GetSessions().FirstOrDefault(s =>
            s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing);

    public async Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct)
    {
        var session = PickSession();
        if (session is null) return new ActionResult(false, "no_session");

        bool accepted;
        switch (action)
        {
            case "play_pause": accepted = await session.TryTogglePlayPauseAsync(); break;
            case "play": accepted = await session.TryPlayAsync(); break;
            case "pause": accepted = await session.TryPauseAsync(); break;
            case "next": accepted = await session.TrySkipNextAsync(); break;
            case "previous": accepted = await session.TrySkipPreviousAsync(); break;
            case "seek":
                if (!TryParseSeek(args, out var seconds)) return new ActionResult(false, "invalid_args");
                accepted = await session.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(seconds).Ticks);
                break;
            default: return new ActionResult(false, "unknown_action");
        }

        if (!accepted) return new ActionResult(false, "not_supported");   // o app não aceita esse controle agora
        await Task.Delay(150, ct);   // dá tempo do SMTC refletir antes da coleta imediata
        return new ActionResult(true);
    }

    /// <summary><c>args.positionSec</c> numérico e não negativo.</summary>
    public static bool TryParseSeek(JsonElement args, out double seconds)
    {
        seconds = 0;
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("positionSec", out var v)
            || v.ValueKind != JsonValueKind.Number || !v.TryGetDouble(out var d) || !double.IsFinite(d) || d < 0) return false;
        seconds = d;
        return true;
    }

    public async Task<JsonElement?> CollectAsync(CancellationToken ct)
    {
        var session = PickSession();
        if (session is null)
        {
            _trackKey = "";
            _coverHash = null;
            return Json.ToElement(new
            {
                app = "", title = "", artist = "", album = "", status = "stopped",
                positionSec = 0, durationSec = 0, coverHash = (string?)null,
            });
        }

        var props = await session.TryGetMediaPropertiesAsync();
        var playback = session.GetPlaybackInfo();
        var timeline = session.GetTimelineProperties();

        var app = session.SourceAppUserModelId ?? "";
        if (app.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) app = app[..^4];

        var status = playback.PlaybackStatus switch
        {
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing => "playing",
            GlobalSystemMediaTransportControlsSessionPlaybackStatus.Paused => "paused",
            _ => "stopped",
        };

        var duration = Math.Max(0, (timeline.EndTime - timeline.StartTime).TotalSeconds);
        var position = timeline.Position.TotalSeconds;
        // O Position do SMTC só é atualizado em eventos: tocando, soma o tempo desde a última atualização.
        if (status == "playing" && timeline.LastUpdatedTime.Year > 2000)
            position += Math.Max(0, (DateTimeOffset.UtcNow - timeline.LastUpdatedTime).TotalSeconds);
        position = Math.Clamp(position, 0, duration);

        var key = $"{app}\u0001{props.Title}\u0001{props.Artist}\u0001{props.AlbumTitle}";
        if (key != _trackKey)
        {
            _trackKey = key;
            _coverReadsLeft = CoverReadsPerTrack;
            _coverHash = null;
        }
        if (_coverReadsLeft > 0)
        {
            _coverReadsLeft--;   // a miniatura costuma chegar depois dos metadados
            var bytes = await ReadCoverAsync(props, ct);
            if (bytes is { Length: > 0 }) _coverHash = _ctx.Blobs.Put(bytes);
        }

        return Json.ToElement(new
        {
            app,
            title = props.Title ?? "",
            artist = props.Artist ?? "",
            album = props.AlbumTitle ?? "",
            status,
            positionSec = (int)Math.Round(position),
            durationSec = (int)Math.Round(duration),
            coverHash = _coverHash,
        });
    }

    private async Task<byte[]?> ReadCoverAsync(GlobalSystemMediaTransportControlsSessionMediaProperties props, CancellationToken ct)
    {
        if (props.Thumbnail is null) return null;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(CoverTimeout);
            using var reader = await props.Thumbnail.OpenReadAsync().AsTask(cts.Token);
            if (reader.Size is 0 or > MaxCoverBytes) return null;
            using var stream = reader.AsStreamForRead();
            using var ms = new MemoryStream((int)reader.Size);
            await stream.CopyToAsync(ms, cts.Token);
            return ms.ToArray();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _ctx.Logger.LogDebug("Capa não lida: {Type}", ex.GetType().Name);   // sem dados da faixa
            return null;
        }
    }
}
