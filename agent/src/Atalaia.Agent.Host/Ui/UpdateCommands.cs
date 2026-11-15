// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Text.Json;
using System.Text.Json.Nodes;
using Atalaia.Agent.Host.Adb;
using Atalaia.Agent.Host.Install;
using Atalaia.Agent.Host.Updates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atalaia.Agent.Host.Ui;

/// <summary>Do que os comandos de atualização precisam fora do <see cref="UpdateService"/>: o adb e o APK de uma versão qualquer da release.</summary>
internal sealed record UpdateCommandServices(
    UpdateService Updates,
    AdbService Adb,
    Func<int> Port,                        // a porta do agente agora (o adb confere o túnel dela; ela muda se o usuário a troca)
    Func<string> Repository,
    Func<string, ApkProvider?> ApkFor);    // APK da release de uma versão (null = não dá para baixar)

/// <summary>
/// Os comandos da aba Atualizações. A página só pergunta e desenha: quem decide se há versão nova, baixa, confere o SHA-256 e executa o
/// instalador é o <see cref="UpdateService"/>. Notas da release são <b>dado de fora</b>: vão como texto e a página as mostra por
/// <c>textContent</c>. O link "Ver no GitHub" é montado aqui a partir do repositório configurado e ainda passa pelo <see cref="LinkPolicy"/>.
/// </summary>
internal sealed class UpdateCommands(UpdateCommandServices s, ILogger? logger = null)
{
    private readonly ILogger _log = logger ?? NullLogger.Instance;
    private const int MaxNotesChars = 20_000;
    private readonly InstallJob _apkJob = new();
    private string? _lastInstalled;   // última versão do app lida no tablet, para mostrar enquanto a instalação roda

    public void Register(CommandRouter r)
    {
        r.Register("updates.get", _ => Task.FromResult<JsonNode?>(Get()));
        r.Register("updates.check", _ => { s.Updates.StartCheck(); return Task.FromResult<JsonNode?>(Get()); });
        r.Register("updates.install", _ => Task.FromResult<JsonNode?>(Install()));
        r.Register("updates.skip", _ => { s.Updates.Skip(); return Task.FromResult<JsonNode?>(Get()); });
        r.Register("updates.setChannel", a => SetChannel(CommandRouter.Text(a, "channel", 16)));
        r.Register("updates.setAutoInstall", a => { s.Updates.SetAutoInstall(Bool(a, "enabled")); return Task.FromResult<JsonNode?>(Get()); });
        r.Register("updates.tablet", _ => TabletGet());
        r.Register("updates.apkUpdate", a => ApkUpdate(OptionalBool(a, "uninstallFirst") && Bool(a, "confirm")));
    }

    // ================================================================ o agente

    private JsonNode Get()
    {
        var st = s.Updates.Status;
        var opts = s.Updates.ReadSettings();
        return new JsonObject
        {
            ["version"] = s.Updates.InstalledVersion,
            ["devBuild"] = s.Updates.IsDevelopmentBuild,
            ["channel"] = UpdateChannels.Name(opts.Channel),
            ["autoCheck"] = opts.AutoCheck,
            ["autoInstall"] = opts.AutoInstall,
            ["checkHours"] = opts.CheckHours,
            ["state"] = st.State,
            ["message"] = st.Message,
            ["retryAt"] = st.RetryAt?.ToUnixTimeMilliseconds(),
            ["lastCheck"] = st.LastCheck?.ToUnixTimeMilliseconds(),
            ["progress"] = st.Progress,
            ["installError"] = st.InstallError,
            ["staged"] = st.Staged,
            ["skipped"] = st.SkippedVersion,
            ["available"] = st.Available is { } a ? AvailableJson(a) : null,
        };
    }

    private JsonNode AvailableJson(UpdateCandidate c)
    {
        var notes = c.Release.Body ?? "";
        return new JsonObject
        {
            ["version"] = c.Version.ToString(),
            ["tag"] = c.Release.Tag,
            ["prerelease"] = c.Version.IsBeta || c.Release.Prerelease,
            ["notes"] = notes.Length > MaxNotesChars ? notes[..MaxNotesChars] + "…" : notes,
            ["url"] = $"https://github.com/{s.Repository()}/releases/tag/{c.Release.Tag}",
            ["size"] = c.Installer.Size,
        };
    }

    private JsonNode? Install() =>
        s.Updates.StartInstall() ? Get() : throw new CommandException("nothing_to_install",
            s.Updates.Status.Busy ? "Já há uma atualização em andamento." : "Não há atualização para instalar. Use \"Verificar agora\".");

