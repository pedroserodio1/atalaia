// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.ComponentModel;
using Atalaia.Agent.Host.Usb;

namespace Atalaia.Agent.Host.Adb;

/// <param name="Tunnel"><c>true</c> se o <c>adb reverse</c> da porta do agente existe neste aparelho; <c>null</c> se não deu para saber (aparelho não autorizado).</param>
internal sealed record DeviceStatus(AdbDevice Device, bool? Tunnel);

/// <param name="Source"><c>config</c>, <c>path</c>, <c>android_home</c> ou <c>none</c>.</param>
/// <param name="Problem">Por que não deu para falar com o <c>adb</c> (caminho inexistente, falha ao executar), ou <c>null</c>.</param>
internal sealed record AdbStatus(string? Path, string Source, bool Found, string? Problem, IReadOnlyList<DeviceStatus> Devices);

/// <summary>
/// Fala com o <c>adb</c> para a janela: achar o executável, listar os aparelhos com o estado de verdade, ler a versão do app do tablet,
/// instalar e desinstalar o app do Atalaia. Só usa os comandos de <see cref="AdbCommands"/>. As costuras (<c>runnerFactory</c>, ambiente
/// e arquivos) deixam testar sem o <c>adb</c> nem o Windows.
/// </summary>
internal sealed class AdbService(
    Func<string> configuredPath,
    Func<string, string?> env,
    Func<string, bool> fileExists,
    Func<string, TimeSpan, IAdbRunner> runnerFactory)
{
    private static readonly TimeSpan Quick = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan Install = TimeSpan.FromMinutes(3);

    /// <summary>O caminho do <c>adb</c> e de onde veio. <paramref name="Found"/> só é verdadeiro se o arquivo existe (ou está no PATH, o que já foi conferido).</summary>
    public (string? Path, string Source, bool Found) Locate()
    {
        var (path, source) = AdbLocator.ResolveWithSource(configuredPath(), env, fileExists);
        return (path, source, path is not null && (source != "config" || fileExists(path)));
    }

    public async Task<AdbStatus> GetStatusAsync(int agentPort, CancellationToken ct)
    {
        var (path, source, found) = Locate();
        if (!found)
            return new AdbStatus(path, source, false, path is null ? null : "O arquivo informado não existe.", []);

        IReadOnlyList<AdbDevice> devices;
        var runner = runnerFactory(path!, Quick);
        try
        {
            var r = await runner.RunAsync(AdbCommands.Devices(), ct);
            if (r.ExitCode != 0) return new AdbStatus(path, source, true, "O adb não respondeu. Tente de novo em instantes.", []);
            devices = AdbDevices.Parse(r.Output);
        }
        catch (Win32Exception) { return new AdbStatus(path, source, false, "Não consegui executar o adb neste caminho.", []); }

        var list = new List<DeviceStatus>();
        foreach (var d in devices)
        {
            bool? tunnel = null;
            if (d.State == AdbDeviceState.Authorized && AdbCommands.IsValidSerial(d.Serial))
            {
                var rv = await runner.RunAsync(AdbCommands.ReverseList(d.Serial), ct);
                tunnel = rv.ExitCode == 0 && AdbBridge.HasAllReverses(rv.Output, agentPort);
            }
            list.Add(new DeviceStatus(d, tunnel));
        }
        return new AdbStatus(path, source, true, null, list);
    }

    /// <summary>A versão do app do Atalaia instalada no aparelho, ou <c>null</c> se não está instalado (ou não deu para ler).</summary>
    public async Task<string?> InstalledVersionAsync(string serial, CancellationToken ct)
    {
        var runner = RunnerOrNull(Quick);
        if (runner is null) return null;
        var r = await runner.RunAsync(AdbCommands.DumpPackage(serial), ct);
        return r.ExitCode == 0 ? TabletPackage.ParseVersionName(r.Output) : null;
    }

    /// <summary>Instala e, se deu certo, abre o app no tablet (o <c>install -r</c> deixa o app fechado). Falhar ao abrir não é erro de instalação.</summary>
    public async Task<InstallResult> InstallAsync(string serial, Install.ApkFile apk, CancellationToken ct)
    {
        var result = await RunInstallAsync(AdbCommands.Install(serial, apk.Path), ct);
        if (result.Outcome == InstallOutcome.Success) await TryStartTabletAsync(serial, ct);
        return result;
    }

    private async Task TryStartTabletAsync(string serial, CancellationToken ct)
    {
        try
        {
            var runner = RunnerOrNull(Quick);
            if (runner is not null) await runner.RunAsync(AdbCommands.StartTablet(serial), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { }   // o app já está instalado; abrir é só conveniência
    }

    public async Task<InstallResult> UninstallTabletAsync(string serial, CancellationToken ct)
    {
        var runner = RunnerOrNull(Quick) ?? throw new InvalidOperationException("O adb não foi encontrado.");
        var r = await runner.RunAsync(AdbCommands.UninstallTablet(serial), ct);
        return r.ExitCode == 0 && r.Output.Contains("Success", StringComparison.OrdinalIgnoreCase)
            ? new InstallResult(InstallOutcome.Success, "App removido do tablet.")
            : InstallErrorMapper.Map(r.ExitCode, r.Output);
    }

    private async Task<InstallResult> RunInstallAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var runner = RunnerOrNull(Install) ?? throw new InvalidOperationException("O adb não foi encontrado.");
        var r = await runner.RunAsync(args, ct);
        return InstallErrorMapper.Map(r.ExitCode, r.Output);
    }

    private IAdbRunner? RunnerOrNull(TimeSpan timeout)
    {
        var (path, _, found) = Locate();
        return found ? runnerFactory(path!, timeout) : null;
    }
}
