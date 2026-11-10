// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Text.Json;
using System.Text.Json.Nodes;
using Atalaia.Agent.Core;
using Atalaia.Agent.Host.Adb;
using Atalaia.Agent.Host.Install;
using Atalaia.Agent.Host.Network;
using Atalaia.Agent.Host.Settings;
using Atalaia.Agent.Host.Sources;
using Atalaia.Agent.Host.Startup;
using Atalaia.Agent.Host.Updates;
using Atalaia.Agent.Host.Usb;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atalaia.Agent.Host.Ui;

/// <summary>Tudo o que os comandos dos passos precisam do mundo de fora, por costuras: o teste troca cada uma por uma versão falsa.</summary>
internal sealed record StepServices(
    AgentRuntime Runtime,
    SettingsStore Settings,
    SecretVault Vault,                            // segredos digitados na janela (DPAPI); o valor nunca volta para a página
    Func<Task> ApplyConfig,                      // religa o agente com a configuração nova (RestartAsync)
    IFirewall Firewall,
    AdbService Adb,
    Func<ApkProvider?> Apk,                       // null = não dá para baixar (sem repositório válido)
    Func<IReadOnlyList<LanAdapter>> ReadAdapters,
    Func<int, bool> IsPortFree,
    Func<string, SourceProbe> Probe,              // o ambiente de uma fonte: arquivos, pastas e variáveis
    AutostartService Autostart,
    Func<string?> PickAdb,                        // caixa "Procurar…" (só na thread da janela)
    Func<string?> PickApk,
    Action<string> CopyText,
    Action<string> OpenExternal);

/// <summary>
/// Os comandos dos seis passos do onboarding: rede e Firewall, USB e adb, app no tablet, código, fontes e resumo. A janela só pergunta e
/// desenha; quem decide o que é verdade (o que o Firewall diz, o que o adb vê, o que está instalado) é este código. Mudança de configuração
/// grava no <c>settings.json</c>, religa o agente e <b>desfaz</b> a gravação se o agente não subir ou se uma variável de ambiente
/// mantiver o valor antigo.
/// </summary>
internal sealed class StepCommands(StepServices s, ILogger? logger = null)
{
    private const string PlatformToolsUrl = "https://developer.android.com/tools/releases/platform-tools";
    private readonly ILogger _log = logger ?? NullLogger.Instance;
    private readonly InstallJob _job = new();
    private string? _localApk;

    public void Register(CommandRouter r)
    {
        r.Register("network.get", _ => NetworkGet());
        r.Register("network.setLan", a => NetworkSetLan(Bool(a, "enabled")));
        r.Register("network.setPort", a => NetworkSetPort(CommandRouter.Int(a, "port")));
        r.Register("network.setAdapter", a => NetworkSetAdapter(CommandRouter.Text(a, "name", 128)));
        r.Register("network.firewallCreate", _ => FirewallCreate());
        r.Register("network.verify", _ => NetworkVerify());

        r.Register("usb.get", _ => UsbGet());
        r.Register("usb.setEnabled", a => UsbSetEnabled(Bool(a, "enabled")));
        r.Register("usb.browse", _ => UsbBrowse());
        r.Register("usb.openDownload", _ => { s.OpenExternal(PlatformToolsUrl); return Task.FromResult<JsonNode?>(null); });

        r.Register("app.get", _ => AppGet());
        r.Register("app.install", a => AppInstall(OptionalText(a, "serial"), uninstallFirst: false));
        r.Register("app.uninstallAndInstall", a => Bool(a, "confirm") ? AppInstall(OptionalText(a, "serial"), uninstallFirst: true)
            : throw new CommandException("invalid_args", "Falta a confirmação para desinstalar."));
        r.Register("app.pickLocal", _ => AppPickLocal());
        r.Register("app.useRelease", _ => { _localApk = null; return AppGet(); });

        r.Register("code.get", _ => CodeGet());
        r.Register("code.copy", _ => CodeCopy());

        r.Register("sources.get", _ => SourcesGet());
        r.Register("sources.set", a => SourcesSet(CommandRouter.Text(a, "id", 32), Bool(a, "enabled")));
        r.Register("sources.configure", a => SourcesConfigure(CommandRouter.Text(a, "id", 32), FormValues(a)));
        r.Register("sources.clearConfig", a => SourcesClearConfig(CommandRouter.Text(a, "id", 32)));

        r.Register("summary.get", _ => Summary());
        r.Register("startup.set", a => StartupSet(Bool(a, "enabled")));
        r.Register("updates.setAuto", a => UpdatesSetAuto(Bool(a, "enabled")));
    }

