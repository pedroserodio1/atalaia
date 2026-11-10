// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Nodes;
using Atalaia.Agent.Host.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atalaia.Agent.Host.Updates;

/// <summary>
/// O que a tela mostra da atualização. <see cref="State"/>: <c>unknown</c> (ainda sem checagem, ou build de desenvolvimento),
/// <c>checking</c>, <c>current</c>, <c>available</c>, <c>offline</c>, <c>rate_limited</c> (com <see cref="RetryAt"/>), <c>error</c>,
/// <c>downloading</c>, <c>verifying</c> e <c>installing</c>. Falta de internet e limite da API são <b>estados</b>, nunca erro fatal.
/// </summary>
internal sealed record UpdateStatus(
    string State,
    string? Message = null,
    DateTimeOffset? RetryAt = null,
    DateTimeOffset? LastCheck = null,
    UpdateCandidate? Available = null,
    string? SkippedVersion = null,   // a versão que o usuário dispensou e que ainda é a mais nova (State = current)
    bool Staged = false,             // já baixada e conferida: será instalada quando o agente for fechado (Instalar sozinho)
    double Progress = 0,
    string? InstallError = null)    // a última tentativa de instalar falhou (State volta a available: nada mudou)
{
    public bool Busy => State is "checking" or "downloading" or "verifying" or "installing";
}

/// <summary>As opções <c>Updates:*</c> do <c>settings.json</c>, com os padrões.</summary>
internal sealed record UpdateSettings(bool AutoCheck, UpdateChannel Channel, int CheckHours, string? SkippedVersion, bool AutoInstall)
{
    public const int DefaultCheckHours = 6;
}

/// <summary>
/// A atualização automática, em segundo plano: a primeira checagem ~30 s depois da partida e depois a cada <c>Updates:CheckHours</c> (6 h),
/// só com <c>Updates:AutoCheck</c> ligado. Consulta a lista de releases (com <c>ETag</c>, então 304 não gasta limite), escolhe a versão pelo
/// <see cref="ChannelSelector"/> e, na hora que o usuário pede, baixa, confere e executa o instalador (<see cref="UpdateApplier"/>).
/// </summary>
internal sealed class UpdateService(SettingsStore settings, Func<GitHubReleaseClient> client, string installedVersion, UpdateService.Hooks hooks, ILogger? logger = null)
{
    /// <summary>Costuras do sistema (o teste troca): onde guardar o instalador, como executá-lo, como fechar o agente e os relógios.</summary>
    internal sealed record Hooks(string UpdatesDir, Action<string, string> Launch, Action ExitApp)
    {
        public TimeSpan FirstDelay { get; init; } = TimeSpan.FromSeconds(30);
        public TimeSpan Tick { get; init; } = TimeSpan.FromMinutes(1);          // de quanto em quanto tempo o laço olha se chegou a hora (e se o AutoCheck ligou)
        public TimeSpan ExitDelay { get; init; } = TimeSpan.FromSeconds(2);     // deixa a tela mostrar "reiniciando" antes de o agente sair
        public Func<DateTimeOffset> Now { get; init; } = () => DateTimeOffset.UtcNow;
    }

    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromMinutes(30);
    private readonly ILogger _log = logger ?? NullLogger.Instance;
    private readonly object _lock = new();
    private readonly AtalaiaVersion? _installed = AtalaiaVersion.TryParse(installedVersion, out var v) ? v : null;   // null = build de desenvolvimento
    private UpdateStatus _status = new("unknown", Message: DevMessage(installedVersion), LastCheck: ReadLastCheck(settings));
    private IReadOnlyList<ReleaseInfo>? _releases;
    private string? _etag;
    private (AtalaiaVersion Version, VerifiedFile File)? _staged;

    public string InstalledVersion => installedVersion;
    public bool IsDevelopmentBuild => _installed is null;

    public UpdateStatus Status
    {
        get { lock (_lock) return _status; }
    }

    private static string? DevMessage(string version) =>
        AtalaiaVersion.TryParse(version, out _) ? null : "Este é um build de desenvolvimento: ele não se atualiza sozinho.";

