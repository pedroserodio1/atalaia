// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Globalization;
using System.Text.Json;
using Atalaia.Agent.Core;
using Atalaia.Agent.Plugins.Calendar;
using Atalaia.Agent.Plugins.Simulator;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Atalaia.Agent.Tests;

public class CalendarTests
{
    // 15/11/2026 12:00 em UTC-3; o fuso é fixo para o teste não depender do Windows de quem roda.
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone("teste", TimeSpan.FromHours(-3), "teste", "teste");
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.FromHours(-3));

    private const string Ics = """
        BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Teste//PT
        X-WR-CALNAME:Trabalho
        BEGIN:VEVENT
        UID:a1@teste
        DTSTAMP:20261101T000000Z
        DTSTART:20261116T120000Z
        DTEND:20261116T130000Z
        SUMMARY:Reunião de equipe
        END:VEVENT
        BEGIN:VEVENT
        UID:rec@teste
        DTSTAMP:20261101T000000Z
        DTSTART:20261102T130000Z
        DTEND:20261102T140000Z
        RRULE:FREQ=WEEKLY;BYDAY=MO;COUNT=10
        EXDATE:20261109T130000Z
        SUMMARY:Segunda semanal
        END:VEVENT
        BEGIN:VEVENT
        UID:dia@teste
        DTSTAMP:20261101T000000Z
        DTSTART;VALUE=DATE:20261120
        DTEND;VALUE=DATE:20261121
        SUMMARY:Feriado
        END:VEVENT
        BEGIN:VEVENT
        UID:cancelado@teste
        DTSTAMP:20261101T000000Z
        DTSTART:20261118T120000Z
        DTEND:20261118T130000Z
        STATUS:CANCELLED
        SUMMARY:Cancelado
        END:VEVENT
        BEGIN:VEVENT
        UID:longe@teste
        DTSTAMP:20261101T000000Z
        DTSTART:20270215T120000Z
        DTEND:20270215T130000Z
        SUMMARY:Fora da janela
        END:VEVENT
        END:VCALENDAR
        """;

    private static List<JsonElement> Events(JsonElement data) => data.GetProperty("events").EnumerateArray().ToList();

    [Fact]
    public void Eventos_do_mes_e_do_seguinte_em_ordem_de_inicio()
    {
        var d = CalendarPlugin.Build([("Trabalho", Ics)], Now, Zone);
        var titles = Events(d).Select(e => e.GetProperty("title").GetString()).ToList();
        Assert.Equal("Segunda semanal", titles[0]);                 // 2/11, antes de "agora" mas ainda no mês
        Assert.DoesNotContain("Cancelado", titles);                 // STATUS:CANCELLED não aparece
        Assert.DoesNotContain("Fora da janela", titles);            // fevereiro/2027 está além do mês seguinte
        Assert.Contains("Reunião de equipe", titles);
        Assert.Equal("2026-11-16T09:00:00-03:00", Events(d).First(e => e.GetProperty("title").GetString() == "Reunião de equipe").GetProperty("start").GetString());
        Assert.Equal(0, Events(d)[0].GetProperty("calendar").GetInt32());                      // índice da agenda
        var cals = d.GetProperty("calendars").EnumerateArray().ToList();
        Assert.Equal("Trabalho", cals.Single().GetProperty("name").GetString());                 // o nome é o que o usuário deu, não o X-WR-CALNAME
        Assert.True(cals.Single().GetProperty("ok").GetBoolean());
        Assert.Equal("2026-11-15T12:00:00-03:00", d.GetProperty("updatedAt").GetString());
    }

    [Fact]
    public void Evento_que_se_repete_vira_uma_entrada_por_ocorrencia_e_respeita_a_excecao()
    {
        var starts = Events(CalendarPlugin.Build([("Trabalho", Ics)], Now, Zone))
            .Where(e => e.GetProperty("title").GetString() == "Segunda semanal").Select(e => e.GetProperty("start").GetString()).ToList();
        Assert.Equal(
            ["2026-11-02T10:00:00-03:00", "2026-11-16T10:00:00-03:00", "2026-11-23T10:00:00-03:00", "2026-11-30T10:00:00-03:00",
             "2026-12-07T10:00:00-03:00", "2026-12-14T10:00:00-03:00", "2026-12-21T10:00:00-03:00", "2026-12-28T10:00:00-03:00"], starts);   // sem 9/11 (EXDATE) e sem 4/1/2027
    }

    [Fact]
    public void Dia_inteiro_comeca_a_meia_noite_local_e_leva_allDay()
    {
        var all = Events(CalendarPlugin.Build([("Trabalho", Ics)], Now, Zone));
        var feriado = all.Single(e => e.GetProperty("title").GetString() == "Feriado");
        Assert.True(feriado.GetProperty("allDay").GetBoolean());
        Assert.Equal("2026-11-20T00:00:00-03:00", feriado.GetProperty("start").GetString());
        Assert.Equal("2026-11-21T00:00:00-03:00", feriado.GetProperty("end").GetString());
        Assert.False(all[0].GetProperty("allDay").GetBoolean());
    }

    [Fact]
    public void Titulo_comprido_e_cortado_e_sem_titulo_vira_vazio()
    {
        var ics = "BEGIN:VCALENDAR\nVERSION:2.0\nPRODID:-//T//PT\nBEGIN:VEVENT\nUID:x@t\nDTSTAMP:20261101T000000Z\nDTSTART:20261116T120000Z\nDTEND:20261116T130000Z\nSUMMARY:" + new string('a', 200) + "\nEND:VEVENT\n" +
                  "BEGIN:VEVENT\nUID:y@t\nDTSTAMP:20261101T000000Z\nDTSTART:20261117T120000Z\nDTEND:20261117T130000Z\nEND:VEVENT\nEND:VCALENDAR\n";
        var ev = Events(CalendarPlugin.Build([("A", ics)], Now, Zone));
        Assert.Equal(CalendarPlugin.MaxTitle, ev[0].GetProperty("title").GetString()!.Length);
        Assert.EndsWith("…", ev[0].GetProperty("title").GetString());
        Assert.Equal("", ev[1].GetProperty("title").GetString());
    }

    [Fact]
    public void Junta_varias_agendas_e_repassa_quantas_ficaram_de_fora()
    {
        var d = CalendarPlugin.Build([("Trabalho", Ics), ("Pessoal", Ics), ("Fora do ar", null)], Now, Zone);
        Assert.Equal(2 * Events(CalendarPlugin.Build([("Trabalho", Ics)], Now, Zone)).Count, Events(d).Count);
        Assert.Equal([0, 1], Events(d).Select(e => e.GetProperty("calendar").GetInt32()).Distinct().Order().ToArray());
        var cals = d.GetProperty("calendars").EnumerateArray().ToList();
        Assert.Equal(["Trabalho", "Pessoal", "Fora do ar"], cals.Select(c => c.GetProperty("name").GetString()).ToArray());
        Assert.Equal([true, true, false], cals.Select(c => c.GetProperty("ok").GetBoolean()).ToArray());
    }

    [Fact]
    public void Sem_eventos_a_lista_vai_vazia_e_nao_nula()
    {
        var d = CalendarPlugin.Build([("A", "BEGIN:VCALENDAR\nVERSION:2.0\nPRODID:-//T//PT\nEND:VCALENDAR\n")], Now, Zone);
        Assert.Empty(Events(d));
    }

    [Fact]
    public void A_cultura_do_windows_nao_muda_o_resultado()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
            var d = CalendarPlugin.Build([("Trabalho", Ics)], Now, Zone);
            Assert.Equal("2026-11-16T09:00:00-03:00", Events(d).First(e => e.GetProperty("title").GetString() == "Reunião de equipe").GetProperty("start").GetString());
            Assert.Equal("2026-11-15T12:00:00-03:00", d.GetProperty("updatedAt").GetString());
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    // ---- links

    [Theory]
    [InlineData("Trabalho https://example.com/cal/FAKEKEY0123456789ABCDEF/basic.ics", "Trabalho")]
    [InlineData("https://a.example/x.ics", "Agenda 1")]                          // sem nome
    [InlineData("Pessoal: https://a.example/x.ics", "Pessoal")]
    [InlineData("  Minha agenda = https://a.example/x.ics  ", "Minha agenda")]
    public void Uma_agenda_com_nome_opcional(string raw, string name)
    {
        var links = CalendarPlugin.ParseLinks(raw, out var error);
        Assert.Null(error);
        Assert.Equal(name, links.Single().Name);
        Assert.Equal("https", links.Single().Url.Scheme);
    }

    [Theory]
    [InlineData("Trabalho https://a.example/x.ics; Pessoal https://b.example/y.ics")]
    [InlineData("Trabalho https://a.example/x.ics\nPessoal https://b.example/y.ics")]
    [InlineData("Trabalho https://a.example/x.ics\r\nPessoal https://b.example/y.ics;")]
    public void Varias_agendas_separadas_por_linha_ou_ponto_e_virgula_na_ordem(string raw)
    {
        var links = CalendarPlugin.ParseLinks(raw, out var error);
        Assert.Null(error);
        Assert.Equal(["Trabalho", "Pessoal"], links.Select(l => l.Name).ToArray());
    }

    [Fact]
    public void Sem_nome_a_numeracao_segue_a_ordem()
    {
        var links = CalendarPlugin.ParseLinks("https://a.example/x.ics; Casa https://b.example/y.ics; https://c.example/z.ics", out _);
        Assert.Equal(["Agenda 1", "Casa", "Agenda 3"], links.Select(l => l.Name).ToArray());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("http://example.com/x.ics")]                       // só https
    [InlineData("ftp://example.com/x.ics")]
    [InlineData("Trabalho example.com/x.ics")]
    [InlineData("https://user:senha@example.com/x.ics")]           // sem usuário e senha no endereço
    [InlineData("webcal://example.com/x.ics")]
    [InlineData("https://a.example/x.ics https://b.example/y.ics")]   // dois links na mesma entrada
    [InlineData("Nome comprido demais para caber na tela https://a.example/x.ics")]
    public void Entradas_invalidas_sao_recusadas_sem_repetir_o_link(string? raw)
    {
        var links = CalendarPlugin.ParseLinks(raw, out var error);
        Assert.Empty(links);
        Assert.NotNull(error);
        Assert.DoesNotContain("example.com", error);
        Assert.DoesNotContain("senha", error);
    }

    [Fact]
    public void No_maximo_cinco_agendas()
    {
        CalendarPlugin.ParseLinks(string.Join(';', Enumerable.Range(1, 6).Select(i => $"https://e{i}.example/x.ics")), out var error);
        Assert.NotNull(error);
        CalendarPlugin.ParseLinks(string.Join(';', Enumerable.Range(1, 5).Select(i => $"https://e{i}.example/x.ics")), out var ok);
        Assert.Null(ok);
    }

    [Fact]
    public async Task Sem_link_configurado_a_fonte_pede_a_chave_certa()
    {
        var plugin = new CalendarPlugin();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => plugin.StartAsync(new FakeCtx(new Dictionary<string, string?>()), CancellationToken.None));
        Assert.Contains("Plugins:calendar:urls", ex.Message);
    }

    [Fact]
    public async Task Acoes_refresh_ok_e_desconhecida_recusada()
    {
        var plugin = new CalendarPlugin();
        Assert.True((await plugin.ExecuteAsync("refresh", Json.EmptyObject, CancellationToken.None)).Ok);
        var bad = await plugin.ExecuteAsync("apagar", Json.EmptyObject, CancellationToken.None);
        Assert.False(bad.Ok);
        Assert.Equal("unknown_action", bad.Error);
    }

    [Fact]
    public async Task O_simulador_publica_o_mesmo_formato_e_um_evento_logo_a_frente()
    {
        var sim = new SimCalendar();
        var d = (await sim.CollectAsync(CancellationToken.None))!.Value;
        Assert.Equal("calendar", sim.Id);
        var events = Events(d);
        Assert.NotEmpty(events);
        foreach (var name in new[] { "title", "start", "end", "allDay", "calendar" }) Assert.True(events[0].TryGetProperty(name, out _), name);
        Assert.Equal(["Trabalho", "Pessoal"], d.GetProperty("calendars").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToArray());
        Assert.Contains(events, e => e.GetProperty("title").GetString() == "Evento em breve");
    }

    [Fact]
    public void O_campo_da_janela_e_segredo_normaliza_e_recusa_sem_repetir_o_link()
    {
        var f = Atalaia.Agent.Host.Sources.SourceCredentials.For("calendar").Single();
        Assert.True(f.Secret);
        Assert.True(Atalaia.Agent.Host.Sources.SourceCredentials.TryValidate(f, "  Trabalho:  https://a.example/x.ics ;https://b.example/y.ics ", out var normalized, out _));
        Assert.Equal("Trabalho https://a.example/x.ics; Agenda 2 https://b.example/y.ics", normalized);
        Assert.False(Atalaia.Agent.Host.Sources.SourceCredentials.TryValidate(f, "http://a.example/x.ics", out _, out var error));
        Assert.DoesNotContain("a.example", error);
    }

    private sealed class FakeCtx(IDictionary<string, string?> options) : IPluginContext
    {
        public IConfiguration Options { get; } = new ConfigurationBuilder().AddInMemoryCollection(options).Build();
        public IEventSink Events => null!;
        public IBlobStore Blobs => null!;
        public ILogger Logger => null!;
    }
}

