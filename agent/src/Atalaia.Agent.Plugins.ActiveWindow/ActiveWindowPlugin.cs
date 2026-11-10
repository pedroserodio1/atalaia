// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Atalaia.Agent.Core;

namespace Atalaia.Agent.Plugins.ActiveWindow;

/// <summary>Janela em primeiro plano. Origem: fim de <c>Procs</c> e a classe <c>Native</c> do PcProbe. Nunca loga o título.</summary>
public sealed class ActiveWindowPlugin : IPlugin
{
    private bool _sendTitle = true;

    public string Id => "activeWindow";
    public string Name => "Janela ativa";
    public int DefaultIntervalMs => 500;
    public IReadOnlyList<ActionDescriptor> Actions => [];

    public Task StartAsync(IPluginContext ctx, CancellationToken ct)
    {
        _sendTitle = ctx.Options.GetValue("sendTitle", true);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<JsonElement?> CollectAsync(CancellationToken ct)
    {
        string title = "", process = "";
        var hwnd = Native.GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
        {
            if (_sendTitle)
            {
                var sb = new StringBuilder(512);
                Native.GetWindowText(hwnd, sb, sb.Capacity);
                title = sb.ToString();
            }

            Native.GetWindowThreadProcessId(hwnd, out var pid);
            try { process = Process.GetProcessById((int)pid).ProcessName + ".exe"; }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { }   // processo já saiu
        }
        return Task.FromResult<JsonElement?>(Json.ToElement(new { title, process }));
    }

    public Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct) =>
        Task.FromResult(new ActionResult(false, "unknown_action"));

    private static class Native
    {
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    }
}
