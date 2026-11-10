// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Atalaia.Agent.Core;

namespace Atalaia.Agent.Plugins.Ups;

/// <summary>
/// Nobreak Intelbras, lendo o SQLite do serviço deles (somente leitura: só <c>status_history</c> e
/// <c>events_and_failures</c>). Origem: <c>Ups</c> do PcProbe, sem a cópia temporária do banco.
/// </summary>
public sealed class UpsPlugin : IPlugin
{
    private const string FailText = "Falha na rede elétrica";
    private const string RestoreText = "Rede elétrica reestabelecida";
    private const string TimeFormat = "yyyy-MM-dd HH:mm:ss";

    private IPluginContext _ctx = null!;
    private string _connectionString = "";
    private OutageDebouncer _debouncer = null!;
    private long _lastEventId;
    private bool _onBattery;

    public string Id => "ups";
    public string Name => "Nobreak";
    public int DefaultIntervalMs => 15000;
    public IReadOnlyList<ActionDescriptor> Actions { get; } = [new("refresh", "coleta de novo agora")];

    public Task StartAsync(IPluginContext ctx, CancellationToken ct)
    {
        _ctx = ctx;
        var path = ctx.Options["dbPath"];
        if (string.IsNullOrWhiteSpace(path))
            path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Intelbras", "ups_desktop", "intelbras_nobreak_management_service.db");
        if (!File.Exists(path)) throw new FileNotFoundException("Banco do nobreak não encontrado", path);
        _connectionString = $"Data Source={path};Mode=ReadOnly;Pooling=False";

        _debouncer = new OutageDebouncer(
            TimeSpan.FromSeconds(ctx.Options.GetValue("shortOutageSec", 5)),
            TimeSpan.FromSeconds(ctx.Options.GetValue("groupWindowSec", 600)));

        using var db = Open();
        // eventos antigos não são reenviados
        _lastEventId = Scalar<long?>(db, "SELECT MAX(id) FROM events_and_failures") ?? 0;

        // valor inicial de onBattery: o último dos dois textos que importam
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT alert_description, created_at FROM events_and_failures " +
                          "WHERE alert_description IN (@fail, @restore) ORDER BY id DESC LIMIT 1";
        cmd.Parameters.AddWithValue("@fail", FailText);
        cmd.Parameters.AddWithValue("@restore", RestoreText);
        using var r = cmd.ExecuteReader();
        if (r.Read() && r.GetString(0) == FailText)
        {
            _onBattery = true;
            _debouncer.OnFail(ParseLocal(r.GetString(1)));   // queda em andamento: a volta não pode ser ignorada
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    public Task<JsonElement?> CollectAsync(CancellationToken ct)
    {
        using var db = Open();   // aberta e fechada a cada coleta
        ReadNewEvents(db);
        foreach (var e in _debouncer.Tick(DateTime.Now)) _ctx.Events.Emit(e.Kind, e.Severity, e.Message);

        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT input_voltage, output_voltage, power, frequency, battery_charge, " +
                          "battery_voltage, temperature, status, created_at FROM status_history ORDER BY id DESC LIMIT 1";
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return Task.FromResult<JsonElement?>(null);

        var created = ParseLocal(r.GetString(8));
        return Task.FromResult<JsonElement?>(Json.ToElement(new
        {
            inputV = Dec(r, 0),
            outputV = Dec(r, 1),
            loadPercent = Int(r, 2),   // coluna "power": não confirmado que seja % de carga
            freqHz = Dec(r, 3),
            batteryPercent = Int(r, 4),
            batteryV = Dec(r, 5),
            tempC = Int(r, 6),
            status = Int(r, 7),
            onBattery = _onBattery,
            updatedAt = new DateTimeOffset(created, TimeZoneInfo.Local.GetUtcOffset(created))
                .ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture),
        }));
    }

    /// <summary><c>refresh</c> só responde ok: o agendador coleta de novo logo em seguida.</summary>
    public Task<ActionResult> ExecuteAsync(string action, JsonElement args, CancellationToken ct) =>
        Task.FromResult(new ActionResult(action == "refresh", action == "refresh" ? null : "unknown_action"));

    private void ReadNewEvents(SqliteConnection db)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT id, alert_description, created_at FROM events_and_failures WHERE id > @last ORDER BY id";
        cmd.Parameters.AddWithValue("@last", _lastEventId);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            _lastEventId = r.GetInt64(0);
            var text = r.IsDBNull(1) ? "" : r.GetString(1);   // os demais textos são ignorados neste marco
            if (text != FailText && text != RestoreText) continue;

            var at = ParseLocal(r.GetString(2));
            IReadOnlyList<UpsEvent> events;
            if (text == FailText) { _onBattery = true; events = _debouncer.OnFail(at); }
            else { _onBattery = false; events = _debouncer.OnRestore(at); }
            foreach (var e in events) _ctx.Events.Emit(e.Kind, e.Severity, e.Message);
        }
    }

    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connectionString);
        db.Open();
        return db;
    }

    private static T? Scalar<T>(SqliteConnection db, string sql)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        var v = cmd.ExecuteScalar();
        return v is null or DBNull ? default : (T)Convert.ChangeType(v, Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T), CultureInfo.InvariantCulture);
    }

    /// <summary><c>created_at</c> é hora local, no formato <c>yyyy-MM-dd HH:mm:ss</c>.</summary>
    private static DateTime ParseLocal(string s) =>
        DateTime.TryParseExact(s, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : DateTime.Now;

    private static double? Number(SqliteDataReader r, int i) =>
        r.IsDBNull(i) ? null : Convert.ToDouble(r.GetValue(i), CultureInfo.InvariantCulture);

    private static int? Int(SqliteDataReader r, int i) => Number(r, i) is { } v ? (int)Math.Round(v) : null;
    private static double? Dec(SqliteDataReader r, int i) => Number(r, i) is { } v ? Math.Round(v, 1) : null;
}
