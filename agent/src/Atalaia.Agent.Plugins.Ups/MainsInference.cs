// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

namespace Atalaia.Agent.Plugins.Ups;

/// <summary>Uma linha de <c>status_history</c>, com os valores crus (sem arredondar).</summary>
public sealed record UpsReading(DateTime At, double? Charge, double? InputV);

/// <summary>
/// Deduz que a rede voltou sem o evento de restauração (o software da Intelbras não o grava com o PC desligado).
/// Função pura: sem relógio e sem banco.
/// </summary>
public static class MainsInference
{
    /// <summary>
    /// Hora da primeira leitura, posterior à falha, que prova a rede: tensão de entrada normal (&gt;= <paramref name="minInputV"/>)
    /// ou carga pelo menos <paramref name="minRisePercent"/> pontos acima do mínimo visto desde a falha.
    /// <c>null</c> se continua na bateria. A carga sozinha não prova nada quando a bateria ficou em 100%.
    /// </summary>
    public static DateTime? InferReturn(DateTime failAt, IReadOnlyList<UpsReading> readings,
        double minRisePercent = 2, double minInputV = 80)
    {
        double? minCharge = null;
        foreach (var r in readings.Where(r => r.At > failAt).OrderBy(r => r.At))
        {
            if (r.InputV >= minInputV) return r.At;
            if (r.Charge is not { } c) continue;
            if (minCharge is { } m && c - m >= minRisePercent) return r.At;
            minCharge = minCharge is { } min ? Math.Min(min, c) : c;
        }
        return null;
    }
}