    // ================================================================ rede e Firewall

    private async Task<JsonNode?> NetworkGet()
    {
        var chosen = ChosenAdapter(out var adapters);
        var port = s.Runtime.Port;
        var fw = await ReadFirewallAsync();
        return new JsonObject
        {
            ["adapters"] = new JsonArray([.. adapters.Select(a => (JsonNode)new JsonObject
            {
                ["name"] = a.Name, ["ip"] = a.Ip, ["gateway"] = a.HasGateway, ["selected"] = ReferenceEquals(a, chosen),
            })]),
            ["selectedIp"] = chosen?.Ip,
            ["port"] = port,
            ["listenOnLan"] = s.Runtime.ListenOnLan,
            ["wifiAddress"] = chosen is null ? null : $"{chosen.Ip}:{port}",
            ["usbAddress"] = $"127.0.0.1:{port}",
            ["firewall"] = FirewallJson(fw, port),
        };
    }

    private async Task<JsonNode?> NetworkSetLan(bool enabled)
    {
        await ApplyAsync(root => SettingsStore.SetPath(root, "Agent:ListenOnLan", enabled), () => s.Runtime.ListenOnLan == enabled,
            "O Wi-Fi está fixado por uma variável de ambiente (ATALAIA_Agent__ListenOnLan): remova-a para mudar por aqui.");
        return await NetworkGet();
    }

    private async Task<JsonNode?> NetworkSetPort(int port)
    {
        var problem = PortValidator.Check(port, s.Runtime.Port, s.IsPortFree);
        if (problem != PortProblem.None) throw new CommandException("invalid_port", PortValidator.Message(problem));
        if (port != s.Runtime.Port)
            await ApplyAsync(root => SettingsStore.SetPath(root, "Agent:Port", port), () => s.Runtime.Port == port,
                "A porta está fixada por uma variável de ambiente (ATALAIA_Agent__Port): remova-a para mudar por aqui.");
        return await NetworkGet();
    }