    private Task<JsonNode?> SetChannel(string channel)
    {
        s.Updates.SetChannel(UpdateChannels.Parse(channel) ?? throw new CommandException("invalid_args", "Canal desconhecido (use stable ou beta)."));
        return Task.FromResult<JsonNode?>(Get());
    }

    // ================================================================ o app do tablet

    /// <summary>O app do tablet fica atrás? Compara a versão instalada com a da release oferecida (ou, sem oferta, com a do próprio agente).</summary>
    private async Task<JsonNode?> TabletGet()
    {
        var status = await s.Adb.GetStatusAsync(s.Port(), CancellationToken.None);
        var device = status.Devices.FirstOrDefault(d => d.Device.State == AdbDeviceState.Authorized)?.Device;
        string? installed = null;
        if (device is null) _lastInstalled = null;
        // Durante a instalação o `pm` não é consultado: devolver `null` fazia a janela dizer "app não instalado" (#66). Vale a última versão conhecida.
        else if (_apkJob.Running) installed = _lastInstalled;
        else installed = _lastInstalled = await s.Adb.InstalledVersionAsync(device.Serial, CancellationToken.None);

        var target = TargetVersion();
        var behind = AtalaiaVersion.TryParse(installed, out var have) && target is { } want && have < want;
        return new JsonObject
        {
            ["adbFound"] = status.Found,
            ["device"] = device is null ? null : new JsonObject { ["serial"] = device.Serial, ["model"] = device.Model },
            ["installedVersion"] = installed,
            ["targetVersion"] = target?.ToString(),
            ["canUpdate"] = behind && device is not null,
            ["job"] = _apkJob.ToJson(),
        };
    }

    private AtalaiaVersion? TargetVersion()
    {
        if (s.Updates.Status.Available is { } a) return a.Version;
        return AtalaiaVersion.TryParse(s.Updates.InstalledVersion, out var own) ? own : null;
    }

    private async Task<JsonNode?> ApkUpdate(bool uninstallFirst)
    {
        if (_apkJob.Running) throw new CommandException("busy", "Já há uma instalação em andamento.");
        var target = TargetVersion() ?? throw new CommandException("no_apk", "Este é um build de desenvolvimento: escolha o APK pelo passo \"App no tablet\".");
        var status = await s.Adb.GetStatusAsync(s.Port(), CancellationToken.None);
        var device = status.Devices.FirstOrDefault(d => d.Device.State == AdbDeviceState.Authorized)?.Device
            ?? throw new CommandException("no_device", "Nenhum tablet autorizado pelo cabo.");
        var provider = s.ApkFor(target.ToString());
        if (provider is not { CanDownload: true }) throw new CommandException("no_apk", "Não há release publicada para baixar o app do tablet.");

        _apkJob.Start(ct => RunApkAsync(device.Serial, provider, uninstallFirst, ct));
        return TabletJob();
    }

    private JsonNode TabletJob() => new JsonObject { ["job"] = _apkJob.ToJson() };

    private async Task RunApkAsync(string serial, ApkProvider provider, bool uninstallFirst, CancellationToken ct)
    {
        try
        {
            _apkJob.Phase("download");
            var verified = await provider.DownloadAsync(new Progress<double>(_apkJob.Progress), ct);
            _apkJob.Phase("install");
            if (uninstallFirst)
            {
                var removed = await s.Adb.UninstallTabletAsync(serial, ct);
                if (!removed.Ok) { _apkJob.Fail(removed.Message, removed.Outcome.ToString()); return; }
            }
            var result = await s.Adb.InstallAsync(serial, ApkFile.From(verified), ct);
            if (result.Ok) _apkJob.Succeed(result.Message); else _apkJob.Fail(result.Message, result.Outcome.ToString());
        }
        catch (GitHubException ex) { _apkJob.Fail(ex.Message, "Download"); }
        catch (ChecksumException ex) { _apkJob.Fail(ex.Message, "Checksum"); }
        catch (Exception ex)
        {
            _log.LogError(ex, "A atualização do app no tablet falhou.");
            _apkJob.Fail("A instalação falhou. Veja o log do agente.", "Other");
        }
    }

    // ================================================================ argumentos

    private static bool Bool(JsonElement a, string name) =>
        a.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean()
            : throw new CommandException("invalid_args", $"Falta o valor '{name}' (verdadeiro ou falso).");

    private static bool OptionalBool(JsonElement a, string name) => a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
}
