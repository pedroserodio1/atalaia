// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Diagnostics;
using System.Runtime.InteropServices;
using Atalaia.Agent.Host.Adb;
using Atalaia.Agent.Host.Install;
using Atalaia.Agent.Host.Network;
using Atalaia.Agent.Host.Settings;
using Atalaia.Agent.Host.Sources;
using Atalaia.Agent.Host.Startup;
using Atalaia.Agent.Host.Ui;
using Atalaia.Agent.Host.Updates;
using Atalaia.Agent.Host.Usb;
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
        if (args.Contains(FirewallHelper.Flag)) return FirewallHelper.Run(args);   // processo elevado (UAC): só cria a regra do Firewall e sai

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
            var uiLog = new Serilog.Extensions.Logging.SerilogLoggerFactory(Log.Logger).CreateLogger("Janela");   // erros dos comandos da janela vão para o log (a página só recebe uma mensagem genérica)
            var stepServices = BuildStepServices(runtime);
            var steps = new StepCommands(stepServices, uiLog);
            Tray? tray = null;
            var updates = BuildUpdateService(runtime, () => window?.RunOnUi(() => tray?.Quit()));
            var updateCommands = new UpdateCommands(new UpdateCommandServices(updates, stepServices.Adb, () => runtime.Port, () => RepositoryFrom(runtime.Config),
                version => ApkProviderFor(runtime, version)), uiLog);
            window = new WindowHost(runtime, onboarding, () => RepositoryFrom(config), uiLog, router => { steps.Register(router); updateCommands.Register(router); }, () => updates.Status);
            tray = new Tray(runtime, window, beforeExit: () => updates.ApplyStagedOnExit());
            var updateLoop = new CancellationTokenSource();
            _ = Task.Run(() => updates.RunAsync(updateLoop.Token));

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
            updateLoop.Cancel();
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

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };   // o prazo de cada chamada é do GitHubReleaseClient

    /// <summary>Liga os comandos dos passos ao Windows de verdade: Firewall, adb, GitHub, registro, caixas de arquivo e área de transferência.</summary>
    private static StepServices BuildStepServices(AgentRuntime runtime)
    {
        var settings = SettingsStore.Default;
        string Repo() => RepositoryFrom(runtime.Config);
        return new StepServices(
            runtime,
            settings,
            ApplyConfig: () => runtime.RestartAsync(HostConfig.Compose(AppContext.BaseDirectory, settings)),
            Firewall: new WindowsFirewall(),
            Adb: new AdbService(() => UsbOptions.From(runtime.Config).AdbPath, Environment.GetEnvironmentVariable, File.Exists, (exe, timeout) => new ProcessAdbRunner(exe, timeout)),
            Apk: () => ApkProviderFor(runtime, AppInfo.Version),
            ReadAdapters: LanAddressProvider.Read,
            IsPortFree: PortValidator.IsFreeOnLoopback,
            Probe: id => new SourceProbe(k => runtime.Config[$"Plugins:{id}:{k}"], File.Exists, Directory.Exists, Environment.GetEnvironmentVariable,
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            Autostart: new AutostartService(new RegistryRunKey(), () => Environment.ProcessPath),
            PickAdb: () => Pick("Escolha o adb.exe (pasta platform-tools do Android)", "adb.exe|adb.exe"),
            PickApk: () => Pick("Escolha o APK do Atalaia para o tablet", "APK do Android (*.apk)|*.apk"),
            CopyText: Clipboard.SetText,
            OpenExternal: MainWindow.OpenInBrowser);
    }

    private static ApkProvider ApkProviderFor(AgentRuntime runtime, string version) =>
        new(new GitHubReleaseClient(Http, new GitHubReleaseClient.Options(RepositoryFrom(runtime.Config), AppInfo.Version)), Path.Combine(AppPaths.Dir, "downloads"), version);

    /// <summary>A atualização automática: lê as releases do repositório configurado e, a pedido, executa o instalador baixado (só um arquivo já conferido).</summary>
    private static UpdateService BuildUpdateService(AgentRuntime runtime, Action exitApp) =>
        new(SettingsStore.Default,
            () => new GitHubReleaseClient(Http, new GitHubReleaseClient.Options(RepositoryFrom(runtime.Config), AppInfo.Version)),
            AppInfo.Version,
            new UpdateService.Hooks(Path.Combine(AppPaths.Dir, "updates"),
                Launch: (path, args) => Process.Start(new ProcessStartInfo(path, args) { UseShellExecute = false })?.Dispose(),
                ExitApp: exitApp));

    private static string? Pick(string title, string filter)
    {
        using var dialog = new OpenFileDialog { Title = title, Filter = filter, CheckFileExists = true, Multiselect = false };
        return dialog.ShowDialog() == DialogResult.OK ? dialog.FileName : null;
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