/// <summary>A lista de agendas da janela: só os nomes voltam para a página; o link é segredo e fica no cofre.</summary>
public class CalendarWindowTests : StepTestBase
{
    private const string KeyName = "Plugins:calendar:urls";
    private const string LinkA = "https://a.example/ical/FAKEKEY0123456789ABCDEF/basic.ics";
    private const string LinkB = "https://b.example/ical/FAKEKEY0123456789ABCDEG/basic.ics";
    private const string LinkC = "https://c.example/ical/FAKEKEY0123456789ABCDEH/basic.ics";

    private async Task<JsonElement> Source() =>
        (await H.Data("sources.get")).GetProperty("sources").EnumerateArray().Single(s => s.GetProperty("id").GetString() == "calendar");

    private static string[] Names(JsonElement source) => source.GetProperty("calendars").EnumerateArray().Select(c => c.GetProperty("name").GetString()!).ToArray();

    private string? Saved() => H.Vault.Read().GetValueOrDefault(KeyName);

    [Fact]
    public async Task Sem_nada_guardado_a_lista_vem_vazia()
    {
        Assert.Empty(Names(await Source()));
    }

    [Fact]
    public async Task Salvar_guarda_no_cofre_e_so_os_nomes_voltam_para_a_pagina()
    {
        await H.Data("sources.setCalendars", new { items = new object[] { new { name = "Trabalho", url = LinkA }, new { name = "Pessoal", url = LinkB } } });

        Assert.Equal($"Trabalho {LinkA}; Pessoal {LinkB}", Saved());
        var source = await Source();
        Assert.Equal(["Trabalho", "Pessoal"], Names(source));
        Assert.DoesNotContain("a.example", source.ToString());          // o link nunca volta
        Assert.DoesNotContain("FAKEKEY", source.ToString());
        Assert.True(source.GetProperty("fields")[0].GetProperty("configured").GetBoolean());
        Assert.False(source.GetProperty("fields")[0].TryGetProperty("value", out _));
    }