    /// <summary>A hora da última checagem gravada em <c>Updates:LastCheck</c> (ISO-8601), ou <c>null</c>.</summary>
    private static DateTimeOffset? ReadLastCheck(SettingsStore store) =>
        store.Read()["Updates"]?["LastCheck"] is JsonValue v && v.TryGetValue<string>(out var text)
        && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at) ? at : null;

    // ================================================================ opções

    public UpdateSettings ReadSettings()
    {
        var u = settings.Read()["Updates"] as JsonObject;
        bool Bool(string key, bool fallback) => u?[key] is JsonValue val && val.TryGetValue<bool>(out var b) ? b : fallback;
        var channel = u?["Channel"] is JsonValue c && c.TryGetValue<string>(out var name) && UpdateChannels.Parse(name) is { } parsed
            ? parsed : UpdateChannels.DefaultFor(_installed ?? default);
        var hours = u?["CheckHours"] is JsonValue h && h.TryGetValue<int>(out var n) ? Math.Clamp(n, 1, 168) : UpdateSettings.DefaultCheckHours;
        var skipped = u?["SkippedVersion"] is JsonValue s && s.TryGetValue<string>(out var text) && AtalaiaVersion.TryParse(text, out _) ? text : null;
        return new UpdateSettings(Bool("AutoCheck", true), channel, hours, skipped, Bool("AutoInstall", false));
    }

    public void SetChannel(UpdateChannel channel)
    {
        settings.Set("Updates:Channel", UpdateChannels.Name(channel));
        Reevaluate();
    }

    public void SetAutoInstall(bool enabled) => settings.Set("Updates:AutoInstall", enabled);

    /// <summary>"Lembrar depois": não oferece de novo esta versão (uma mais nova volta a ser oferecida).</summary>
    public void Skip()
    {
        UpdateCandidate? available;
        lock (_lock) available = _status.Busy ? null : _status.Available;
        if (available is null) return;
        settings.Set("Updates:SkippedVersion", available.Version.ToString());
        Reevaluate();
    }

    // ================================================================ checagem

    /// <summary>O laço de segundo plano. Termina só quando <paramref name="ct"/> é cancelado; nunca lança por causa da rede.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            var due = hooks.Now() + hooks.FirstDelay;
            while (!ct.IsCancellationRequested)
            {
                var wait = due - hooks.Now();
                if (wait > hooks.Tick) wait = hooks.Tick;
                if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
                if (hooks.Now() < due) continue;

                var opts = ReadSettings();
                if (!opts.AutoCheck) { due = hooks.Now() + hooks.Tick; continue; }   // ligou depois? a próxima olhada pega
                await CheckAsync(ct);
                due = hooks.Now() + NextWait(opts);
            }
        }
        catch (OperationCanceledException) { }
    }

    private TimeSpan NextWait(UpdateSettings opts)
    {
        var interval = TimeSpan.FromHours(opts.CheckHours);
        var status = Status;
        return status.State switch
        {
            "offline" or "error" => TimeSpan.Compare(RetryAfterFailure, interval) < 0 ? RetryAfterFailure : interval,
            "rate_limited" when status.RetryAt is { } at => Clamp(at - hooks.Now() + TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), interval),
            _ => interval,
        };

        static TimeSpan Clamp(TimeSpan value, TimeSpan min, TimeSpan max) => value < min ? min : value > max ? max : value;
    }

    /// <summary>Consulta o GitHub agora. Se já há algo em andamento (outra checagem, um download), não faz nada.</summary>
    public async Task CheckAsync(CancellationToken ct)
    {
        if (_installed is null || !TryBegin("checking")) return;   // build de desenvolvimento: não há o que comparar
        await CheckCoreAsync(ct);
    }

    /// <summary>"Verificar agora" da tela: marca "verificando" na hora e consulta em segundo plano (a tela acompanha pelo <see cref="Status"/>).</summary>
    public bool StartCheck()
    {
        if (_installed is null || !TryBegin("checking")) return false;
        _ = Task.Run(() => CheckCoreAsync(CancellationToken.None));
        return true;
    }

    private async Task CheckCoreAsync(CancellationToken ct)
    {
        UpdateCandidate? toStage = null;
        try
        {
            var github = client();
            var list = await github.ListAsync(_releases is null ? null : _etag, ct);
            var now = hooks.Now();
            lock (_lock)
            {
                if (!list.NotModified) { _releases = list.Releases; _etag = list.ETag; }
                _status = _status with { LastCheck = now, RetryAt = null };
            }
            settings.Set("Updates:LastCheck", now.ToString("O", CultureInfo.InvariantCulture));
            toStage = Evaluate();
        }
        catch (GitHubException ex)
        {
            _log.LogInformation("Checagem de atualização: {Kind}", ex.Kind);   // só o tipo: nada pessoal
            lock (_lock)
            {
                _status = ex.Kind switch
                {
                    GitHubFailure.Offline => _status with { State = "offline", Message = ex.Message },
                    GitHubFailure.RateLimited => _status with { State = "rate_limited", Message = ex.Message, RetryAt = ex.RetryAt },
                    GitHubFailure.NotFound => _status with { State = "error", Message = "Não encontrei o repositório de atualizações no GitHub." },
                    _ => _status with { State = "error", Message = ex.Message },
                };
            }
        }
        catch (OperationCanceledException) { lock (_lock) _status = _status with { State = _status.Available is null ? "unknown" : "available" }; throw; }
        catch (Exception ex)
        {
            _log.LogError(ex, "A checagem de atualização falhou.");
            lock (_lock) _status = _status with { State = "error", Message = "Não foi possível buscar atualizações. Veja o log do agente." };
        }

        if (toStage is not null) await StageAsync(toStage, ct);   // "Instalar sozinho": baixa e confere desde já
    }

    /// <summary>Calcula o estado a partir da lista guardada (sem rede). Devolve a versão a baixar antes, se "Instalar sozinho" está ligado.</summary>
    private UpdateCandidate? Evaluate()
    {
        var opts = ReadSettings();
        lock (_lock)
        {
            if (_releases is null || _installed is not { } installed) { _status = _status with { State = "unknown" }; return null; }
            var best = ChannelSelector.Select(_releases, opts.Channel, installed, opts.SkippedVersion);
            var dismissed = best is null && opts.SkippedVersion is not null ? ChannelSelector.SelectIgnoringSkip(_releases, opts.Channel, installed) : null;
            var staged = best is not null && _staged is { } st && st.Version == best.Version;
            _status = best is not null
                ? _status with { State = "available", Available = best, SkippedVersion = null, Message = null, InstallError = null, Progress = 0, Staged = staged }
                : _status with { State = "current", Available = null, SkippedVersion = dismissed?.Version.ToString(), Message = null, InstallError = null, Progress = 0, Staged = false };
            if (best is not null) _log.LogInformation("Atualização disponível: {Version}", best.Version);
            return best is not null && opts.AutoInstall && !staged ? best : null;
        }
    }

    private void Reevaluate()
    {
        lock (_lock) if (_status.Busy || _status.State is not ("available" or "current")) return;
        Evaluate();
    }

    private bool TryBegin(string state)
    {
        lock (_lock)
        {
            if (_status.Busy) return false;
            _status = _status with { State = state, Progress = 0, InstallError = null };
            return true;
        }
    }

    // ================================================================ instalar

    /// <summary>
    /// "Baixar e instalar": baixa, confere e executa o instalador em segundo plano; a tela acompanha pelo <see cref="Status"/>. Quando o instalador
    /// começa, o agente fecha (o instalador o reabre). Devolve <c>false</c> se não há versão oferecida ou já há algo em andamento.
    /// </summary>
    public bool StartInstall()
    {
        UpdateCandidate? candidate;
        lock (_lock)
        {
            candidate = _status.Busy ? null : _status.Available;
            if (candidate is null || _status.State != "available") return false;
        }
        if (!TryBegin("downloading")) return false;
        _ = Task.Run(() => InstallAsync(candidate));
        return true;
    }

    private async Task InstallAsync(UpdateCandidate candidate)
    {
        try
        {
            var applier = NewApplier();
            var file = StagedFor(candidate) ?? await DownloadAsync(applier, candidate, CancellationToken.None);
            Set(s => s with { State = "installing", Progress = 1, Message = "Instalando. O Atalaia vai fechar e reabrir sozinho." });
            applier.Launch(file, candidate.Version);
            _log.LogInformation("Instalador da versão {Version} iniciado: o agente vai fechar.", candidate.Version);
            if (hooks.ExitDelay > TimeSpan.Zero) await Task.Delay(hooks.ExitDelay);
            hooks.ExitApp();
        }
        catch (Exception ex) { FailInstall(ex); }
    }

    /// <summary>Baixa e confere o instalador agora, sem instalar: ele será executado quando o agente for fechado.</summary>
    private async Task StageAsync(UpdateCandidate candidate, CancellationToken ct)
    {
        if (!TryBegin("downloading")) return;
        try
        {
            var file = await DownloadAsync(NewApplier(), candidate, ct);
            lock (_lock)
            {
                _staged = (candidate.Version, file);
                _status = _status with { State = "available", Progress = 0, Staged = true };
            }
            _log.LogInformation("Atualização {Version} baixada e conferida: será instalada quando o agente for fechado.", candidate.Version);
        }
        catch (OperationCanceledException) { lock (_lock) _status = _status with { State = "available" }; throw; }
        catch (Exception ex) { FailInstall(ex); }
    }

    private async Task<VerifiedFile> DownloadAsync(UpdateApplier applier, UpdateCandidate candidate, CancellationToken ct)
    {
        var progress = new InlineProgress(p => Set(s => s with { Progress = p }));
        return await applier.DownloadAsync(candidate, progress, phase =>
            Set(s => s with { State = phase == "verify" ? "verifying" : "downloading", Progress = 0 }), ct);
    }

    private UpdateApplier NewApplier() => new(client(), hooks.UpdatesDir, hooks.Launch);

    private VerifiedFile? StagedFor(UpdateCandidate c)
    {
        lock (_lock) return _staged is { } s && s.Version == c.Version ? s.File : null;
    }

    private void FailInstall(Exception ex)
    {
        var message = ex switch
        {
            GitHubException or ChecksumException or InvalidOperationException => ex.Message,
            Win32Exception => "Não foi possível executar o instalador. A versão atual continua valendo.",
            IOException => "Não foi possível gravar o instalador no disco. A versão atual continua valendo.",
            _ => "A atualização falhou. A versão atual continua valendo. Veja o log do agente.",
        };
        if (ex is not (GitHubException or ChecksumException)) _log.LogError(ex, "A atualização falhou antes de instalar.");
        lock (_lock)
        {
            if (ex is ChecksumException) _staged = null;
            _status = _status with { State = _status.Available is null ? "error" : "available", InstallError = message, Progress = 0, Staged = false, Message = _status.Available is null ? message : null };
        }
    }

    /// <summary>
    /// Chamado quando o agente vai fechar por escolha do usuário (<b>Sair</b> da bandeja): com "Instalar sozinho" ligado e a atualização já
    /// baixada e conferida, executa o instalador. Devolve <c>true</c> se o instalador foi iniciado.
    /// </summary>
    public bool ApplyStagedOnExit()
    {
        (AtalaiaVersion Version, VerifiedFile File)? staged;
        lock (_lock) staged = _status.Busy ? null : _staged;
        if (staged is not { } s || !ReadSettings().AutoInstall) return false;
        try
        {
            NewApplier().Launch(s.File, s.Version);
            _log.LogInformation("Instalador da versão {Version} iniciado ao fechar o agente.", s.Version);
            return true;
        }
        catch (Exception ex) when (ex is ChecksumException or InvalidOperationException or Win32Exception or IOException)
        {
            _log.LogWarning(ex, "Não foi possível aplicar a atualização ao fechar.");
            return false;
        }
    }

    private void Set(Func<UpdateStatus, UpdateStatus> change)
    {
        lock (_lock) _status = change(_status);
    }

    private sealed class InlineProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }
}
