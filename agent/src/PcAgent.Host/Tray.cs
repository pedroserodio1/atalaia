using System.Diagnostics;

namespace PcAgent.Host;

/// <summary>Ícone da bandeja. Só vive na thread da interface; nunca chama plugin.</summary>
internal sealed class Tray : ApplicationContext
{
    private readonly NotifyIcon _icon;

    public Tray(AgentRuntime runtime)
    {
        var status = new ToolStripMenuItem($"Rodando em {runtime.Address}{(runtime.Simulate ? " (simulado)" : "")}") { Enabled = false };
        var clients = new ToolStripMenuItem("Clientes conectados: 0") { Enabled = false };
        var copy = new ToolStripMenuItem("Copiar token", null, (_, _) => Clipboard.SetText(runtime.Token));
        var logs = new ToolStripMenuItem("Abrir pasta de logs", null, (_, _) =>
        {
            Directory.CreateDirectory(AppPaths.Logs);
            Process.Start(new ProcessStartInfo(AppPaths.Logs) { UseShellExecute = true });
        });
        var exit = new ToolStripMenuItem("Sair", null, (_, _) => ExitThread());

        var menu = new ContextMenuStrip();
        menu.Items.AddRange([status, clients, new ToolStripSeparator(), copy, logs, new ToolStripSeparator(), exit]);
        menu.Opening += (_, _) => clients.Text = $"Clientes conectados: {runtime.Hub.ClientCount}";

        _icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Painel Agent",
            ContextMenuStrip = menu,
            Visible = true,
        };

        if (runtime.TokenCreated)   // o balão nunca mostra o token
            _icon.ShowBalloonTip(8000, "Painel Agent", "O token de pareamento foi criado. Use \"Copiar token\" no menu da bandeja.", ToolTipIcon.Info);
    }

    protected override void ExitThreadCore()
    {
        _icon.Visible = false;
        _icon.Dispose();
        base.ExitThreadCore();
    }
}
