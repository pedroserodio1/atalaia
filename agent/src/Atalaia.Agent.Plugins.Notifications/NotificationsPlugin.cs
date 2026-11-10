using System.Text.Json;
using Atalaia.Agent.Core;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace Atalaia.Agent.Plugins.Notifications;

/// <summary>
/// Notificações do Windows: só o nome do app e a contagem. O título e o texto nunca são lidos, enviados nem logados.
/// Origem: <c>Notifs</c> do PcProbe.
/// </summary>
public sealed class NotificationsPlugin : IPlugin
{
    private UserNotificationListener _listener = null!;

    public string Id => "notifications";
    public string Name => "Notificações";
    public int DefaultIntervalMs => 5000;
    public IReadOnlyList<ActionDescriptor> Actions { get; } =
        [new("clear_all", "descarta todas as notificações do Windows"), new("refresh", "coleta de novo agora")];

    public async Task StartAsync(IPluginContext ctx, CancellationToken ct)
    {
        _listener = UserNotificationListener.Current;
        var access = await _listener.RequestAccessAsync();
        if (access != UserNotificationListenerAccessStatus.Allowed)
            throw new InvalidOperationException(
                $"Acesso às notificações: {access}. Libere em Configurações > Privacidade > Notificações.");
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    public async Task<JsonElement?> CollectAsync(CancellationToken ct)
    {
        var list = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
        var apps = list
            .GroupBy(n => n.AppInfo?.DisplayInfo?.DisplayName ?? "?")
            .Select(g => new { name = g.Key, count = g.Count() })
            .OrderByDescending(a => a.count).ThenBy(a => a.name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return Json.ToElement(new { count = list.Count, apps });
    }

    public Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct)
    {
        switch (action)
        {
            case "clear_all": _listener.ClearNotifications(); return Task.FromResult(new ActionResult(true));
            case "refresh": return Task.FromResult(new ActionResult(true));
            default: return Task.FromResult(new ActionResult(false, "unknown_action"));
        }
    }
}