    private async Task<JsonNode?> NetworkSetAdapter(string name)
    {
        if (!s.ReadAdapters().Any(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new CommandException("invalid_args", "Essa placa de rede não está mais disponível.");
        s.Settings.Set("Network:PreferredAdapter", name);   // só muda o que a tela mostra: não precisa religar o agente
        return await NetworkGet();
    }

    private async Task<JsonNode?> FirewallCreate()
    {
        try { await s.Firewall.CreateRuleAsync(s.Runtime.Port, CancellationToken.None); }
        catch (FirewallException ex) { throw new CommandException("firewall_failed", ex.Message); }
        return await NetworkGet();
    }

    /// <summary>
    /// "Verificar": o que dá para <b>garantir</b> daqui. Que o agente escuta na rede, que há regra de Firewall para a porta e que a rede
    /// é privada. Chamar o próprio IP não provaria nada (o tráfego local não passa pelo Firewall); a prova de que o tablet entra é ele
    /// conectar de verdade, no passo do código.
    /// </summary>
    private async Task<JsonNode?> NetworkVerify()
    {
        var port = s.Runtime.Port;
        var fw = await ReadFirewallAsync();
        var checks = new JsonArray
        {
            Check("listening", s.Runtime.ListenOnLan,
                s.Runtime.ListenOnLan ? $"O agente escuta na rede local (porta {port})." : "O Wi-Fi está desligado: o agente só atende este PC (USB)."),
        };
        if (fw is null)
        {
            checks.Add(Check("rule", false, "Não consegui ler o Firewall do Windows."));
            checks.Add(Check("profile", false, "Não consegui saber o tipo da rede."));
        }
        else
        {
            var state = FirewallEvaluator.Evaluate(fw.Rules, port, fw.CurrentProfiles, Environment.ProcessPath);
            checks.Add(Check("rule", state == FirewallState.Open, state switch
            {
                FirewallState.Open => $"Há uma regra de entrada liberando a porta {port}.",
                FirewallState.OtherProfile => $"Há uma regra para a porta {port}, mas ela não vale para o tipo de rede de agora.",
                FirewallState.Blocked => $"Há uma regra do Windows bloqueando a porta {port}.",
                _ => $"Nenhuma regra libera a porta {port}: o Wi-Fi não chega ao PC.",
            }));
            var priv = fw.CurrentProfiles.HasFlag(FwProfiles.Private);
            checks.Add(Check("profile", priv || fw.CurrentProfiles.HasFlag(FwProfiles.Domain), ProfileText(fw.CurrentProfiles)));
        }
        return new JsonObject
        {
            ["checks"] = checks,
            ["note"] = "Isto confere o PC. Que o tablet de fato consegue entrar só fica provado quando ele conecta, no passo do código de pareamento.",
        };
    }

    private async Task<FirewallSnapshot?> ReadFirewallAsync() => await Task.Run(s.Firewall.Read);

    private JsonNode FirewallJson(FirewallSnapshot? fw, int port)
    {
        if (fw is null) return new JsonObject { ["state"] = "unknown", ["profile"] = "unknown" };
        var state = FirewallEvaluator.Evaluate(fw.Rules, port, fw.CurrentProfiles, Environment.ProcessPath);
        return new JsonObject
        {
            ["state"] = state switch { FirewallState.Open => "open", FirewallState.OtherProfile => "other_profile", FirewallState.Blocked => "blocked", _ => "no_rule" },
            ["profile"] = fw.CurrentProfiles.HasFlag(FwProfiles.Private) ? "private" : fw.CurrentProfiles.HasFlag(FwProfiles.Public) ? "public" : fw.CurrentProfiles.HasFlag(FwProfiles.Domain) ? "domain" : "unknown",
        };
    }

    private static string ProfileText(FwProfiles p) =>
        p.HasFlag(FwProfiles.Private) ? "A rede atual está marcada como Privada."
        : p.HasFlag(FwProfiles.Domain) ? "O PC está numa rede de domínio."
        : p.HasFlag(FwProfiles.Public) ? "A rede atual está marcada como Pública: o Windows bloqueia a entrada por padrão. Mude para Privada em Configurações > Rede."
        : "Não deu para saber o tipo da rede.";

    private LanAdapter? ChosenAdapter(out IReadOnlyList<LanAdapter> adapters)
    {
        adapters = s.ReadAdapters();
        var preferred = s.Settings.Read()["Network"]?["PreferredAdapter"]?.GetValue<string>();
        return LanAddressProvider.Choose(adapters, preferred);
    }

    // ================================================================ USB e adb

    private async Task<JsonNode?> UsbGet()
    {
        var status = await s.Adb.GetStatusAsync(s.Runtime.Port, CancellationToken.None);
        return new JsonObject
        {
            ["enabled"] = UsbOptions.From(s.Runtime.Config).Enabled,
            ["adb"] = new JsonObject { ["found"] = status.Found, ["path"] = status.Path, ["source"] = status.Source, ["problem"] = status.Problem },
            ["devices"] = new JsonArray([.. status.Devices.Select(d => (JsonNode)new JsonObject
            {
                ["serial"] = d.Device.Serial,
                ["state"] = d.Device.State.ToString().ToLowerInvariant(),
                ["model"] = d.Device.Model,
                ["tunnel"] = d.Tunnel,
            })]),
        };
    }

    private async Task<JsonNode?> UsbSetEnabled(bool enabled)
    {
        await ApplyAsync(root => SettingsStore.SetPath(root, "Usb:Enabled", enabled), () => UsbOptions.From(s.Runtime.Config).Enabled == enabled,
            "O módulo USB está fixado por uma variável de ambiente (ATALAIA_Usb__Enabled): remova-a para mudar por aqui.");
        return await UsbGet();
    }

    private async Task<JsonNode?> UsbBrowse()
    {
        var path = s.PickAdb();
        if (path is null) return await UsbGet();   // cancelou a caixa
        if (!Path.GetFileName(path).Equals("adb.exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new CommandException("invalid_adb", "Escolha o arquivo adb.exe (da pasta platform-tools do Android).");
        await ApplyAsync(root => SettingsStore.SetPath(root, "Usb:AdbPath", path), () => UsbOptions.From(s.Runtime.Config).AdbPath == path,
            "O caminho do adb está fixado por uma variável de ambiente (ATALAIA_Usb__AdbPath): remova-a para mudar por aqui.");
        return await UsbGet();
    }

    // ================================================================ app no tablet

    private async Task<JsonNode?> AppGet()
    {
        var status = await s.Adb.GetStatusAsync(s.Runtime.Port, CancellationToken.None);
        var device = status.Devices.FirstOrDefault(d => d.Device.State == AdbDeviceState.Authorized)?.Device;
        string? installed = null;
        if (device is not null && !_job.Running) installed = await s.Adb.InstalledVersionAsync(device.Serial, CancellationToken.None);
        var provider = s.Apk();
        var repo = Repo();
        var releaseUrl = ReleaseLink.Page(repo, AppInfo.Version);
        return new JsonObject
        {
            ["appVersion"] = AppInfo.Version,
            ["adbFound"] = status.Found,
            ["device"] = device is null ? null : new JsonObject { ["serial"] = device.Serial, ["model"] = device.Model },
            ["unauthorized"] = status.Devices.Any(d => d.Device.State == AdbDeviceState.Unauthorized),
            ["installedVersion"] = installed,
            ["canDownload"] = provider?.CanDownload ?? false,
            ["apkName"] = provider?.ApkName,
            ["localApk"] = _localApk is null ? null : Path.GetFileName(_localApk),
            ["releaseUrl"] = releaseUrl,
            ["qr"] = ReleaseLink.QrDataUri(releaseUrl),
            ["job"] = _job.ToJson(),
        };
    }

    private async Task<JsonNode?> AppPickLocal()
    {
        var path = s.PickApk();
        if (path is null) return await AppGet();
        if (LocalApk.Validate(path) is { } problem) throw new CommandException("invalid_apk", problem);
        _localApk = path;
        return await AppGet();
    }

    private async Task<JsonNode?> AppInstall(string? serial, bool uninstallFirst)
    {
        if (_job.Running) throw new CommandException("busy", "Já há uma instalação em andamento.");
        var status = await s.Adb.GetStatusAsync(s.Runtime.Port, CancellationToken.None);
        var device = status.Devices.Where(d => d.Device.State == AdbDeviceState.Authorized)
            .FirstOrDefault(d => serial is null || d.Device.Serial == serial)?.Device
            ?? throw new CommandException("no_device", "Nenhum tablet autorizado pelo cabo. Volte ao passo \"USB e adb\".");
        var provider = s.Apk();
        if (_localApk is null && provider is not { CanDownload: true })
            throw new CommandException("no_apk", "Esta versão do agente não tem release publicada: escolha um APK do tablet no seu computador.");

        _job.Start(ct => RunInstallAsync(device.Serial, provider, _localApk, uninstallFirst, ct));
        return _job.ToJson();
    }

    private async Task RunInstallAsync(string serial, ApkProvider? provider, string? local, bool uninstallFirst, CancellationToken ct)
    {
        try
        {
            ApkFile apk;
            if (local is not null) apk = ApkFile.FromUserChoice(local);
            else
            {
                _job.Phase("download");
                var verified = await provider!.DownloadAsync(new Progress<double>(_job.Progress), ct);
                apk = ApkFile.From(verified);
            }

            _job.Phase("install");
            if (uninstallFirst)
            {
                var removed = await s.Adb.UninstallTabletAsync(serial, ct);
                if (!removed.Ok) { _job.Fail(removed.Message, removed.Outcome.ToString()); return; }
            }
            var result = await s.Adb.InstallAsync(serial, apk, ct);
            if (result.Ok) _job.Succeed(result.Message); else _job.Fail(result.Message, result.Outcome.ToString());
        }
        catch (GitHubException ex) { _job.Fail(ex.Message, "Download"); }
        catch (ChecksumException ex) { _job.Fail(ex.Message, "Checksum"); }
        catch (InvalidOperationException ex) { _job.Fail(ex.Message, "Other"); }
        catch (Exception ex)
        {
            _log.LogError(ex, "A instalação do app no tablet falhou.");
            _job.Fail("A instalação falhou. Veja o log do agente.", "Other");
        }
    }

    // ================================================================ código de pareamento

    private Task<JsonNode?> CodeGet()
    {
        var chosen = ChosenAdapter(out _);
        var port = s.Runtime.Port;
        return Task.FromResult<JsonNode?>(new JsonObject
        {
            ["code"] = s.Runtime.TokenCreated ? s.Runtime.Token : null,   // o código só sai nesta tela (e na bandeja); token fixo nunca
            ["fixed"] = !s.Runtime.TokenCreated,
            ["usbAddress"] = $"127.0.0.1:{port}",
            ["wifiAddress"] = chosen is null || !s.Runtime.ListenOnLan ? null : $"{chosen.Ip}:{port}",
            ["clients"] = new JsonArray([.. s.Runtime.Clients.Select(WindowCommands.ClientJson)]),
        });
    }

    private Task<JsonNode?> CodeCopy()
    {
        if (!s.Runtime.TokenCreated) throw new CommandException("fixed_token", "O token fixo (ATALAIA_TOKEN) não é copiado pela janela.");
        s.CopyText(s.Runtime.Token);
        return Task.FromResult<JsonNode?>(null);
    }

    // ================================================================ fontes de dados

    private Task<JsonNode?> SourcesGet() => Task.FromResult<JsonNode?>(SourcesJson());

    private JsonObject SourcesJson()
    {
        var status = s.Runtime.Scheduler.Plugins.ToDictionary(p => p.Id, p => p.Status);
        var list = new JsonArray();
        foreach (var info in SourceCatalog.All)
        {
            var st = status.TryGetValue(info.Id, out var v) ? v : PluginStatus.Disabled;
            var needs = SourceCatalog.Missing(info.Id, s.Probe(info.Id));
            var entry = new JsonObject
            {
                ["id"] = info.Id, ["name"] = info.Name, ["description"] = info.Description,
                ["enabled"] = st != PluginStatus.Disabled,
                ["status"] = st.ToString().ToLowerInvariant(),
                ["needs"] = new JsonArray([.. needs.Select(n => (JsonNode)JsonValue.Create(n)!)]),
            };
            var fields = SourceCredentials.For(info.Id);
            if (fields.Count > 0) entry["fields"] = new JsonArray([.. fields.Select(f => FieldJson(info.Id, f))]);
            list.Add(entry);
        }
        return new JsonObject { ["simulate"] = s.Runtime.Simulate, ["sources"] = list };
    }

    /// <summary>
    /// Um campo do formulário. Segredo: só "configurada" (<c>configured</c>), <b>nunca</b> o valor. Não segredo: o valor atual, para preencher o campo.
    /// <c>fromEnv</c> = uma variável de ambiente <c>ATALAIA_*</c> define o campo e manda sobre o que a janela grava.
    /// </summary>
    private JsonNode FieldJson(string sourceId, CredentialField f)
    {
        var current = s.Runtime.Config[f.Key(sourceId)];
        var o = new JsonObject
        {
            ["name"] = f.Name, ["label"] = f.Label, ["secret"] = f.Secret,
            ["configured"] = !string.IsNullOrWhiteSpace(current),
            ["fromEnv"] = !string.IsNullOrWhiteSpace(s.Probe(sourceId).Env(f.EnvName(sourceId))),
            ["envName"] = f.EnvName(sourceId),
        };
        if (!f.Secret) o["value"] = current ?? "";
        return o;
    }

    /// <summary>Os valores do formulário (<c>values</c>: nome do campo → texto). Qualquer outra forma é recusada.</summary>
    private static Dictionary<string, string?> FormValues(JsonElement a)
    {
        if (!a.TryGetProperty("values", out var v) || v.ValueKind != JsonValueKind.Object)
            throw new CommandException("invalid_args", "Faltam os valores do formulário.");
        var map = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var p in v.EnumerateObject())
            map[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString()
                : p.Value.ValueKind == JsonValueKind.Null ? null : throw new CommandException("invalid_args", $"O valor de '{p.Name}' precisa ser texto.");
        return map;
    }

    private async Task<JsonNode?> SourcesConfigure(string id, Dictionary<string, string?> submitted)
    {
        var fields = SourceCredentials.For(id);
        if (fields.Count == 0) throw new CommandException("invalid_args", "Esta fonte não tem o que configurar pela janela.");
        if (submitted.Keys.Any(k => fields.All(f => f.Name != k))) throw new CommandException("invalid_args", "Campo desconhecido.");

        if (!SourceCredentials.TryValidateForm(id, submitted, f => !string.IsNullOrWhiteSpace(s.Runtime.Config[f.Key(id)]) || s.Vault.Has(f.Key(id)),
                out var changes, out var errors))
            throw new CommandException("invalid_fields", string.Join(" ", errors));

        // Variável de ambiente manda: se define um dos campos, a janela não consegue mudá-lo (e diz qual variável remover).
        var pinned = changes.Keys.Where(f => !string.IsNullOrWhiteSpace(s.Probe(id).Env(f.EnvName(id)))).ToList();
        if (pinned.Count > 0)
            throw new CommandException("overridden", $"Definido por variável de ambiente ({string.Join(", ", pinned.Select(f => f.EnvName(id)))}): remova-a para mudar por aqui.");

        await ApplyAsync(
            root => { foreach (var (f, v) in changes.Where(c => !c.Key.Secret)) SettingsStore.SetPath(root, f.Key(id), v); },
            () => changes.All(c => s.Runtime.Config[c.Key.Key(id)] == c.Value),
            "Uma variável de ambiente está mandando sobre este valor: remova-a para mudar por aqui.",
            vault => { foreach (var (f, v) in changes.Where(c => c.Key.Secret)) vault[f.Key(id)] = v; });
        return SourcesJson();
    }

    /// <summary>Remove tudo o que a janela guardou da fonte (campos comuns no <c>settings.json</c>, segredos no cofre). Ela fica sem configuração.</summary>
    private async Task<JsonNode?> SourcesClearConfig(string id)
    {
        var fields = SourceCredentials.For(id);
        if (fields.Count == 0) throw new CommandException("invalid_args", "Esta fonte não tem o que configurar pela janela.");
        await ApplyAsync(
            root => { foreach (var f in fields.Where(f => !f.Secret)) SettingsStore.SetPath(root, f.Key(id), null); },
            () => fields.All(f => string.IsNullOrWhiteSpace(s.Runtime.Config[f.Key(id)])),
            "Uma variável de ambiente ainda define esta fonte: remova-a para limpar por aqui.",
            vault => { foreach (var f in fields.Where(f => f.Secret)) vault[f.Key(id)] = null; });
        return SourcesJson();
    }

    private async Task<JsonNode?> SourcesSet(string id, bool enabled)
    {
        if (SourceCatalog.All.All(i => i.Id != id)) throw new CommandException("invalid_args", "Fonte desconhecida.");
        await ApplyAsync(root => SettingsStore.SetPath(root, $"Plugins:{id}:enabled", enabled),
            () => SourceChangeTookEffect(s.Runtime.Simulate, s.Runtime.Scheduler.Plugins.FirstOrDefault(p => p.Id == id)?.Status, enabled),
            $"Esta fonte está fixada por uma variável de ambiente (ATALAIA_Plugins__{id}__enabled): remova-a para mudar por aqui.");
        return SourcesJson();
    }

    /// <summary>
    /// A mudança de ligar/desligar uma fonte valeu? No modo simulado todas ficam ligadas, seja qual for a configuração, então não há o que conferir.
    /// Fora dele, o estado do plugin tem que ter virado o pedido; se não virou, alguma variável de ambiente está mandando.
    /// </summary>
    internal static bool SourceChangeTookEffect(bool simulate, PluginStatus? status, bool wanted) =>
        simulate || (status is { } s && s != PluginStatus.Disabled) == wanted;

    // ================================================================ resumo

    private async Task<JsonNode?> Summary()
    {
        var port = s.Runtime.Port;
        var chosen = ChosenAdapter(out _);
        var fw = await ReadFirewallAsync();
        var adb = await s.Adb.GetStatusAsync(port, CancellationToken.None);
        var device = adb.Devices.FirstOrDefault(d => d.Device.State == AdbDeviceState.Authorized);
        var installed = device is null ? null : await s.Adb.InstalledVersionAsync(device.Device.Serial, CancellationToken.None);
        var sources = SourcesJson();
        var enabled = sources["sources"]!.AsArray().Count(x => x!["enabled"]!.GetValue<bool>());
        var needSetup = sources["sources"]!.AsArray().Count(x => x!["enabled"]!.GetValue<bool>() && x["needs"]!.AsArray().Count > 0);
        var clients = s.Runtime.Clients;

        JsonNode Item(string key, string status, string text) => new JsonObject { ["key"] = key, ["status"] = status, ["text"] = text };

        var fwState = fw is null ? (FirewallState?)null : FirewallEvaluator.Evaluate(fw.Rules, port, fw.CurrentProfiles, Environment.ProcessPath);
        var network = !s.Runtime.ListenOnLan ? Item("network", "off", "Wi-Fi desligado: só o USB funciona")
            : fwState == FirewallState.Open ? Item("network", "ok", $"Wi-Fi liberado · {(chosen is null ? "sem endereço de rede local" : $"{chosen.Ip}:{port}")}")
            : Item("network", "warn", fwState is null ? "Wi-Fi ligado, mas não consegui ler o Firewall" : "Wi-Fi ligado, mas o Firewall não libera a porta");
        var usb = !adb.Found ? Item("usb", "warn", "adb não encontrado")
            : device is null ? Item("usb", "off", adb.Devices.Any(d => d.Device.State == AdbDeviceState.Unauthorized) ? "Tablet plugado, mas ainda não autorizou a depuração USB" : "Nenhum tablet plugado")
            : device.Tunnel == true ? Item("usb", "ok", "Túnel adb reverse ativo")
            : Item("usb", "warn", "Tablet autorizado, mas o túnel USB não está ativo (ligue \"Manter o túnel USB\")");
        var app = device is null ? Item("app", "off", "Sem tablet pelo cabo para conferir")
            : installed is null ? Item("app", "off", "O app do Atalaia não está instalado no tablet")
            : installed == AppInfo.Version ? Item("app", "ok", $"Atalaia {installed} instalado")
            : Item("app", "warn", $"Instalado no tablet: {installed} (o agente é {AppInfo.Version})");
        var tablet = clients.Count == 0 ? Item("tablet", "off", "Nenhum tablet conectado agora")
            : Item("tablet", "ok", $"{clients[0].Client} · conectado por {(WindowCommands.ChannelOf(clients[0].Remote) == "usb" ? "USB" : "Wi-Fi")}");
        var src = Item("sources", needSetup > 0 ? "warn" : "ok", $"{enabled} de {SourceCatalog.All.Count} ligadas{(needSetup > 0 ? $" · {needSetup} pedem ajuste" : "")}");

        return new JsonObject
        {
            ["items"] = new JsonArray(network, usb, app, tablet, src),
            ["startup"] = s.Autostart.State.ToString().ToLowerInvariant(),   // off | on | otherpath
            ["autoCheck"] = AutoCheck(),
        };
    }

    private Task<JsonNode?> StartupSet(bool enabled)
    {
        try { if (enabled) s.Autostart.Enable(); else s.Autostart.Disable(); }
        catch (InvalidOperationException ex) { throw new CommandException("startup_failed", ex.Message); }
        return Summary();
    }

    private Task<JsonNode?> UpdatesSetAuto(bool enabled)
    {
        s.Settings.Set("Updates:AutoCheck", enabled);   // lido pela atualização automática (Fase 4); não precisa religar o agente
        return Summary();
    }

    private bool AutoCheck() => s.Settings.Read()["Updates"]?["AutoCheck"] is JsonValue v && v.TryGetValue<bool>(out var b) ? b : true;

    // ================================================================ utilidades

    /// <summary>
    /// Grava a mudança no <c>settings.json</c>, religa o agente e confere que ela valeu (<paramref name="effective"/>). Se religar falha, ou se
    /// uma variável de ambiente manteve o valor antigo, <b>volta o arquivo</b> ao que era: o <c>settings.json</c> nunca fica com algo que não funciona.
    /// </summary>
    private async Task ApplyAsync(Action<JsonObject> change, Func<bool> effective, string overriddenMessage, Action<Dictionary<string, string?>>? secretChange = null)
    {
        var before = s.Settings.Read();
        var secretsBefore = s.Vault.Read();
        s.Settings.Update(change);
        if (secretChange is not null) s.Vault.Update(secretChange);
        try { await s.ApplyConfig(); }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Não foi possível aplicar a configuração; voltando ao que era.");   // o texto da exceção não carrega valores de configuração
            s.Settings.Replace(before);
            if (secretChange is not null) s.Vault.Replace(secretsBefore);
            throw new CommandException("apply_failed", "O agente não conseguiu religar com essa mudança, então ela foi desfeita. A configuração anterior continua valendo.");
        }
        if (!effective())
        {
            s.Settings.Replace(before);
            if (secretChange is not null) s.Vault.Replace(secretsBefore);
            await TryReapplyAsync();
            throw new CommandException("overridden", overriddenMessage);
        }
    }

    private async Task TryReapplyAsync()
    {
        try { await s.ApplyConfig(); } catch (Exception ex) { _log.LogError(ex, "Não consegui religar o agente depois de desfazer a mudança."); }
    }

    private string Repo() => s.Runtime.Config["Updates:Repository"] is { } r && LinkPolicy.IsValidRepository(r) ? r : LinkPolicy.DefaultRepository;

    private static bool Bool(JsonElement a, string name) =>
        a.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean()
            : throw new CommandException("invalid_args", $"Falta o valor '{name}' (verdadeiro ou falso).");

    private static string? OptionalText(JsonElement a, string name) =>
        a.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static JsonNode Check(string key, bool ok, string text) => new JsonObject { ["key"] = key, ["ok"] = ok, ["text"] = text };
}

/// <summary>A instalação em andamento (baixar e instalar). Roda em segundo plano; a janela consulta o estado de tempos em tempos.</summary>
internal sealed class InstallJob
{
    private readonly object _lock = new();
    private string _state = "idle";       // idle | running | done | error
    private string _phase = "";           // download | install
    private double _progress;
    private string? _message;
    private string? _outcome;

    public bool Running { get { lock (_lock) return _state == "running"; } }

    public void Start(Func<CancellationToken, Task> work)
    {
        lock (_lock) { _state = "running"; _phase = ""; _progress = 0; _message = null; _outcome = null; }
        _ = Task.Run(() => work(CancellationToken.None));
    }

    public void Phase(string phase) { lock (_lock) { _phase = phase; _progress = 0; } }
    public void Progress(double value) { lock (_lock) _progress = value; }
    public void Succeed(string message) { lock (_lock) { _state = "done"; _message = message; _outcome = "Success"; } }
    public void Fail(string message, string outcome) { lock (_lock) { _state = "error"; _message = message; _outcome = outcome; } }

    public JsonNode ToJson()
    {
        lock (_lock)
            return new JsonObject { ["state"] = _state, ["phase"] = _phase, ["progress"] = _progress, ["message"] = _message, ["outcome"] = _outcome };
    }
}
