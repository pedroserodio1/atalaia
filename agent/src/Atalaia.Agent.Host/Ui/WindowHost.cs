// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using Atalaia.Agent.Host.Settings;
using Microsoft.Extensions.Logging;

namespace Atalaia.Agent.Host.Ui;

/// <summary>
/// Dono da janela do agente. Abre sob demanda (bandeja, segunda instância, primeira execução) e <b>fecha de verdade</b> quando o
/// usuário a fecha: o WebView2 só existe enquanto a janela existe, e o agente segue na bandeja. Tudo o que importa (onboarding,
/// configuração) vive em <see cref="SettingsStore"/> e no <see cref="AgentRuntime"/>, então reabrir nunca perde nada.
/// Qualquer thread pode chamar <see cref="Show"/>.
/// </summary>
internal sealed class WindowHost : IDisposable
{
    private readonly AgentRuntime _runtime;
    private readonly CommandRouter _router;
    private readonly Control _marshal = new();   // só para voltar à thread da interface
    private MainWindow? _window;
    private bool _disposed;

    public WindowHost(AgentRuntime runtime, OnboardingStore onboarding, Func<string> repository, ILogger? logger = null, Action<CommandRouter>? registerMore = null, UpdateStatusSource? updates = null)
    {
        _runtime = runtime;
        _router = new CommandRouter(logger);
        new WindowCommands(runtime, onboarding, repository, closeWindow: CloseWindow, openExternal: MainWindow.OpenInBrowser, updates).Register(_router);
        registerMore?.Invoke(_router);   // os comandos dos passos do onboarding
        _marshal.CreateControl();
        _ = _marshal.Handle;   // cria o handle agora, na thread da interface
        runtime.ClientsChanged += OnClientsChanged;
    }

    public bool IsOpen => _window is { IsDisposed: false };

    public void Show() => OnUiThread(ShowCore);

    /// <summary>Roda na thread da interface (de qualquer thread).</summary>
    public void RunOnUi(Action action) => OnUiThread(action);

    public void CloseWindow() => OnUiThread(() => _window?.Close());

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _runtime.ClientsChanged -= OnClientsChanged;
        OnUiThread(() => { _window?.Dispose(); _window = null; });
        _marshal.Dispose();
    }

    private void ShowCore()
    {
        if (_disposed) return;
        if (_window is null || _window.IsDisposed)
        {
            var window = new MainWindow(_router);
            window.FormClosed += (_, _) => { if (ReferenceEquals(_window, window)) _window = null; };
            _window = window;
        }
        _window.ShowAndFocus();
    }

    private void OnClientsChanged() => OnUiThread(() => _window?.PushChanged("clients"));

    private void OnUiThread(Action action)
    {
        if (_disposed || !_marshal.IsHandleCreated) return;
        if (_marshal.InvokeRequired) { try { _marshal.BeginInvoke(action); } catch (InvalidOperationException) { } }   // fechando o programa
        else action();
    }
}
