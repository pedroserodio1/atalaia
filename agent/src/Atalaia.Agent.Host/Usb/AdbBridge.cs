// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Atalaia.Agent.Host.Usb;

/// <summary>Resultado de uma chamada ao <c>adb</c>: código de saída e o texto de saída e de erro juntos.</summary>
public sealed record AdbResult(int ExitCode, string Output);

/// <summary>Costura para testar sem o <c>adb</c> de verdade. Lança <see cref="Win32Exception"/> se o executável não existe.</summary>
public interface IAdbRunner
{
    Task<AdbResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct);
}

public sealed class ProcessAdbRunner(string exe, TimeSpan? timeout = null) : IAdbRunner
{
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(8);

    /// <remarks>
    /// Não espera o fim dos canais de saída: quando o servidor do <c>adb</c> precisa subir, ele herda esses canais e os mantém
    /// abertos para sempre, então esperar o EOF travaria (ou demoraria até o prazo). Só o fim do <c>adb</c> cliente conta; o
    /// texto já recebido nesse momento é o resultado.
    /// </remarks>
    public async Task<AdbResult> RunAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = new Process { StartInfo = psi };
        var text = new System.Text.StringBuilder();
        void Add(object _, DataReceivedEventArgs e) { if (e.Data is not null) lock (text) text.AppendLine(e.Data); }
        p.OutputDataReceived += Add;
        p.ErrorDataReceived += Add;
        if (!p.Start()) throw new Win32Exception($"Não consegui iniciar {exe}");
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        var deadline = DateTime.UtcNow + _timeout;
        while (!p.HasExited)
        {
            if (DateTime.UtcNow >= deadline)
            {
                try { p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                return new AdbResult(-1, "tempo esgotado");
            }
            await Task.Delay(20, ct);   // cancelar (agente fechando) lança aqui; o processo é encerrado pelo `using`
        }
        await Task.Delay(100, CancellationToken.None);   // dá tempo de chegarem as últimas linhas já escritas
        lock (text) return new AdbResult(p.ExitCode, text.ToString());
    }
}

/// <summary>Configuração <c>Usb:*</c>. Desligado por padrão.</summary>
public sealed record UsbOptions(bool Enabled, string AdbPath, int PollSeconds, string Serial)
{
    public static UsbOptions From(IConfiguration config)
    {
        var s = config.GetSection("Usb");
        return new UsbOptions(
            s.GetValue("Enabled", false),
            s["AdbPath"]?.Trim() ?? "",
            Math.Clamp(s.GetValue("PollSeconds", 3), 1, 60),
            s["Serial"]?.Trim() ?? "");
    }
}

/// <summary>Onde está o <c>adb</c>: o caminho configurado; senão o do PATH; senão <c>ANDROID_HOME</c>/<c>ANDROID_SDK_ROOT</c>.</summary>
public static class AdbLocator
{
    /// <returns>O caminho (ou nome) a executar; <c>null</c> se não achou em lugar nenhum.</returns>
    public static string? Resolve(string configured, Func<string, string?> env, Func<string, bool> fileExists)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return configured;

        foreach (var dir in (env("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(dir.Trim('"'), "adb.exe");
            if (fileExists(candidate)) return candidate;
        }
        foreach (var variable in new[] { "ANDROID_HOME", "ANDROID_SDK_ROOT" })
        {
            var root = env(variable);
            if (string.IsNullOrWhiteSpace(root)) continue;
            var candidate = Path.Combine(root, "platform-tools", "adb.exe");
            if (fileExists(candidate)) return candidate;
        }
        return null;
    }
}

public enum AdbState { Ok, AdbMissing, AdbFailed }

/// <param name="State">Estado do aparelho segundo o <c>adb devices</c>: <c>device</c>, <c>unauthorized</c>, <c>offline</c>...</param>
/// <param name="ReverseCreated">Nesta rodada o agente refez o <c>adb reverse</c> para este aparelho.</param>
public sealed record DeviceReport(string Serial, string State, bool ReverseCreated, bool ReverseFailed);

public sealed record TickReport(AdbState Adb, IReadOnlyList<DeviceReport> Devices);

/// <summary>
/// Mantém <c>adb reverse tcp:PORTA tcp:PORTA</c> ativo para todo tablet autorizado ligado por USB, para o app alcançar o
/// agente em 127.0.0.1 sem Wi-Fi. A cada rodada confere os aparelhos e refaz a regra que faltar, o que cobre o cabo
/// replugado e o servidor do <c>adb</c> reiniciado. Só lê o que o <c>adb</c> informa e cria a regra de reverse; nada mais.
/// </summary>
public sealed class AdbBridge(UsbOptions options, int port, IAdbRunner runner, ILogger logger, Func<long>? nowMs = null)
{
    private readonly Func<long> _now = nowMs ?? (() => Environment.TickCount64);
    private long _emptySince = -1;      // desde quando `adb devices` não lista nenhum aparelho (-1 = lista não vazia)
    private long _lastEmptyLog;
    private readonly Dictionary<string, string> _lastState = [];   // para logar só mudanças
    private AdbState? _lastAdb;

    public async Task RunAsync(CancellationToken ct)
    {
        logger.LogInformation("USB: mantendo adb reverse tcp:{Port} (a cada {Poll} s)", port, options.PollSeconds);
        while (!ct.IsCancellationRequested)
        {
            try { await TickAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning(ex, "USB: rodada falhou"); }
            try { await Task.Delay(TimeSpan.FromSeconds(options.PollSeconds), ct); }
            catch (OperationCanceledException) { return; }
        }
    }

