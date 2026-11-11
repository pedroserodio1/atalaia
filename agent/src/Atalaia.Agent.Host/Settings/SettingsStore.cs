// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Atalaia.Agent.Host.Settings;

/// <summary>
/// As configurações do usuário, em <c>%LOCALAPPDATA%\AtalaiaAgent\settings.json</c>. É a camada que a janela do agente escreve; fica entre
/// <c>appsettings.Local.json</c> e o <c>.env</c> na ordem de carga (o <c>appsettings.json</c> versionado nunca recebe config pessoal).
/// Segredos (chaves de API) <b>nunca</b> vêm para cá: só por variável de ambiente.
/// </summary>
internal sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };
    private readonly object _lock = new();

    public static SettingsStore Default { get; } = new(Path.Combine(AppPaths.Dir, "settings.json"));

    public string FilePath => path;

    /// <summary>
    /// Confere o arquivo na partida. JSON inválido (ou que não é um objeto) é guardado ao lado como
    /// <c>settings.json.corrompido-aaaammdd-hhmmss</c> e o agente começa com configuração vazia, em vez de nem subir.
    /// </summary>
    /// <returns>O caminho da cópia guardada, ou <c>null</c> se o arquivo estava bom (ou não existia).</returns>
    public string? QuarantineIfInvalid()
    {
        lock (_lock)
        {
            if (!File.Exists(path)) return null;
            if (TryParse(File.ReadAllText(path), out _)) return null;
            var saved = $"{path}.corrompido-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Move(path, saved, overwrite: true);
            return saved;
        }
    }

    /// <summary>O conteúdo atual (uma cópia: pode mexer à vontade). Ausente ou inválido = objeto vazio.</summary>
    public JsonObject Read()
    {
        lock (_lock) return ReadUnlocked();
    }

    /// <summary>Lê, aplica a mudança e grava de uma vez, com escrita atômica (arquivo temporário + troca).</summary>
    public void Update(Action<JsonObject> change)
    {
        lock (_lock)
        {
            var root = ReadUnlocked();
            change(root);
            WriteUnlocked(root);
        }
    }

    /// <summary>Volta o arquivo para um conteúdo anterior (usado quando uma mudança de configuração não pôde ser aplicada).</summary>
    public void Replace(JsonObject content) => Update(root =>
    {
        root.Clear();
        foreach (var (key, value) in content.ToArray()) { content.Remove(key); root[key] = value; }
    });

    /// <summary>Atalho: grava um valor em <c>"Secao:Chave"</c> (cria as seções que faltarem). <c>null</c> remove a chave.</summary>
    public void Set(string key, JsonNode? value) => Update(root => SetPath(root, key, value));

    public static void SetPath(JsonObject root, string key, JsonNode? value)
    {
        var parts = key.Split(':');
        var node = root;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            if (node[parts[i]] is not JsonObject next) node[parts[i]] = next = new JsonObject();
            node = next;
        }
        if (value is null) node.Remove(parts[^1]);
        else node[parts[^1]] = value;
    }

    private JsonObject ReadUnlocked()
    {
        if (!File.Exists(path)) return new JsonObject();
        return TryParse(File.ReadAllText(path), out var obj) ? obj : new JsonObject();
    }

    private void WriteUnlocked(JsonObject root)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(Pretty));
        if (File.Exists(path)) File.Replace(tmp, path, destinationBackupFileName: null);
        else File.Move(tmp, path);
    }

    private static bool TryParse(string text, out JsonObject obj)
    {
        obj = new JsonObject();
        try
        {
            if (JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip }) is not JsonObject parsed) return false;
            obj = parsed;
            return true;
        }
        catch (JsonException) { return false; }
    }
}
