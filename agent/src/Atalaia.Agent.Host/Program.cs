using System.Runtime.InteropServices;
using Serilog;
using Serilog.Events;

namespace Atalaia.Agent.Host;

internal static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AllocConsole();

    [STAThread]
    private static int Main(string[] args)
    {
        var simulate = args.Contains("--simulate");
        var console = args.Contains("--console");
        if (console) AllocConsole();

        using var instance = new Mutex(true, @"Local\Atalaia.Agent.Host.SingleInstance", out var first);
        if (!first)
        {
            Warn("O Atalaia já está rodando (veja o ícone na bandeja).", console);
            return 0;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        AgentRuntime? runtime = null;
        try
        {
            var config = HostConfig.Build();
            ConfigureLog(console);
            runtime = new AgentRuntime(config, simulate, TokenStore.Load(config));
            Task.Run(runtime.StartAsync).GetAwaiter().GetResult();   // fora da thread da interface

            if (console)   // o token só aparece aqui (e em "Copiar token"), nunca no arquivo de log
            {
                Console.WriteLine($"Agente em ws://{runtime.Address}/ws{(simulate ? " (simulado)" : "")}");
                Console.WriteLine(runtime.TokenCreated ? $"Código de acesso: {runtime.Token}   (novo a cada início)" : $"Token: {runtime.Token}");
            }

            Application.Run(new Tray(runtime));
            Task.Run(runtime.DisposeAsync().AsTask).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Falha ao subir o agente");
            Warn($"Não foi possível iniciar o Atalaia:\n\n{ex.Message}\n\nDetalhes em {AppPaths.Logs}", console);
            return 1;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void ConfigureLog(bool console)
    {
        var cfg = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .WriteTo.File(Path.Combine(AppPaths.Logs, "agent-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 14);
        if (console) cfg.WriteTo.Console();
        Log.Logger = cfg.CreateLogger();
    }

    private static void Warn(string message, bool console)
    {
        if (console) Console.WriteLine(message);
        MessageBox.Show(message, "Atalaia", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }
}