    [Fact]
    public async Task Manter_reaproveita_o_link_guardado_mesmo_com_o_nome_mudado()
    {
        await H.Data("sources.setCalendars", new { items = new object[] { new { name = "Trabalho", url = LinkA }, new { name = "Pessoal", url = LinkB } } });
        await H.Data("sources.setCalendars", new { items = new object[] { new { name = "Casa", keep = 1 }, new { name = "Trabalho", keep = 0 }, new { name = "", url = LinkC } } });

        Assert.Equal($"Casa {LinkB}; Trabalho {LinkA}; {LinkC}", Saved());     // reordenou; o sem nome fica sem nome
        Assert.Equal(["Casa", "Trabalho", "Agenda 3"], Names(await Source()));
    }

    [Fact]
    public async Task Remover_uma_agenda_do_meio_mantem_os_links_das_outras()
    {
        await H.Data("sources.setCalendars", new { items = new object[] { new { name = "A", url = LinkA }, new { name = "B", url = LinkB }, new { name = "C", url = LinkC } } });
        await H.Data("sources.setCalendars", new { items = new object[] { new { name = "A", keep = 0 }, new { name = "C", keep = 2 } } });

        Assert.Equal($"A {LinkA}; C {LinkC}", Saved());
    }

    [Fact]
    public async Task Lista_vazia_apaga_a_configuracao()
    {
        await H.Data("sources.setCalendars", new { items = new object[] { new { name = "A", url = LinkA } } });
        await H.Data("sources.setCalendars", new { items = Array.Empty<object>() });

        Assert.Null(Saved());
        Assert.Empty(Names(await Source()));
    }

