// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Text.Json;
using System.Text.Json.Nodes;
using Atalaia.Agent.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atalaia.Agent.Host.Ui;

/// <summary>Erro que a página pode mostrar ao usuário: <see cref="Code"/> é estável (a página decide por ele), a mensagem é em português.</summary>
internal sealed class CommandException(string code, string message) : Exception(message)
{
    public string Code => code;
}

/// <summary>
/// A ponte entre a página da janela e o agente. A página manda <c>{ "id": 1, "cmd": "state", "args": { } }</c> e recebe
/// <c>{ "id": 1, "ok": true, "data": ... }</c> ou <c>{ "id": 1, "ok": false, "error": "codigo", "message": "texto" }</c>.
/// <b>Só existem os comandos registrados</b>: a página nunca escolhe caminho, URL nem comando do sistema. Não depende do WebView2
/// (por isso é testável). Nunca lança: qualquer entrada vira uma resposta de erro.
/// </summary>
internal sealed class CommandRouter(ILogger? logger = null)
{
    /// <summary>Mensagem da página maior que isso é recusada (as da janela são pequenas).</summary>
    public const int MaxMessageChars = 16 * 1024;

    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly Dictionary<string, Func<JsonElement, Task<JsonNode?>>> _handlers = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> Commands => _handlers.Keys;

    public void Register(string command, Func<JsonElement, Task<JsonNode?>> handler)
    {
        if (!_handlers.TryAdd(command, handler)) throw new InvalidOperationException($"Comando já registrado: {command}");
    }

    public async Task<string> HandleAsync(string message)
    {
        JsonNode? id = null;
        try
        {
            if (message.Length > MaxMessageChars) return Fail(null, "bad_request", "Mensagem grande demais.");
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return Fail(null, "bad_request", "A mensagem precisa ser um objeto.");

            if (root.TryGetProperty("id", out var idEl))
            {
                if (idEl.ValueKind is not (JsonValueKind.String or JsonValueKind.Number))
                    return Fail(null, "bad_request", "id precisa ser texto ou número.");
                id = JsonNode.Parse(idEl.GetRawText());
            }

            if (!root.TryGetProperty("cmd", out var cmd) || cmd.ValueKind != JsonValueKind.String)
                return Fail(id, "bad_request", "Falta o cmd.");
            if (!_handlers.TryGetValue(cmd.GetString()!, out var handler))
                return Fail(id, "unknown_cmd", "Comando desconhecido.");

            var args = Json.EmptyObject;
            if (root.TryGetProperty("args", out var a) && a.ValueKind != JsonValueKind.Null)
            {
                if (a.ValueKind != JsonValueKind.Object) return Fail(id, "bad_request", "args precisa ser um objeto.");
                args = a;
            }

            var data = await handler(args);
            return new JsonObject { ["id"] = id, ["ok"] = true, ["data"] = data }.ToJsonString();
        }
        catch (JsonException) { return Fail(id, "bad_request", "JSON inválido."); }
        catch (CommandException ex) { return Fail(id, ex.Code, ex.Message); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Comando da janela falhou.");   // o texto da exceção fica só no log: a página recebe uma mensagem genérica
            return Fail(id, "failed", "Não foi possível concluir. Veja o log do agente.");
        }
    }

    private static string Fail(JsonNode? id, string code, string message) =>
        new JsonObject { ["id"] = id, ["ok"] = false, ["error"] = code, ["message"] = message }.ToJsonString();

    // ---------------------------------------------------------------- leitura de argumentos

    /// <summary>Inteiro obrigatório em <paramref name="args"/>; ausente ou de outro tipo vira <c>invalid_args</c>.</summary>
    public static int Int(JsonElement args, string name) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)
            ? n : throw new CommandException("invalid_args", $"Falta o número '{name}'.");

    public static string Text(JsonElement args, string name, int maxLength = 2048) =>
        args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && v.GetString() is { Length: > 0 } s && s.Length <= maxLength
            ? s : throw new CommandException("invalid_args", $"Falta o texto '{name}'.");
}
