using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Atalaia.Agent.Core;

namespace Atalaia.Agent.Plugins.Gpu;

/// <summary>GPU NVIDIA via <c>nvidia-smi</c>. Origem: <c>Nvidia</c> do PcProbe.</summary>
public sealed class GpuPlugin : IPlugin
{
    private const string Query = "--query-gpu=name,temperature.gpu,utilization.gpu,memory.used,memory.total,power.draw --format=csv,noheader,nounits";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private string _exe = "nvidia-smi";

    public string Id => "gpu";
    public string Name => "GPU NVIDIA";
    public int DefaultIntervalMs => 2000;
    public IReadOnlyList<ActionDescriptor> Actions => [];

    public Task StartAsync(IPluginContext ctx, CancellationToken ct)
    {
        var path = ctx.Options["nvidiaSmiPath"];
        if (!string.IsNullOrWhiteSpace(path)) _exe = path;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    // ponytail: um processo por coleta; se pesar, trocar por `nvidia-smi -l` ou NVML.
    public async Task<JsonElement?> CollectAsync(CancellationToken ct)
    {
        var psi = new ProcessStartInfo(_exe, Query)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        Process process;
        try { process = Process.Start(psi) ?? throw new InvalidOperationException("nvidia-smi não iniciou"); }
        catch (Win32Exception ex) { throw new FileNotFoundException($"nvidia-smi não encontrado ({_exe})", ex); }

        using (process)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(Timeout);
            var stdout = process.StandardOutput.ReadToEndAsync(cts.Token);
            _ = process.StandardError.ReadToEndAsync(CancellationToken.None);   // esvazia o stderr para não travar o processo
            try
            {
                await process.WaitForExitAsync(cts.Token);
                var text = await stdout;
                if (process.ExitCode != 0) throw new InvalidOperationException($"nvidia-smi saiu com código {process.ExitCode}");
                return ParseLine(text);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException("nvidia-smi passou de 5 s");
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw;
            }
        }
    }

    public Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct) =>
        Task.FromResult(new ActionResult(false, "unknown_action"));

    /// <summary>Primeira linha do CSV (sem cabeçalho, sem unidades). Valor <c>[N/A]</c> ou não numérico vira <c>null</c>.</summary>
    public static JsonElement ParseLine(string output)
    {
        var line = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault()
            ?? throw new InvalidDataException("nvidia-smi não devolveu nenhuma linha");
        var f = line.Split(',').Select(x => x.Trim()).ToArray();
        if (f.Length < 6) throw new InvalidDataException("nvidia-smi devolveu menos de 6 campos");

        return Json.ToElement(new
        {
            name = f[0],
            tempC = Int(f[1]),
            utilPercent = Int(f[2]),
            vramUsedMb = Int(f[3]),
            vramTotalMb = Int(f[4]),
            powerW = Dec(f[5]),
        });
    }

    // InvariantCulture: o Windows daqui é pt-BR e o nvidia-smi devolve "10.77".
    private static double? Num(string s) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && double.IsFinite(v) ? v : null;

    private static int? Int(string s) => Num(s) is { } v ? (int)Math.Round(v) : null;
    private static double? Dec(string s) => Num(s) is { } v ? Math.Round(v, 1) : null;
}