    [Theory]
    [InlineData("http://a.example/x.ics")]                       // só https
    [InlineData("a.example/x.ics")]
    [InlineData("https://user:senha@a.example/x.ics")]
    [InlineData("Nome https://a.example/x.ics")]                 // o nome vai no campo do nome, não no do link
    [InlineData("https://a.example/x.ics https://b.example/y.ics")]
    public async Task Link_invalido_e_recusado_sem_repetir_o_valor_e_nada_muda(string url)
    {
        await H.Data("sources.setCalendars", new { items = new object[] { new { name = "A", url = LinkA } } });
        var r = await H.Call("sources.setCalendars", new { items = new object[] { new { name = "B", url = LinkB }, new { name = "X", url } } });

        Assert.False(r.GetProperty("ok").GetBoolean());
        Assert.Equal("invalid_fields", r.GetProperty("error").GetString());
        var message = r.GetProperty("message").GetString()!;
        Assert.Contains("Agenda 2", message);                    // diz qual
        Assert.DoesNotContain("a.example", message);
        Assert.DoesNotContain("senha", message);
        Assert.Equal($"A {LinkA}", Saved());                     // a recusa não mudou nada
    }

    [Fact]
    public async Task Agenda_sem_link_e_sem_manter_e_recusada()
    {
        Assert.Equal("invalid_fields", await H.ErrorOf("sources.setCalendars", new { items = new object[] { new { name = "A" } } }));
        Assert.Equal("invalid_fields", await H.ErrorOf("sources.setCalendars", new { items = new object[] { new { name = "A", keep = 0 } } }));   // nada guardado ainda
        Assert.Null(Saved());
    }

