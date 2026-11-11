// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Serilog;

namespace Atalaia.Agent.Host.Ui;

/// <summary>
/// A janela do agente: um WebView2 que carrega as páginas locais de <c>ui/</c> (somente leitura, ao lado do programa). A página não
/// tem rede, não abre outras janelas e só fala com o agente pelos comandos do <see cref="CommandRouter"/>.
/// </summary>
internal sealed class MainWindow : Form
{
    /// <summary>Nome do site virtual que aponta para a pasta <c>ui/</c>. Só ele é permitido: qualquer outra navegação é cancelada.</summary>
    internal const string VirtualHost = "atalaia.ui";
    private static readonly string Origin = $"https://{VirtualHost}/";

    private readonly CommandRouter _router;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.FromArgb(11, 13, 16) };

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public MainWindow(CommandRouter router)
    {
        _router = router;
        Text = "Atalaia";
        Icon = BrandIcon.Window();
        BackColor = Color.FromArgb(11, 13, 16);   // grafite desde o primeiro quadro: nada de piscar branco ao abrir
        StartPosition = FormStartPosition.CenterScreen;

        // O desenho é de 1280 x 800 pixels lógicos: acompanha o zoom do Windows e nunca passa da área útil da tela.
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        MinimumSize = new Size(LogicalToDeviceUnits(1000), LogicalToDeviceUnits(660));
        ClientSize = new Size(Math.Min(LogicalToDeviceUnits(1280), area.Width - 40), Math.Min(LogicalToDeviceUnits(800), area.Height - 80));

        Controls.Add(_web);
        Load += async (_, _) => await InitializeWebAsync();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Barra de título escura no tom da janela (Windows 10 20H1+ e 11). Em versão que não conhece, a chamada só devolve erro.
        var dark = 1;
        _ = DwmSetWindowAttribute(Handle, 20, ref dark, sizeof(int));        // DWMWA_USE_IMMERSIVE_DARK_MODE
        var caption = 0x16120F;                                              // #0F1216 (COLORREF é BGR)
        _ = DwmSetWindowAttribute(Handle, 35, ref caption, sizeof(int));     // DWMWA_CAPTION_COLOR (Windows 11)
        var text = 0xF5F1EE;                                                 // #EEF1F5
        _ = DwmSetWindowAttribute(Handle, 36, ref text, sizeof(int));        // DWMWA_TEXT_COLOR (Windows 11)
    }

    private async Task InitializeWebAsync()
    {
        try
        {
            var env = await CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: AppPaths.WebView2Data);
            await _web.EnsureCoreWebView2Async(env);
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or COMException)
        {
            Log.Error(ex, "WebView2 indisponível.");
            MessageBox.Show(
                "A janela do Atalaia precisa do WebView2 da Microsoft, que não foi encontrado neste PC.\n\n" +
                "O agente continua funcionando na bandeja. Para abrir a janela, instale o WebView2 Runtime (https://go.microsoft.com/fwlink/p/?LinkId=2124703) e abra o Atalaia de novo.",
                "Atalaia", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
            return;
        }

        var web = _web.CoreWebView2;
        var uiFolder = Path.Combine(AppContext.BaseDirectory, "ui");
        web.SetVirtualHostNameToFolderMapping(VirtualHost, uiFolder, CoreWebView2HostResourceAccessKind.Deny);

        // A página é só uma tela local: nada de menu do navegador, atalhos, download, permissão, outra janela ou outro site.
        var s = web.Settings;
#if DEBUG
        s.AreDevToolsEnabled = true;
#else
        s.AreDevToolsEnabled = false;
#endif
        s.AreBrowserAcceleratorKeysEnabled = s.AreDevToolsEnabled;
        s.AreDefaultContextMenusEnabled = false;
        s.AreDefaultScriptDialogsEnabled = false;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.IsGeneralAutofillEnabled = false;

        web.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith(Origin, StringComparison.Ordinal)) e.Cancel = true; };
        web.NewWindowRequested += (_, e) => e.Handled = true;
        web.DownloadStarting += (_, e) => e.Cancel = true;
        web.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        web.WebMessageReceived += async (_, e) => await OnMessageAsync(e);

        web.Navigate(Origin + "index.html");
    }

    private async Task OnMessageAsync(CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(Origin, StringComparison.Ordinal)) return;   // só a nossa página fala com o agente
        string message;
        try { message = e.TryGetWebMessageAsString(); }
        catch (ArgumentException) { return; }   // não era texto

        var reply = await _router.HandleAsync(message);
        if (!IsDisposed && _web.CoreWebView2 is { } web) web.PostWebMessageAsString(reply);
    }

    /// <summary>Avisa a página de que algo mudou (quem está conectado, por exemplo): ela pede o <c>state</c> de novo. Só na thread da janela.</summary>
    public void PushChanged(string what)
    {
        if (!IsDisposed && _web.CoreWebView2 is { } web)
            web.PostWebMessageAsString($$"""{"event":"{{what}}"}""");
    }

    /// <summary>Traz a janela para a frente (o Windows só deixa quem acabou de receber o clique do usuário; o truque do TopMost contorna o resto).</summary>
    public void ShowAndFocus()
    {
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Show();
        TopMost = true;
        TopMost = false;
        Activate();
    }

    internal static void OpenInBrowser(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
