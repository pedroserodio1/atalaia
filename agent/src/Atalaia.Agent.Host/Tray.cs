// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Diagnostics;
using Atalaia.Agent.Host.Ui;

namespace Atalaia.Agent.Host;

/// <summary>Ícone da bandeja. Só vive na thread da interface; nunca chama plugin. O agente continua rodando com a janela fechada.</summary>
internal sealed class Tray : ApplicationContext
{
    private readonly NotifyIcon _icon;
    private readonly WindowHost _window;

    public Tray(AgentRuntime runtime, WindowHost window)
    {
        _window = window;
        var open = new ToolStripMenuItem("Abrir Atalaia", null, (_, _) => window.Show()) { Font = new Font(SystemFonts.MenuFont!, FontStyle.Bold) };
        var status = new ToolStripMenuItem($"Rodando em {runtime.Address}{(runtime.Simulate ? " (simulado)" : "")}") { Enabled = false };
        var clients = new ToolStripMenuItem("Clientes conectados: 0") { Enabled = false };
        // Código de 6 dígitos gerado a cada início: aparece aqui (só na tela do PC) para digitar no tablet. Token fixo nunca aparece.
        var code = new ToolStripMenuItem(runtime.TokenCreated ? $"Código de acesso: {runtime.Token}" : "Token fixo (ATALAIA_TOKEN)") { Enabled = false };
        var copy = new ToolStripMenuItem(runtime.TokenCreated ? "Copiar código" : "Copiar token", null, (_, _) => Clipboard.SetText(runtime.Token));
        var logs = new ToolStripMenuItem("Abrir pasta de logs", null, (_, _) =>
        {
            Directory.CreateDirectory(AppPaths.Logs);
            Process.Start(new ProcessStartInfo(AppPaths.Logs) { UseShellExecute = true });
        });
        var exit = new ToolStripMenuItem("Sair", null, (_, _) => { _window.Dispose(); ExitThread(); });

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([open, new ToolStripSeparator(), status, clients, code, new ToolStripSeparator(), copy, logs, new ToolStripSeparator(), exit]);
        menu.Opening += (_, _) => clients.Text = $"Clientes conectados: {runtime.Hub.ClientCount}";

        _icon = new NotifyIcon
        {
            Icon = BrandIcon.Tray(),
            Text = "Atalaia",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) window.Show(); };
        if (runtime.TokenCreated)   // o balão nunca mostra o código (fica no histórico de notificações)
            _icon.ShowBalloonTip(8000, "Atalaia", "Novo código de acesso de 6 dígitos. Veja no menu do ícone da bandeja.", ToolTipIcon.Info);
    }

    protected override void ExitThreadCore()
    {
        _icon.Visible = false;
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