    [Theory]
    [InlineData("Nome; outro")]
    [InlineData("Minha http agenda")]
    [InlineData("Nome comprido demais para caber")]
    public async Task Nome_invalido_e_recusado(string name)
    {
        Assert.Equal("invalid_fields", await H.ErrorOf("sources.setCalendars", new { items = new object[] { new { name, url = LinkA } } }));
        Assert.Null(Saved());
    }

    [Fact]
    public async Task Forma_errada_do_pedido_e_recusada()
    {
        Assert.Equal("invalid_args", await H.ErrorOf("sources.setCalendars"));
        Assert.Equal("invalid_args", await H.ErrorOf("sources.setCalendars", new { items = "x" }));
        Assert.Equal("invalid_args", await H.ErrorOf("sources.setCalendars", new { items = new object[] { "texto" } }));
        var six = Enumerable.Range(1, 6).Select(i => (object)new { name = $"A{i}", url = $"https://e{i}.example/x.ics" }).ToArray();
        Assert.Equal("invalid_args", await H.ErrorOf("sources.setCalendars", new { items = six }));
    }

    [Fact]
    public async Task Variavel_de_ambiente_manda_sobre_a_lista()
    {
        H.Env["ATALAIA_Plugins__calendar__urls"] = "Env https://env.example/x.ics";
        Assert.Equal("overridden", await H.ErrorOf("sources.setCalendars", new { items = new object[] { new { name = "A", url = LinkA } } }));
        Assert.Null(Saved());
    }
}
