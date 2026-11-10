// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Globalization;

namespace Atalaia.Agent.Host.Sources;

internal enum FieldKind { Latitude, Longitude, SteamId64, Text }

/// <param name="Secret">Segredo: vai para o cofre (DPAPI), nunca para o <c>settings.json</c>, e o valor nunca volta para a tela.</param>
internal sealed record CredentialField(string Name, string Label, FieldKind Kind, bool Secret)
{
    /// <summary>A chave de configuração, como o plugin a lê: <c>Plugins:steam:apiKey</c>.</summary>
    public string Key(string sourceId) => $"Plugins:{sourceId}:{Name}";

    /// <summary>A variável de ambiente que, se definida, manda sobre o que a janela grava.</summary>
    public string EnvName(string sourceId) => $"ATALAIA_Plugins__{sourceId}__{Name}";
}

/// <summary>
/// Os campos que a janela configura para as fontes que dependem de dado pessoal ou credencial (clima, Steam, Discord) e as regras de cada
/// um. Tudo puro: o teste chama sem Windows. Mensagens em português, sem nunca repetir o valor digitado.
/// </summary>
internal static class SourceCredentials
{
    public const int MaxLength = 256;

    private static readonly Dictionary<string, CredentialField[]> Fields = new(StringComparer.Ordinal)
    {
        ["weather"] =
        [
            new("latitude", "Latitude", FieldKind.Latitude, Secret: false),
            new("longitude", "Longitude", FieldKind.Longitude, Secret: false),
        ],
        ["steam"] =
        [
            new("apiKey", "Chave da Web API", FieldKind.Text, Secret: true),
            new("steamId64", "SteamID64", FieldKind.SteamId64, Secret: false),
        ],
        // clientId é o identificador público do aplicativo (aparece na própria tela de autorização do Discord): não é segredo.
        ["discord"] =
        [
            new("clientId", "Client ID", FieldKind.Text, Secret: false),
            new("clientSecret", "Client Secret", FieldKind.Text, Secret: true),
        ],
    };

    /// <summary>Os campos da fonte, na ordem da tela; vazio se a janela não configura essa fonte.</summary>
    public static IReadOnlyList<CredentialField> For(string sourceId) => Fields.TryGetValue(sourceId, out var f) ? f : [];

    /// <summary>
    /// Confere e normaliza um valor. Devolve <c>null</c> em <paramref name="error"/> se está certo; <paramref name="normalized"/> é o que se grava
    /// (latitude e longitude voltam com ponto decimal, <c>InvariantCulture</c>, seja qual for a cultura do Windows).
    /// </summary>
    public static bool TryValidate(CredentialField field, string? raw, out string normalized, out string? error)
    {
        normalized = "";
        var v = raw?.Trim() ?? "";
        if (v.Length == 0) { error = $"{field.Label}: preencha este campo."; return false; }
        if (v.Length > MaxLength) { error = $"{field.Label}: valor grande demais."; return false; }

        switch (field.Kind)
        {
            case FieldKind.Latitude:
                return Coordinate(field, v, 90, "-90 e 90", out normalized, out error);
            case FieldKind.Longitude:
                return Coordinate(field, v, 180, "-180 e 180", out normalized, out error);
            case FieldKind.SteamId64:
                if (v.Length != 17 || !v.All(char.IsAsciiDigit)) { error = $"{field.Label}: precisa ter exatamente 17 dígitos (só números)."; return false; }
                break;
            default:
                if (v.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) { error = $"{field.Label}: não pode ter espaços nem quebras de linha."; return false; }
                break;
        }
        normalized = v;
        error = null;
        return true;
    }

    private static bool Coordinate(CredentialField field, string v, double limit, string range, out string normalized, out string? error)
    {
        normalized = "";
        if (v.Contains(',')) { error = $"{field.Label}: use ponto como separador decimal (por exemplo, -23.55), não vírgula."; return false; }
        if (!double.TryParse(v, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var n) || !double.IsFinite(n))
        { error = $"{field.Label}: informe um número em graus decimais, com ponto (por exemplo, -23.55)."; return false; }
        if (n < -limit || n > limit) { error = $"{field.Label}: precisa estar entre {range}."; return false; }
        normalized = n.ToString("0.#######", CultureInfo.InvariantCulture);
        error = null;
        return true;
    }

    /// <summary>
    /// Valida o que a página mandou. Um campo ausente (ou vazio) de <b>segredo já guardado</b> fica como está; os demais são obrigatórios.
    /// Só devolve os campos que mudam. <paramref name="errors"/> junta todas as mensagens (uma por campo com problema).
    /// </summary>
    public static bool TryValidateForm(string sourceId, IReadOnlyDictionary<string, string?> submitted, Func<CredentialField, bool> alreadyConfigured,
        out Dictionary<CredentialField, string> changes, out List<string> errors)
    {
        changes = [];
        errors = [];
        foreach (var field in For(sourceId))
        {
            submitted.TryGetValue(field.Name, out var raw);
            if (field.Secret && string.IsNullOrWhiteSpace(raw) && alreadyConfigured(field)) continue;   // em branco = manter
            if (TryValidate(field, raw, out var value, out var error)) changes[field] = value;
            else errors.Add(error!);
        }
        return errors.Count == 0;
    }
}
