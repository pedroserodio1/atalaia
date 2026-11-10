// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Globalization;

namespace Atalaia.Agent.Plugins.Ups;

public sealed record UpsEvent(string Kind, string Severity, string Message);

/// <summary>
/// Anti-ruído das quedas de energia. Classe pura, sem relógio próprio: recebe os instantes.
/// Quedas mais curtas que <c>shortOutage</c> não geram evento na hora; viram um <c>power_flicker</c> por janela.
/// </summary>
public sealed class OutageDebouncer(TimeSpan shortOutage, TimeSpan groupWindow)
{
    private DateTime? _failAt;
    private bool _announced;
    private int _shortCount;
    private DateTime? _windowStart;

    /// <summary>Falha quando já há uma aberta: ignorada.</summary>
    public IReadOnlyList<UpsEvent> OnFail(DateTime t)
    {
        if (_failAt is not null) return [];
        _failAt = t;
        _announced = false;
        return [];
    }

    /// <summary>Volta sem falha aberta: ignorada.</summary>
    public IReadOnlyList<UpsEvent> OnRestore(DateTime t)
    {
        if (_failAt is not { } failAt) return [];
        var duration = t - failAt;
        var announced = _announced;
        _failAt = null;
        _announced = false;

        if (announced)
            return [Restored(duration)];

        if (duration < shortOutage)   // queda curta: sem evento agora, conta para a janela
        {
            _shortCount++;
            _windowStart ??= t;
            return [];
        }

        // falha e volta lidas na mesma coleta: anuncia a queda e já a volta
        return [Failed(), Restored(duration)];
    }

    public IReadOnlyList<UpsEvent> Tick(DateTime now)
    {
        List<UpsEvent>? events = null;

        if (_failAt is { } failAt && !_announced && now - failAt >= shortOutage)
        {
            _announced = true;
            (events ??= []).Add(Failed());
        }

        if (_windowStart is { } start && _shortCount > 0 && now - start >= groupWindow)
        {
            (events ??= []).Add(new UpsEvent("power_flicker", "warn",
                $"{_shortCount} queda(s) de energia com menos de {Secs(shortOutage)} nos últimos {Mins(groupWindow)}"));
            _shortCount = 0;
            _windowStart = null;
        }

        return events ?? [];
    }

    private static UpsEvent Failed() => new("power_fail", "critical", "Falha na rede elétrica");

    private static UpsEvent Restored(TimeSpan duration) =>
        new("power_restored", "info", $"Rede elétrica reestabelecida (queda de {Duration(duration)})");

    private static string Duration(TimeSpan d) => d.TotalMinutes >= 1
        ? string.Create(CultureInfo.InvariantCulture, $"{(int)d.TotalMinutes} min {d.Seconds} s")
        : string.Create(CultureInfo.InvariantCulture, $"{(int)d.TotalSeconds} s");

    private static string Secs(TimeSpan d) => string.Create(CultureInfo.InvariantCulture, $"{(int)d.TotalSeconds} s");
    private static string Mins(TimeSpan d) => string.Create(CultureInfo.InvariantCulture, $"{(int)Math.Round(d.TotalMinutes)} min");
}
