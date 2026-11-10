// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Runtime.InteropServices;
using Atalaia.Agent.Host.Settings;
using Atalaia.Agent.Host.Ui;
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

        // Uma instância só. Quem tenta abrir de novo não sobe outro agente: pede à primeira que traga a janela para a frente.
        using var showWindow = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Atalaia.Agent.Host.Show");
        using var instance = new Mutex(true, @"Local\Atalaia.Agent.Host.SingleInstance", out var first);
        if (!first)
        {
            showWindow.Set();
            if (console) Console.WriteLine("O Atalaia já está rodando: a janela foi trazida para a frente.");
            return 0;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);   // sem isso o WebView2 sai borrado com zoom diferente de 100%
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        AgentRuntime? runtime = null;
        WindowHost? window = null;
        try
        {
            var config = HostConfig.Build();
            ConfigureLog(console);
            if (HostConfig.SettingsQuarantined is { } bad)
                Log.Warning("O settings.json estava inválido: foi guardado em {Path} e o agente começou com a configuração padrão.", bad);
            runtime = new AgentRuntime(config, simulate, TokenStore.Load(config));
            Task.Run(runtime.StartAsync).GetAwaiter().GetResult();   // fora da thread da interface

            if (console)   // o token só aparece aqui (e em "Copiar token"), nunca no arquivo de log
            {
                Console.WriteLine($"Agente em ws://{runtime.Address}/ws{(simulate ? " (simulado)" : "")}");
                Console.WriteLine(runtime.TokenCreated ? $"Código de acesso: {runtime.Token}   (novo a cada início)" : $"Token: {runtime.Token}");
            }

            var onboarding = new OnboardingStore(SettingsStore.Default);
            window = new WindowHost(runtime, onboarding, () => RepositoryFrom(config));
            var tray = new Tray(runtime, window);

            // Outra instância pediu a janela: abre/traz para a frente (Show pode ser chamado de qualquer thread).
            var wait = ThreadPool.RegisterWaitForSingleObject(showWindow, (_, _) => window.Show(), null, Timeout.Infinite, executeOnlyOnce: false);

            // Primeira execução: abre o onboarding sozinho, assim que o laço de mensagens estiver rodando.
            if (onboarding.State.AutoOpen)
            {
                EventHandler? once = null;
                once = (_, _) => { Application.Idle -= once; window.Show(); };
                Application.Idle += once;
            }

            Application.Run(tray);
            wait.Unregister(null);
            window.Dispose();
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

    /// <summary>O repositório do GitHub (dono/nome) de <c>Updates:Repository</c>; valor ausente ou inválido volta ao padrão.</summary>
    private static string RepositoryFrom(Microsoft.Extensions.Configuration.IConfiguration config) =>
        config["Updates:Repository"] is { } r && LinkPolicy.IsValidRepository(r) ? r : LinkPolicy.DefaultRepository;

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
