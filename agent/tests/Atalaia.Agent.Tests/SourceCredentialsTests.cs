// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Atalaia.Agent.Host;
using Atalaia.Agent.Host.Settings;
using Atalaia.Agent.Host.Sources;

namespace Atalaia.Agent.Tests;

// Todos os valores abaixo são inventados (nenhuma chave, ID ou coordenada de verdade).

public class SourceCredentialsValidationTests
{
    private static CredentialField Field(string source, string name) => SourceCredentials.For(source).Single(f => f.Name == name);

    [Theory]
    [InlineData("-23.55", "-23.55")]
    [InlineData(" 0 ", "0")]
    [InlineData("90", "90")]
    [InlineData("-90", "-90")]
    [InlineData("12.3456789012", "12.3456789")]
    public void Latitude_valida_e_normaliza(string raw, string expected)
    {
        Assert.True(SourceCredentials.TryValidate(Field("weather", "latitude"), raw, out var value, out var error), error);
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("", "preencha")]
    [InlineData("   ", "preencha")]
    [InlineData("90.1", "entre -90 e 90")]
    [InlineData("-91", "entre -90 e 90")]
    [InlineData("-23,55", "ponto")]          // vírgula decimal: recusada com a dica
    [InlineData("abc", "graus decimais")]
    [InlineData("NaN", "graus decimais")]
    [InlineData("1e1", "graus decimais")]    // notação científica não é "grau decimal"
    public void Latitude_invalida_explica_em_portugues(string raw, string pedaco)
    {
        Assert.False(SourceCredentials.TryValidate(Field("weather", "latitude"), raw, out _, out var error));
        Assert.Contains(pedaco, error);
        Assert.StartsWith("Latitude", error);
    }

    [Theory]
    [InlineData("180", true)]
    [InlineData("-180", true)]
    [InlineData("-46.63", true)]
    [InlineData("180.01", false)]
    [InlineData("-181", false)]
    public void Longitude_vai_de_menos_180_a_180(string raw, bool ok) =>
        Assert.Equal(ok, SourceCredentials.TryValidate(Field("weather", "longitude"), raw, out _, out _));