    /// <summary>Uma rodada. Nunca lança por causa do <c>adb</c> (ausente, travado, saída estranha): devolve o estado.</summary>
    public async Task<TickReport> TickAsync(CancellationToken ct)
    {
        AdbResult devices;
        try { devices = await runner.RunAsync(["devices"], ct); }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
        {
            Note(AdbState.AdbMissing, "USB: não achei o adb. Instale o platform-tools do Android e ponha no PATH, ou configure Usb:AdbPath.");
            _lastState.Clear();
            return new TickReport(AdbState.AdbMissing, []);
        }
        if (devices.ExitCode != 0)
        {
            Note(AdbState.AdbFailed, $"USB: 'adb devices' falhou (código {devices.ExitCode}).");
            return new TickReport(AdbState.AdbFailed, []);
        }
        Note(AdbState.Ok, "USB: adb encontrado.");

        var found = ParseDevices(devices.Output)
            .Where(d => options.Serial.Length == 0 || d.Serial == options.Serial)
            .ToList();
        WatchEmpty(found.Count);

        // aparelho que saiu: esquece, para logar de novo quando voltar
        foreach (var gone in _lastState.Keys.Where(k => found.All(d => d.Serial != k)).ToList())
        {
            logger.LogInformation("USB: aparelho {Serial} saiu.", gone);
            _lastState.Remove(gone);
        }

        var reports = new List<DeviceReport>();
        foreach (var (serial, state) in found)
        {
            if (state != "device")
            {
                LogState(serial, state, state == "unauthorized"
                    ? $"USB: aparelho {serial} não autorizado. Aceite a depuração USB na tela do tablet."
                    : $"USB: aparelho {serial} está '{state}'.");
                reports.Add(new DeviceReport(serial, state, false, false));
                continue;
            }

            var list = await runner.RunAsync(["-s", serial, "reverse", "--list"], ct);
            if (list.ExitCode == 0 && HasReverse(list.Output, port))
            {
                LogState(serial, "ready", $"USB: aparelho {serial} com reverse tcp:{port} ativo.");
                reports.Add(new DeviceReport(serial, state, false, false));
                continue;
            }

            var made = await runner.RunAsync(["-s", serial, "reverse", $"tcp:{port}", $"tcp:{port}"], ct);
            if (made.ExitCode == 0)
            {
                logger.LogInformation("USB: reverse tcp:{Port} criado para {Serial}.", port, serial);
                _lastState[serial] = "ready";
                reports.Add(new DeviceReport(serial, state, true, false));
            }
            else
            {
                LogState(serial, "reverse-failed", $"USB: não consegui criar o reverse para {serial}: {made.Output.Trim()}");
                reports.Add(new DeviceReport(serial, state, false, true));
            }
        }
        return new TickReport(AdbState.Ok, reports);
    }

    /// <summary>
    /// O servidor do <c>adb</c> pode ficar com a lista vazia depois de uma queda do USB. Isso não gera nenhum outro log, então
    /// avisa aqui: ao esvaziar, e de novo a cada 30 s enquanto continuar vazio.
    /// </summary>
    private void WatchEmpty(int count)
    {
        var now = _now();
        if (count > 0)
        {
            if (_emptySince >= 0) logger.LogInformation("USB: o adb voltou a listar aparelho (depois de {Seconds} s sem nenhum).", (now - _emptySince) / 1000);
            _emptySince = -1;
            return;
        }
        if (_emptySince < 0) { _emptySince = now; _lastEmptyLog = now; return; }   // a saída do aparelho já foi logada
        if (now - _lastEmptyLog >= 30_000)
        {
            _lastEmptyLog = now;
            logger.LogWarning("USB: o adb está sem aparelho há {Seconds} s. Se o cabo está ligado, confira a depuração USB no tablet ou rode 'adb kill-server'.", (now - _emptySince) / 1000);
        }
    }

    /// <summary>Linhas <c>serial\testado</c> depois do cabeçalho. Ignora o cabeçalho, linhas vazias e avisos do servidor do adb.</summary>
    public static IReadOnlyList<(string Serial, string State)> ParseDevices(string output)
    {
        var list = new List<(string, string)>();
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var tab = line.IndexOf('\t');
            if (tab <= 0) continue;
            var serial = line[..tab].Trim();
            var state = line[(tab + 1)..].Trim();
            if (serial.Length > 0 && state.Length > 0) list.Add((serial, state));
        }
        return list;
    }

    /// <summary>A saída de <c>reverse --list</c> tem a linha <c>(reverse) tcp:PORTA tcp:PORTA</c> (aparelho → PC, mesma porta)?</summary>
    public static bool HasReverse(string output, int port)
    {
        var want = $"tcp:{port}";
        foreach (var raw in output.Split('\n'))
        {
            var t = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (t.Length >= 2 && t[^2] == want && t[^1] == want) return true;
        }
        return false;
    }

    private void LogState(string serial, string state, string message)
    {
        if (_lastState.TryGetValue(serial, out var previous) && previous == state) return;
        _lastState[serial] = state;
        logger.LogInformation("{Message}", message);
    }

    private void Note(AdbState state, string message)
    {
        if (_lastAdb == state) return;
        _lastAdb = state;
        if (state == AdbState.Ok) logger.LogInformation("{Message}", message); else logger.LogWarning("{Message}", message);
    }
}
