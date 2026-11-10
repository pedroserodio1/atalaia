using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Atalaia.Agent.Core;

public interface IPlugin
{
    string Id { get; }                       // "system", "gpu", ... (é o "source" do protocolo)
    string Name { get; }
    int DefaultIntervalMs { get; }
    IReadOnlyList<ActionDescriptor> Actions { get; }
    Task StartAsync(IPluginContext ctx, CancellationToken ct);
    Task StopAsync(CancellationToken ct);
    Task<JsonElement?> CollectAsync(CancellationToken ct);   // null = sem dado nesta rodada
    Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct);
}

public interface IPluginContext
{
    IConfiguration Options { get; }          // seção Plugins:<id> do appsettings
    IEventSink Events { get; }
    IBlobStore Blobs { get; }
    ILogger Logger { get; }
}

public interface IEventSink { void Emit(string kind, string severity, string message); }
public interface IBlobStore { string Put(byte[] bytes); }    // devolve o hash; guarda os 8 mais recentes

public record ActionDescriptor(string Name, string? Description = null);
public record ActionResult(bool Ok, string? Error = null);

public enum PluginStatus { Ok, Error, Disabled }

/// <summary>Mensagem do agente para os clientes, ainda sem o <c>seq</c> (atribuído no envio por conexão).</summary>
public sealed record OutMessage(string Type, string? Source, long Ts, JsonElement Data);

public sealed record PluginInfo(
    string Id, string Name, int IntervalMs, PluginStatus Status, IReadOnlyList<ActionDescriptor> Actions);