    [Fact]
    public void Ponto_decimal_vale_mesmo_com_o_windows_em_pt_BR()
    {
        var before = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("pt-BR");
        try
        {
            Assert.True(SourceCredentials.TryValidate(Field("weather", "latitude"), "-23.55", out var v, out var e), e);
            Assert.Equal("-23.55", v);
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Theory]
    [InlineData("12345678901234567", true)]
    [InlineData("1234567890123456", false)]     // 16
    [InlineData("123456789012345678", false)]   // 18
    [InlineData("1234567890123456a", false)]
    [InlineData("1234567890 234567", false)]
    [InlineData("", false)]
    public void SteamId64_tem_17_digitos(string raw, bool ok) =>
        Assert.Equal(ok, SourceCredentials.TryValidate(Field("steam", "steamId64"), raw, out _, out _));

    [Theory]
    [InlineData("FAKEKEY0123456789ABCDEF", true)]
    [InlineData("", false)]
    [InlineData("com espaco", false)]
    [InlineData("quebra\nde linha", false)]
    public void Campos_de_texto_sao_obrigatorios_e_sem_espacos(string raw, bool ok) =>
        Assert.Equal(ok, SourceCredentials.TryValidate(Field("discord", "clientSecret"), raw, out _, out _));

    [Fact]
    public void Texto_grande_demais_e_recusado_sem_repetir_o_valor()
    {
        var big = new string('x', SourceCredentials.MaxLength + 1);
        Assert.False(SourceCredentials.TryValidate(Field("steam", "apiKey"), big, out _, out var error));
        Assert.DoesNotContain(big, error);
    }

    [Fact]
    public void Segredo_em_branco_mantem_o_guardado_mas_so_se_ja_existe()
    {
        var submitted = new Dictionary<string, string?> { ["steamId64"] = "12345678901234567", ["apiKey"] = "" };

        Assert.True(SourceCredentials.TryValidateForm("steam", submitted, f => f.Secret, out var changes, out _));
        Assert.Single(changes);   // só o steamId64; a chave segue como estava

        Assert.False(SourceCredentials.TryValidateForm("steam", submitted, _ => false, out _, out var errors));
        Assert.Contains(errors, e => e.StartsWith("Chave da Web API"));
    }

    [Fact]
    public void Formulario_junta_o_erro_de_cada_campo()
    {
        var submitted = new Dictionary<string, string?> { ["latitude"] = "100", ["longitude"] = "-200" };
        Assert.False(SourceCredentials.TryValidateForm("weather", submitted, _ => false, out var changes, out var errors));
        Assert.Empty(changes);
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void Segredos_sao_so_a_chave_da_steam_e_o_secret_do_discord()
    {
        var secrets = new[] { "weather", "steam", "discord" }.SelectMany(id => SourceCredentials.For(id).Where(f => f.Secret).Select(f => f.Key(id)));
        Assert.Equal(["Plugins:discord:clientSecret", "Plugins:steam:apiKey"], secrets.Order(StringComparer.Ordinal).ToArray());
        Assert.Empty(SourceCredentials.For("system"));
    }
}

public class SecretVaultTests
{
    private const string Fake = "FAKEKEY0123456789ABCDEF";

    [Fact]
    public void Guarda_cifrado_e_outra_instancia_le_do_disco()
    {
        using var dir = new TempDir();
        var path = dir.File("dados/secrets.dat");
        new SecretVault(path, new FakeProtector()).Update(v => v["Plugins:steam:apiKey"] = Fake);

        Assert.DoesNotContain(Fake, File.ReadAllText(path));   // o arquivo não guarda o texto puro
        var again = new SecretVault(path, new FakeProtector());
        Assert.True(again.Has("Plugins:steam:apiKey"));
        Assert.Equal(Fake, again.Read()["Plugins:steam:apiKey"]);
    }

    [Fact]
    public void Chave_nula_ou_vazia_remove()
    {
        using var dir = new TempDir();
        var vault = new SecretVault(dir.File("secrets.dat"), new FakeProtector());
        vault.Update(v => { v["a"] = "1"; v["b"] = "2"; });
        vault.Update(v => { v["a"] = null; v["b"] = ""; });
        Assert.Empty(vault.Read());
    }

    [Fact]
    public void Replace_volta_ao_conteudo_anterior()
    {
        using var dir = new TempDir();
        var vault = new SecretVault(dir.File("secrets.dat"), new FakeProtector());
        vault.Update(v => v["a"] = "1");
        var before = vault.Read();
        vault.Update(v => { v["a"] = "2"; v["b"] = "3"; });
        vault.Replace(before);
        Assert.Equal(new Dictionary<string, string> { ["a"] = "1" }, vault.Read().ToDictionary(p => p.Key, p => p.Value));
    }

    [Fact]
    public void Ilegivel_le_como_vazio_e_a_quarentena_guarda_o_arquivo_sem_apagar()
    {
        using var dir = new TempDir();
        var path = dir.File("secrets.dat");
        var protector = new FakeProtector();
        new SecretVault(path, protector).Update(v => v["a"] = "1");

        protector.FailOnRead = true;   // outro usuário do Windows
        var vault = new SecretVault(path, protector);
        Assert.Empty(vault.Read());
        var saved = vault.QuarantineIfUnreadable();

        Assert.NotNull(saved);
        Assert.Contains(".ilegivel-", saved);
        Assert.True(File.Exists(saved));
        Assert.False(File.Exists(path));
        Assert.Null(vault.QuarantineIfUnreadable());   // nada mais a guardar
    }

    [Fact]
    public void Arquivo_bom_ou_ausente_nao_vai_para_a_quarentena()
    {
        using var dir = new TempDir();
        var vault = new SecretVault(dir.File("secrets.dat"), new FakeProtector());
        Assert.Null(vault.QuarantineIfUnreadable());
        vault.Update(v => v["a"] = "1");
        Assert.Null(vault.QuarantineIfUnreadable());
    }

    [Fact]
    public void Lixo_no_arquivo_tambem_e_ilegivel()
    {
        using var dir = new TempDir();
        var path = dir.File("secrets.dat");
        File.WriteAllBytes(path, [1, 2, 3]);   // o protetor falso "decifra" para bytes que não são JSON
        var vault = new SecretVault(path, new FakeProtector());
        Assert.Empty(vault.Read());
        Assert.NotNull(vault.QuarantineIfUnreadable());
    }

    [Fact]
    public void Dpapi_de_verdade_faz_ida_e_volta_e_recusa_dado_alheio()
    {
        // Só a DPAPI do Windows de quem roda o teste: ida e volta, e bytes que não são dela são recusados.
        var dpapi = new DpapiProtector();
        var cipher = dpapi.Protect(System.Text.Encoding.UTF8.GetBytes("valor"));
        Assert.Equal("valor", System.Text.Encoding.UTF8.GetString(dpapi.Unprotect(cipher)));
        Assert.ThrowsAny<CryptographicException>(() => dpapi.Unprotect([1, 2, 3, 4]));
    }
}

public class ConfigPrecedenceTests
{
    private const string EnvName = "ATALAIA_Plugins__steam__apiKey";

    private static (TempDir dir, SettingsStore settings, SecretVault vault) Setup()
    {
        var dir = new TempDir();
        File.WriteAllText(dir.File("appsettings.json"), "{}");
        return (dir, new SettingsStore(dir.File("dados/settings.json")), new SecretVault(dir.File("dados/secrets.dat"), new FakeProtector()));
    }

    [Fact]
    public void Cofre_entra_na_configuracao_acima_do_settings_json()
    {
        var (dir, settings, vault) = Setup();
        using (dir)
        {
            settings.Set("Plugins:steam:apiKey", "DOSETTINGS");
            settings.Set("Plugins:steam:steamId64", "12345678901234567");
            vault.Update(v => v["Plugins:steam:apiKey"] = "DOCOFRE");

            var config = HostConfig.Compose(dir.Path, settings, vault);
            Assert.Equal("DOCOFRE", config["Plugins:steam:apiKey"]);
            Assert.Equal("12345678901234567", config["Plugins:steam:steamId64"]);
        }
    }

    [Fact]
    public void Variavel_de_ambiente_manda_sobre_o_cofre()
    {
        var (dir, settings, vault) = Setup();
        using (dir)
        {
            vault.Update(v => v["Plugins:steam:apiKey"] = "DOCOFRE");
            var before = Environment.GetEnvironmentVariable(EnvName);
            Environment.SetEnvironmentVariable(EnvName, "DOAMBIENTE");
            try { Assert.Equal("DOAMBIENTE", HostConfig.Compose(dir.Path, settings, vault)["Plugins:steam:apiKey"]); }
            finally { Environment.SetEnvironmentVariable(EnvName, before); }
            Assert.Equal("DOCOFRE", HostConfig.Compose(dir.Path, settings, vault)["Plugins:steam:apiKey"]);   // sem a variável, volta o cofre
        }
    }

    [Fact]
    public void Sem_cofre_a_composicao_segue_como_antes()
    {
        var (dir, settings, _) = Setup();
        using (dir) Assert.Null(HostConfig.Compose(dir.Path, settings)["Plugins:steam:apiKey"]);
    }
}

public class SourceConfigureCommandsTests : StepTestBase
{
    private const string Fake = "FAKEKEY0123456789ABCDEF";
    private const string FakeId = "12345678901234567";

    private async Task<JsonElement> Source(string id) =>
        (await H.Data("sources.get")).GetProperty("sources").EnumerateArray().Single(s => s.GetProperty("id").GetString() == id);

    [Fact]
    public async Task Clima_grava_no_settings_json_e_a_fonte_enxerga_o_valor()
    {
        var d = await H.Data("sources.configure", new { id = "weather", values = new { latitude = "-23.55", longitude = "-46.63" } });

        Assert.Equal("-23.55", H.Settings.Read()["Plugins"]!["weather"]!["latitude"]!.GetValue<string>());
        Assert.Equal("-46.63", H.Runtime.Config["Plugins:weather:longitude"]);
        Assert.Empty(H.Vault.Read());   // não é segredo: nada no cofre
        var weather = d.GetProperty("sources").EnumerateArray().Single(s => s.GetProperty("id").GetString() == "weather");
        Assert.Equal("-23.55", weather.GetProperty("fields")[0].GetProperty("value").GetString());   // não segredo volta para preencher o campo
    }

    [Fact]
    public async Task Segredo_vai_para_o_cofre_e_nunca_para_o_settings_json_nem_volta_para_a_pagina()
    {
        var response = await H.Call("sources.configure", new { id = "steam", values = new { apiKey = Fake, steamId64 = FakeId } });
        Assert.True(response.GetProperty("ok").GetBoolean(), response.ToString());

        Assert.Equal(Fake, H.Vault.Read()["Plugins:steam:apiKey"]);
        Assert.Equal(Fake, H.Runtime.Config["Plugins:steam:apiKey"]);                  // o agente lê do cofre ao montar a configuração
        var settingsText = File.ReadAllText(H.Settings.FilePath);
        Assert.DoesNotContain(Fake, settingsText);
        Assert.Equal(FakeId, H.Settings.Read()["Plugins"]!["steam"]!["steamId64"]!.GetValue<string>());
        Assert.DoesNotContain("apiKey", settingsText);
        Assert.DoesNotContain(Fake, File.ReadAllText(H.Vault.FilePath));               // e o cofre está cifrado

        Assert.DoesNotContain(Fake, response.ToString());                              // resposta do comando
        var get = (await H.Call("sources.get")).ToString();
        Assert.DoesNotContain(Fake, get);                                              // e a consulta seguinte
        var key = (await Source("steam")).GetProperty("fields")[0];
        Assert.True(key.GetProperty("secret").GetBoolean());
        Assert.True(key.GetProperty("configured").GetBoolean());
        Assert.False(key.TryGetProperty("value", out _));
    }

    [Fact]
    public async Task Discord_guarda_o_secret_no_cofre_e_o_clientId_no_settings_json()
    {
        await H.Data("sources.configure", new { id = "discord", values = new { clientId = FakeId, clientSecret = Fake } });
        Assert.Equal(Fake, H.Vault.Read()["Plugins:discord:clientSecret"]);
        Assert.DoesNotContain("clientSecret", File.ReadAllText(H.Settings.FilePath));
        Assert.Equal(FakeId, H.Settings.Read()["Plugins"]!["discord"]!["clientId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Segredo_em_branco_mantem_o_que_estava_e_troca_so_o_outro_campo()
    {
        await H.Data("sources.configure", new { id = "steam", values = new { apiKey = Fake, steamId64 = FakeId } });
        await H.Data("sources.configure", new { id = "steam", values = new { apiKey = "", steamId64 = "76561190000000000" } });
        Assert.Equal(Fake, H.Vault.Read()["Plugins:steam:apiKey"]);
        Assert.Equal("76561190000000000", H.Runtime.Config["Plugins:steam:steamId64"]);
    }

    [Fact]
    public async Task Trocar_o_segredo_substitui_o_valor_do_cofre()
    {
        await H.Data("sources.configure", new { id = "steam", values = new { apiKey = Fake, steamId64 = FakeId } });
        await H.Data("sources.configure", new { id = "steam", values = new { apiKey = "OUTRAFALSA987654321", steamId64 = FakeId } });
        Assert.Equal("OUTRAFALSA987654321", H.Vault.Read()["Plugins:steam:apiKey"]);
    }

    [Fact]
    public async Task Validacao_recusa_com_mensagem_em_portugues_e_nao_grava_nada()
    {
        var r = await H.Call("sources.configure", new { id = "weather", values = new { latitude = "-23,55", longitude = "300" } });
        Assert.Equal("invalid_fields", r.GetProperty("error").GetString());
        var message = r.GetProperty("message").GetString()!;
        Assert.Contains("Latitude", message);
        Assert.Contains("Longitude", message);
        Assert.False(H.Settings.Read().ContainsKey("Plugins"));

        Assert.Equal("invalid_fields", await H.ErrorOf("sources.configure", new { id = "steam", values = new { apiKey = Fake, steamId64 = "123" } }));
        Assert.Empty(H.Vault.Read());   // nem o segredo válido vai junto de um formulário com erro
        Assert.Equal("invalid_fields", await H.ErrorOf("sources.configure", new { id = "discord", values = new { clientId = FakeId } }));   // campo obrigatório faltando
    }

    [Fact]
    public async Task Argumentos_estranhos_sao_recusados()
    {
        Assert.Equal("invalid_args", await H.ErrorOf("sources.configure", new { id = "system", values = new { x = "1" } }));
        Assert.Equal("invalid_args", await H.ErrorOf("sources.configure", new { id = "inexistente", values = new { x = "1" } }));
        Assert.Equal("invalid_args", await H.ErrorOf("sources.configure", new { id = "weather", values = new { latitude = "1", longitude = "2", enabled = "true" } }));   // só os campos da fonte
        Assert.Equal("invalid_args", await H.ErrorOf("sources.configure", new { id = "weather", values = new { latitude = 1, longitude = 2 } }));   // número no lugar de texto
        Assert.Equal("invalid_args", await H.ErrorOf("sources.configure", new { id = "weather" }));
        Assert.Equal("invalid_args", await H.ErrorOf("sources.clearConfig", new { id = "system" }));
    }

    [Fact]
    public async Task Se_o_agente_nao_sobe_a_mudanca_e_desfeita_no_settings_json_e_no_cofre()
    {
        await H.Data("sources.configure", new { id = "steam", values = new { apiKey = Fake, steamId64 = FakeId } });
        var settingsBefore = File.ReadAllText(H.Settings.FilePath);
        var vaultBefore = File.ReadAllBytes(H.Vault.FilePath);

        H.ApplyOverride = () => throw new InvalidOperationException("subir falhou");
        Assert.Equal("apply_failed", await H.ErrorOf("sources.configure", new { id = "steam", values = new { apiKey = "OUTRAFALSA987654321", steamId64 = "76561190000000000" } }));

        Assert.Equal(Fake, H.Vault.Read()["Plugins:steam:apiKey"]);
        Assert.Equal(FakeId, H.Settings.Read()["Plugins"]!["steam"]!["steamId64"]!.GetValue<string>());
        Assert.Equal(JsonDocument.Parse(settingsBefore).RootElement.ToString(), JsonDocument.Parse(File.ReadAllText(H.Settings.FilePath)).RootElement.ToString());
        Assert.Equal(vaultBefore, File.ReadAllBytes(H.Vault.FilePath));   // a cifra falsa é determinística: o arquivo volta idêntico
    }

    [Fact]
    public async Task Primeira_configuracao_que_falha_ao_religar_nao_deixa_nada_no_cofre()
    {
        H.ApplyOverride = () => throw new InvalidOperationException("subir falhou");
        Assert.Equal("apply_failed", await H.ErrorOf("sources.configure", new { id = "steam", values = new { apiKey = Fake, steamId64 = FakeId } }));
        Assert.Empty(H.Vault.Read());
        Assert.False(H.Settings.Read().ContainsKey("Plugins"));
    }

    [Fact]
    public async Task Se_o_valor_efetivo_nao_mudou_algo_esta_mandando_e_a_gravacao_e_desfeita()
    {
        H.ApplyOverride = () => Task.CompletedTask;   // religou, mas a configuração em uso é a antiga: como se uma variável de ambiente mandasse
        Assert.Equal("overridden", await H.ErrorOf("sources.configure", new { id = "steam", values = new { apiKey = Fake, steamId64 = FakeId } }));
        Assert.Empty(H.Vault.Read());
        Assert.False(H.Settings.Read().ContainsKey("Plugins"));
    }

    [Fact]
    public async Task Campo_fixado_por_variavel_de_ambiente_e_recusado_com_o_nome_da_variavel_e_sem_gravar()
    {
        H.Env["ATALAIA_Plugins__steam__apiKey"] = "DOAMBIENTE";
        var steam = await Source("steam");
        var key = steam.GetProperty("fields")[0];
        Assert.True(key.GetProperty("fromEnv").GetBoolean());
        Assert.Equal("ATALAIA_Plugins__steam__apiKey", key.GetProperty("envName").GetString());
        Assert.DoesNotContain("DOAMBIENTE", steam.ToString());

        var r = await H.Call("sources.configure", new { id = "steam", values = new { apiKey = Fake, steamId64 = FakeId } });
        Assert.Equal("overridden", r.GetProperty("error").GetString());
        Assert.Contains("ATALAIA_Plugins__steam__apiKey", r.GetProperty("message").GetString());
        Assert.Empty(H.Vault.Read());
        Assert.False(H.Settings.Read().ContainsKey("Plugins"));
    }

    [Fact]
    public async Task Remover_limpa_settings_json_e_cofre_e_religa()
    {
        await H.Data("sources.configure", new { id = "steam", values = new { apiKey = Fake, steamId64 = FakeId } });
        var d = await H.Data("sources.clearConfig", new { id = "steam" });

        Assert.Empty(H.Vault.Read());
        Assert.Null(H.Settings.Read()["Plugins"]?["steam"]?["steamId64"]);
        Assert.True(string.IsNullOrWhiteSpace(H.Runtime.Config["Plugins:steam:apiKey"]));
        var steam = d.GetProperty("sources").EnumerateArray().Single(s => s.GetProperty("id").GetString() == "steam");
        Assert.All(steam.GetProperty("fields").EnumerateArray(), f => Assert.False(f.GetProperty("configured").GetBoolean()));
    }

    [Fact]
    public async Task Remover_que_falha_ao_religar_restaura_o_segredo()
    {
        await H.Data("sources.configure", new { id = "steam", values = new { apiKey = Fake, steamId64 = FakeId } });
        H.ApplyOverride = () => throw new InvalidOperationException("subir falhou");
        Assert.Equal("apply_failed", await H.ErrorOf("sources.clearConfig", new { id = "steam" }));
        Assert.Equal(Fake, H.Vault.Read()["Plugins:steam:apiKey"]);
        Assert.Equal(FakeId, H.Settings.Read()["Plugins"]!["steam"]!["steamId64"]!.GetValue<string>());
    }

    [Fact]
    public async Task Fontes_sem_formulario_nao_trazem_fields()
    {
        Assert.False((await Source("system")).TryGetProperty("fields", out _));
        Assert.Equal(2, (await Source("weather")).GetProperty("fields").GetArrayLength());
    }
}
