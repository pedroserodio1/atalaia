// PcProbe — console DESCARTÁVEL: cada sondagem imprime o dado bruto e um VEREDITO.
// Sem arquitetura, sem rede para o tablet, sem UI.
using System.Diagnostics;
using System.IO.Pipes;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LibreHardwareMonitor.Hardware;
using Microsoft.Data.Sqlite;
using NAudio.CoreAudioApi;
using Windows.Media.Control;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

Console.OutputEncoding = Encoding.UTF8;
var envPath = FindEnv();
// espelha tudo em resultado.txt (UTF-8 com BOM: abre com acento certo no Bloco de Notas)
var outFile = Path.Combine(envPath != null ? Path.GetDirectoryName(envPath) : Directory.GetCurrentDirectory(), "resultado.txt");
Console.SetOut(new Tee(Console.Out, new StreamWriter(outFile, false, new UTF8Encoding(true)) { AutoFlush = true }));
LoadEnv();

var results = new List<(string name, string verdict)>();

await Run("1. Métricas básicas do Windows", () => Task.Run(Metrics), 30);
await Run("2. Sensores de hardware (LibreHardwareMonitorLib)", () => Task.Run(Sensors), 60);
await Run("3. GPU NVIDIA (nvidia-smi)", Nvidia, 20);
await Run("4. Mídia tocando (SMTC)", Smtc, 20);
await Run("5. Nobreak Intelbras (SQLite somente leitura)", () => Task.Run(Ups), 30);
await Run("6. Volume / dispositivos de áudio (NAudio)", () => Task.Run(Audio), 20);
await Run("7. Processos e janela ativa", () => Task.Run(Procs), 20);
await Run("8. Notificações do Windows", Notifs, 30);
await Run("9. Steam", Steam, 20);
await Run("10. Clima (Open-Meteo)", Weather, 20);
await Run("11. Discord (RPC local)", Discord, 150);
await Run("12. Claude (uso local via transcripts)", () => Task.Run(ClaudeUse), 120);

Console.WriteLine("\n\n================ RESUMO ================");
foreach (var (name, verdict) in results)
    Console.WriteLine($"{name,-55} {verdict}");

// ---------------------------------------------------------------- infra

async Task Run(string name, Func<Task<string>> f, int timeoutSec)
{
    Console.WriteLine($"\n=== {name} ===");
    string v;
    try
    {
        var t = f();
        v = await Task.WhenAny(t, Task.Delay(timeoutSec * 1000)) == t ? await t : $"FALHA: timeout {timeoutSec}s";
    }
    catch (Exception e) { v = $"FALHA: {e.GetType().Name}: {e.Message}"; }
    v = Redact(v);
    Console.WriteLine("VEREDITO: " + v);
    results.Add((name, v));
}

string Env(string k) => Environment.GetEnvironmentVariable(k)?.Trim() ?? "";

string FindEnv()
{
    // procura .env subindo a partir do diretório atual e do binário
    foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        for (var d = new DirectoryInfo(start); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, ".env"))) return Path.Combine(d.FullName, ".env");
    return null;
}

void LoadEnv()
{
    if (envPath == null) { Console.WriteLine("(.env não encontrado — usando só variáveis de ambiente)"); return; }
    foreach (var raw in File.ReadAllLines(envPath))
    {
        var line = raw.Trim();
        if (line.Length == 0 || line.StartsWith('#')) continue;
        var i = line.IndexOf('=');
        if (i <= 0) continue;
        var k = line[..i].Trim(); var val = line[(i + 1)..].Trim().Trim('"');
        if (Env(k) == "" && val != "") Environment.SetEnvironmentVariable(k, val); // ambiente do sistema tem prioridade
    }
    Console.WriteLine("(.env carregado: " + envPath + ")");
}

void SaveEnv(string key, string value)
{
    if (envPath == null) { Console.WriteLine($"(sem .env; guarde manualmente {key})"); return; }
    var lines = File.ReadAllLines(envPath).ToList();
    var idx = lines.FindIndex(l => l.StartsWith(key + "="));
    if (idx >= 0) lines[idx] = key + "=" + value; else lines.Add(key + "=" + value);
    File.WriteAllLines(envPath, lines);
    Console.WriteLine($"({key} salvo no .env)");
}

string Redact(string s)
{
    foreach (var k in new[] { "STEAM_API_KEY", "DISCORD_CLIENT_SECRET", "DISCORD_REFRESH_TOKEN" })
        if (Env(k).Length > 4) s = s.Replace(Env(k), "***");
    return s;
}

// ---------------------------------------------------------------- 1. métricas

string Metrics()
{
    var problems = new List<string>();
    var cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total");
    var avail = new PerformanceCounter("Memory", "Available MBytes");
    PerformanceCounter disk = null;
    try { disk = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total"); disk.NextValue(); }
    catch (Exception e) { problems.Add("disco: " + e.Message); }

    var nic = PickNic();
    cpu.NextValue();
    long r0 = nic?.GetIPv4Statistics().BytesReceived ?? 0, s0 = nic?.GetIPv4Statistics().BytesSent ?? 0;
    Thread.Sleep(1000);
    long r1 = nic?.GetIPv4Statistics().BytesReceived ?? 0, s1 = nic?.GetIPv4Statistics().BytesSent ?? 0;

    double totalMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1048576.0;
    Console.WriteLine($"  CPU total: {cpu.NextValue():F1}%");
    Console.WriteLine($"  RAM: livre {avail.NextValue():F0} MB de {totalMb:F0} MB");
    if (disk != null) Console.WriteLine($"  Disco (atividade): {disk.NextValue():F1}%");
    foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady))
        Console.WriteLine($"  Volume {d.Name}: {d.AvailableFreeSpace / 1073741824.0:F1} GB livres de {d.TotalSize / 1073741824.0:F1} GB");
    if (nic != null) Console.WriteLine($"  Rede [{nic.Name}]: ↓ {(r1 - r0) / 1024.0:F1} KB/s  ↑ {(s1 - s0) / 1024.0:F1} KB/s");
    else problems.Add("nenhuma placa de rede ativa encontrada");
    return problems.Count == 0 ? "OK" : "PARCIAL: " + string.Join("; ", problems);
}

NetworkInterface PickNic()
{
    var want = Env("PCPROBE_NET_IFACE");
    var all = NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up
        && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel).ToList();
    return (want != "" ? all.FirstOrDefault(n => n.Name == want) : null) ?? all.FirstOrDefault();
}

// ---------------------------------------------------------------- 2. sensores

string Sensors()
{
    bool admin = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
    Console.WriteLine("  rodando como administrador: " + admin);
    var pc = new Computer
    {
        IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true,
        IsMotherboardEnabled = true, IsStorageEnabled = true, IsNetworkEnabled = false
    };
    pc.Open();
    int total = 0, cpuTemps = 0, gpuTemps = 0, fans = 0;
    void Walk(IHardware h)
    {
        h.Update();
        foreach (var s in h.Sensors)
        {
            total++;
            if (s.Value == null) continue;
            if (s.SensorType == SensorType.Temperature)
            {
                if (h.HardwareType == HardwareType.Cpu && s.Value > 0) cpuTemps++; // 0,0 = leitura bloqueada sem admin
                if (h.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel) gpuTemps++;
            }
            if (s.SensorType == SensorType.Fan) fans++;
            if (s.SensorType is SensorType.Temperature or SensorType.Fan or SensorType.Power or SensorType.Load)
                Console.WriteLine($"  [{h.HardwareType}] {h.Name} | {s.SensorType} | {s.Name} = {s.Value:F1}");
        }
        foreach (var sh in h.SubHardware) Walk(sh);
    }
    foreach (var h in pc.Hardware) Walk(h);
    pc.Close();
    Console.WriteLine($"  total de sensores: {total} | temp CPU: {cpuTemps} | temp GPU: {gpuTemps} | fans: {fans}");
    if (cpuTemps > 0) return "OK";
    return admin ? "PARCIAL: sem temperatura de CPU mesmo como admin (CPU/placa não suportada?)"
                 : "PARCIAL: sem admin — rode o terminal como administrador para temperatura de CPU";
}

// ---------------------------------------------------------------- 3. nvidia-smi

async Task<string> Nvidia()
{
    var exe = Env("PCPROBE_NVIDIA_SMI"); if (exe == "") exe = "nvidia-smi";
    var psi = new ProcessStartInfo(exe, "--query-gpu=name,temperature.gpu,utilization.gpu,memory.used,memory.total,power.draw --format=csv")
    { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
    try
    {
        using var p = Process.Start(psi);
        var o = await p.StandardOutput.ReadToEndAsync(); var e = await p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync();
        Console.WriteLine(o.TrimEnd()); if (e.Length > 0) Console.WriteLine("stderr: " + e.TrimEnd());
        return p.ExitCode == 0 ? "OK" : "FALHA: exit code " + p.ExitCode;
    }
    catch (System.ComponentModel.Win32Exception) { return "FALHA: nvidia-smi não encontrado (sem GPU NVIDIA? ou defina PCPROBE_NVIDIA_SMI)"; }
}

// ---------------------------------------------------------------- 4. SMTC

async Task<string> Smtc()
{
    var mgr = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
    var sessions = mgr.GetSessions();
    Console.WriteLine("  sessões: " + sessions.Count);
    foreach (var s in sessions)
    {
        var p = await s.TryGetMediaPropertiesAsync();
        var pb = s.GetPlaybackInfo(); var tl = s.GetTimelineProperties();
        Console.WriteLine($"  [{s.SourceAppUserModelId}] {p.Artist} — {p.Title} (álbum: {p.AlbumTitle})");
        Console.WriteLine($"     status: {pb.PlaybackStatus} | posição: {tl.Position:mm\\:ss} / {tl.EndTime:mm\\:ss} | capa: {(p.Thumbnail != null ? "sim" : "não")}");
    }
    return sessions.Count > 0 ? "OK" : "PARCIAL: API funciona mas nenhuma sessão de mídia ativa (toque algo e rode de novo)";
}

// ---------------------------------------------------------------- 12. Claude (uso local)

string ClaudeUse()
{
    var sw = Stopwatch.StartNew();
    var cfg = Env("CLAUDE_CONFIG_DIR");
    var root = Path.Combine(cfg != "" ? cfg : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude"), "projects");
    if (!Directory.Exists(root)) return "FALHA: pasta não encontrada: " + root;
    var now = DateTime.UtcNow; var cutoff = now.AddDays(-7);
    // chave = message.id|requestId; a mesma resposta aparece várias vezes no .jsonl, vale a última
    var msgs = new Dictionary<string, (DateTime ts, string model, long inp, long outp, long cw, long cr)>();
    int files = 0, bad = 0;
    long L(JsonElement u, string n) => u.TryGetProperty(n, out var x) && x.ValueKind == JsonValueKind.Number ? x.GetInt64() : 0;
    foreach (var f in Directory.EnumerateFiles(root, "*.jsonl", SearchOption.AllDirectories).Where(f => File.GetLastWriteTimeUtc(f) >= cutoff))
    {
        files++;
        try
        {
            using var sr = new StreamReader(new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                if (!line.Contains("\"usage\"")) continue;
                try
                {
                    using var d = JsonDocument.Parse(line); var r = d.RootElement;
                    if (!r.TryGetProperty("message", out var m) || !m.TryGetProperty("usage", out var u) || !m.TryGetProperty("id", out var idEl)) continue;
                    if (!r.TryGetProperty("timestamp", out var tsEl) || !DateTimeOffset.TryParse(tsEl.GetString(), out var ts)) continue;
                    var model = m.TryGetProperty("model", out var mo) ? mo.GetString() : "?";
                    if (model == "<synthetic>") continue;
                    var key = idEl.GetString() + "|" + (r.TryGetProperty("requestId", out var rq) ? rq.GetString() : "");
                    msgs[key] = (ts.UtcDateTime, model, L(u, "input_tokens"), L(u, "output_tokens"), L(u, "cache_creation_input_tokens"), L(u, "cache_read_input_tokens"));
                }
                catch { bad++; }
            }
        }
        catch { bad++; } // arquivo em uso/ilegível: segue
    }
    foreach (var k in msgs.Where(kv => kv.Value.ts < cutoff).Select(kv => kv.Key).ToList()) msgs.Remove(k); // arquivo recente pode ter respostas antigas
    Console.WriteLine($"  pasta: {root} | arquivos tocados nos últimos 7 dias: {files} | respostas únicas (7 dias): {msgs.Count} | linhas ilegíveis: {bad}");
    if (msgs.Count == 0) return "FALHA: nenhum registro de uso nos últimos 7 dias";

    void Win(string label, TimeSpan span)
    {
        var s = msgs.Values.Where(v => now - v.ts <= span).ToList();
        Console.WriteLine($"  {label,-8} respostas={s.Count,6} | entrada={s.Sum(v => v.inp),11:N0} | saída={s.Sum(v => v.outp),10:N0} | cache escrita={s.Sum(v => v.cw),12:N0} | cache leitura={s.Sum(v => v.cr),13:N0}");
    }
    Win("últimas 5h", TimeSpan.FromHours(5)); Win("24h", TimeSpan.FromHours(24)); Win("7 dias", TimeSpan.FromDays(7));
    Console.WriteLine("  por modelo (7 dias, tokens de saída):");
    foreach (var g in msgs.Values.GroupBy(v => v.model).OrderByDescending(g => g.Sum(v => v.outp)))
        Console.WriteLine($"    {g.Key,-28} respostas={g.Count(),6} saída={g.Sum(v => v.outp),10:N0}");
    Console.WriteLine($"  resposta mais recente: {msgs.Values.Max(v => v.ts).ToLocalTime():yyyy-MM-dd HH:mm:ss} | varredura: {sw.ElapsedMilliseconds} ms");

    // --- limites do plano: cache que o próprio Claude Code grava em ~/.claude.json (arquivo interno, não documentado)
    // --- FRESCO: uma chamada mínima `claude -p` (haiku, "ok") faz o Claude Code emitir `rate_limit_event` com os limites do plano.
    // Custa uma requisição minúscula da sua cota. Desligue com PCPROBE_CLAUDE_PING=0 (aí cai no cache abaixo).
    bool fresh = false; string planNote = null;
    if (Env("PCPROBE_CLAUDE_PING") != "0")
        try
        {
            var psi = new ProcessStartInfo(Env("PCPROBE_CLAUDE_EXE") != "" ? Env("PCPROBE_CLAUDE_EXE") : "claude")
            { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetTempPath() };
            foreach (var a in new[] { "-p", "ok", "--model", "haiku", "--output-format", "stream-json", "--verbose", "--no-session-persistence", "--max-turns", "1" }) psi.ArgumentList.Add(a);
            var sw2 = Stopwatch.StartNew();
            using var cp = Process.Start(psi);
            var outTask = cp.StandardOutput.ReadToEndAsync(); _ = cp.StandardError.ReadToEndAsync();
            if (!outTask.Wait(70000)) { try { cp.Kill(true); } catch { } throw new TimeoutException("claude -p não respondeu em 70 s"); }
            JsonNode ev = null;
            foreach (var l in outTask.Result.Split('\n')) if (l.Contains("\"rate_limit_event\"")) { try { ev = JsonNode.Parse(l)?["rate_limit_info"]; } catch { } }
            if (ev == null) planNote = "claude -p respondeu sem rate_limit_event (conta sem plano claude.ai? ou versão sem o evento)";
            else
            {
                Console.WriteLine($"  LIMITES DO PLANO (AO VIVO via `claude -p`, {sw2.ElapsedMilliseconds} ms) — status: {ev["status"]} | janela mais restrita: {ev["rateLimitType"]} | uso extra: {ev["overageStatus"]} (em uso: {ev["isUsingOverage"]})");
                if (ev["unifiedWindows"] is JsonObject uw)
                    foreach (var kv in uw)
                    {
                        double? ut = (double?)kv.Value?["utilization"]; long? rs = (long?)kv.Value?["resetsAt"];
                        Console.WriteLine($"    {kv.Key}: {(ut == null ? "?" : (ut * 100).Value.ToString("F0"))}% (valor bruto {ut}) | reseta em: {(rs == null ? "?" : DateTimeOffset.FromUnixTimeSeconds(rs.Value).ToLocalTime().ToString("yyyy-MM-dd HH:mm"))}");
                    }
                fresh = true; planNote = "limites do plano AO VIVO";
            }
        }
        catch (Exception e) { planNote = "ping `claude -p` falhou: " + e.Message; }
    string S(JsonElement e, string n) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(n, out var v) ? v.ToString() : "?";
    if (!fresh) try
    {
        var cfgFile = Path.Combine(cfg != "" ? cfg : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");
        using var fs = new FileStream(cfgFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var doc = JsonDocument.Parse(fs);
        if (!doc.RootElement.TryGetProperty("cachedUsageUtilization", out var cu)) planNote = "bloco cachedUsageUtilization ausente em .claude.json";
        else
        {
            var fetched = DateTimeOffset.FromUnixTimeMilliseconds(cu.GetProperty("fetchedAtMs").GetInt64());
            var age = DateTimeOffset.UtcNow - fetched;
            fresh = age <= TimeSpan.FromMinutes(15);
            var u = cu.GetProperty("utilization");
            Console.WriteLine($"  LIMITES DO PLANO (cache de {cfgFile}) — buscado em {fetched.ToLocalTime():yyyy-MM-dd HH:mm} ({(age.TotalHours < 48 ? age.TotalHours.ToString("F1") + " h" : age.TotalDays.ToString("F1") + " dias")} atrás){(fresh ? "" : "  >>> DADO VELHO")}");
            foreach (var w in new[] { "five_hour", "seven_day" })
                if (u.TryGetProperty(w, out var x)) Console.WriteLine($"    {w}: {S(x, "utilization")}% | reseta em: {S(x, "resets_at")}");
            if (u.TryGetProperty("limits", out var lim) && lim.ValueKind == JsonValueKind.Array)
                foreach (var l in lim.EnumerateArray()) Console.WriteLine($"    limite {S(l, "kind")}: {S(l, "percent")}% | severidade {S(l, "severity")} | ativo {S(l, "is_active")}");
            if (u.TryGetProperty("seven_day_breakdown", out var bd) && bd.TryGetProperty("rows", out var rows))
                Console.WriteLine("    semana por origem: " + string.Join(" | ", rows.EnumerateArray().Select(r => $"{S(r, "display_name")} {S(r, "percent")}%")));
            if (u.TryGetProperty("extra_usage", out var ex)) Console.WriteLine($"    uso extra (créditos) ativo: {S(ex, "is_enabled")}");
            planNote = fresh ? "limites do plano frescos" : "limites do plano velhos (cache só atualiza quando o Claude Code no terminal consulta o uso)";
        }
    }
    catch (Exception e) { planNote = "limites do plano indisponíveis: " + e.Message; }

    // --- fonte fresca opcional: JSON da status line gravado por .claude/statusline-capture.py (se um `claude` no terminal estiver rodando)
    var slFile = Path.Combine(envPath != null ? Path.GetDirectoryName(envPath) : Directory.GetCurrentDirectory(), ".claude", "statusline-capture.json");
    if (File.Exists(slFile))
        try
        {
            using var sd = JsonDocument.Parse(new FileStream(slFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
            var rl = sd.RootElement.TryGetProperty("rate_limits", out var r0) ? r0 : default;
            var age = DateTime.Now - File.GetLastWriteTime(slFile);
            Console.WriteLine($"  STATUS LINE (captura de {File.GetLastWriteTime(slFile):HH:mm:ss}, {age.TotalMinutes:F0} min atrás): 5h={S(rl.ValueKind == JsonValueKind.Object && rl.TryGetProperty("five_hour", out var f5) ? f5 : default, "used_percentage")}% 7d={S(rl.ValueKind == JsonValueKind.Object && rl.TryGetProperty("seven_day", out var s7) ? s7 : default, "used_percentage")}%");
            if (age < TimeSpan.FromMinutes(15)) fresh = true;
        }
        catch (Exception e) { Console.WriteLine("  status line: captura ilegível: " + e.Message); }
    else Console.WriteLine("  status line: sem captura (.claude/statusline-capture.json) — só existe se um `claude` de terminal estiver rodando neste projeto");

    Console.WriteLine("  LIMITAÇÃO: tokens vêm só do Claude Code/Desktop NESTE PC; o plano vem de `claude -p` ao vivo; se o ping falhar/desligar, cai no cache interno (pode ficar velho) ou na status line.");
    return (fresh ? "OK" : "PARCIAL") + ": tokens locais (entrada, saída, cache, modelo, janelas 5h/24h/7d); " + planNote;
}

// ---------------------------------------------------------------- 5. nobreak (UPS)

string Ups()
{
    var db = Env("PCPROBE_UPS_DB");
    if (db == "") db = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Intelbras", "ups_desktop", "intelbras_nobreak_management_service.db");
    if (!File.Exists(db)) return "FALHA: banco não encontrado: " + db;
    Console.WriteLine($"  banco: {db} | modificado: {File.GetLastWriteTime(db):yyyy-MM-dd HH:mm:ss}");

    string Dump(string path)
    {
        using var c = new SqliteConnection($"Data Source={path};Mode=ReadOnly");
        c.Open();
        List<Dictionary<string, object>> Q(string sql)
        {
            using var cmd = c.CreateCommand(); cmd.CommandText = sql;
            using var r = cmd.ExecuteReader();
            var rows = new List<Dictionary<string, object>>();
            while (r.Read())
            {
                var row = new Dictionary<string, object>();
                for (int i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                rows.Add(row);
            }
            return rows;
        }
        var tables = Q("SELECT name FROM sqlite_master WHERE type='table'").Select(t => (string)t["name"]).ToList();
        Console.WriteLine("  tabelas: " + string.Join(", ", tables));
        var problems = new List<string>(); var got = new List<string>();

        // status_history
        string[] want = { "input_voltage", "output_voltage", "power", "frequency", "battery_charge", "battery_voltage", "temperature", "status", "created_at" };
        if (!tables.Contains("status_history")) problems.Add("tabela status_history ausente");
        else
        {
            var cols = Q("PRAGMA table_info(status_history)").Select(x => (string)x["name"]).ToList();
            var missing = want.Where(w => !cols.Contains(w)).ToList();
            Console.WriteLine("  status_history colunas: " + string.Join(", ", cols));
            if (missing.Count > 0) problems.Add("colunas ausentes: " + string.Join(",", missing));
            var rows = Q("SELECT * FROM status_history ORDER BY rowid DESC LIMIT 5");
            Console.WriteLine($"  status_history: últimos {rows.Count} registros (mais novo primeiro)");
            foreach (var row in rows)
                Console.WriteLine("    " + string.Join(" | ", want.Where(row.ContainsKey).Select(k => $"{k}={row[k]}")));
            if (rows.Count == 0) problems.Add("status_history vazia");
            else
            {
                got.AddRange(want.Where(w => rows[0].ContainsKey(w) && rows[0][w] != null));
                if (rows[0].TryGetValue("created_at", out var ts) && ts != null)
                    Console.WriteLine($"  registro mais novo: {ts} (agora: {DateTime.Now:yyyy-MM-dd HH:mm:ss}) — se estiver velho, o serviço da Intelbras não está gravando");
            }
        }

        // events_and_failures
        if (!tables.Contains("events_and_failures")) problems.Add("tabela events_and_failures ausente");
        else
        {
            var ev = Q("SELECT * FROM events_and_failures ORDER BY rowid DESC LIMIT 10");
            Console.WriteLine($"  events_and_failures: últimos {ev.Count} eventos");
            foreach (var row in ev) Console.WriteLine("    " + string.Join(" | ", row.Select(kv => $"{kv.Key}={kv.Value}")));
            if (ev.Count == 0) Console.WriteLine("    (nenhum evento registrado)");
        }
        var tail = problems.Count == 0 ? "" : " | problemas: " + string.Join("; ", problems);
        return (problems.Count == 0 ? "OK" : "PARCIAL") + ": campos obtidos: " + string.Join(", ", got) + tail;
    }

    try { return Dump(db); }
    catch (Exception e)
    {
        Console.WriteLine("  leitura direta falhou (" + e.Message + "); usando cópia temporária");
        var tmp = Path.Combine(Path.GetTempPath(), "pcprobe-ups-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);
        try
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                if (File.Exists(db + suffix)) File.Copy(db + suffix, Path.Combine(tmp, Path.GetFileName(db) + suffix), true);
            return Dump(Path.Combine(tmp, Path.GetFileName(db)));
        }
        finally { SqliteConnection.ClearAllPools(); try { Directory.Delete(tmp, true); } catch { } }
    }
}

// ---------------------------------------------------------------- 6. áudio

string Audio()
{
    using var en = new MMDeviceEnumerator();
    var def = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
    Console.WriteLine($"  padrão: {def.FriendlyName} | volume {def.AudioEndpointVolume.MasterVolumeLevelScalar * 100:F0}% | mudo: {def.AudioEndpointVolume.Mute}");
    foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)) Console.WriteLine("  saída: " + d.FriendlyName);
    foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)) Console.WriteLine("  entrada: " + d.FriendlyName);
    return "OK";
}

// ---------------------------------------------------------------- 7. processos

string Procs()
{
    var all = Process.GetProcesses();
    int noPath = 0;
    foreach (var p in all) { try { _ = p.MainModule?.FileName; } catch { noPath++; } }
    Console.WriteLine($"  processos: {all.Length} | sem acesso ao caminho: {noPath}");
    foreach (var p in all.OrderByDescending(p => { try { return p.WorkingSet64; } catch { return 0; } }).Take(5))
        Console.WriteLine($"  {p.ProcessName} (pid {p.Id}) {p.WorkingSet64 / 1048576} MB");
    var h = Native.GetForegroundWindow();
    var sb = new StringBuilder(256); Native.GetWindowText(h, sb, 256);
    Native.GetWindowThreadProcessId(h, out var pid);
    string app = "?"; try { app = Process.GetProcessById((int)pid).ProcessName; } catch { }
    Console.WriteLine($"  janela ativa: [{app}] \"{sb}\"");
    return noPath == 0 ? "OK" : $"PARCIAL: {noPath} processos sem acesso ao caminho (precisa admin)";
}

// ---------------------------------------------------------------- 8. notificações

async Task<string> Notifs()
{
    var l = UserNotificationListener.Current;
    var st = await l.RequestAccessAsync();
    Console.WriteLine("  acesso: " + st);
    if (st != UserNotificationListenerAccessStatus.Allowed)
        return $"FALHA: acesso {st} (console app solto costuma exigir app empacotado MSIX + permissão em Configurações > Privacidade > Notificações)";
    var list = await l.GetNotificationsAsync(NotificationKinds.Toast);
    // só nome do app: conteúdo de notificação é sensível
    foreach (var n in list.Take(10)) Console.WriteLine($"  {n.AppInfo?.DisplayInfo?.DisplayName} (id {n.Id})");
    return $"OK: {list.Count} notificações atuais";
}

// ---------------------------------------------------------------- 9. Steam

async Task<string> Steam()
{
    var key = Env("STEAM_API_KEY"); var id = Env("STEAM_ID64");
    if (key == "" || id == "") return "PULADO: STEAM_API_KEY/STEAM_ID64 vazios";
    using var http = new HttpClient();
    var notes = new List<string>();
    async Task<JsonNode> Get(string path, string q) =>
        JsonNode.Parse(await http.GetStringAsync($"https://api.steampowered.com/{path}?key={key}&{q}"));
    string[] states = { "Offline", "Online", "Ocupado", "Ausente", "Soneca", "Quer trocar", "Quer jogar" };
    string State(JsonNode n) { int s = (int?)n?["personastate"] ?? 0; return s < states.Length ? states[s] : s.ToString(); }
    string Date(long? u) => u == null || u == 0 ? "?" : DateTimeOffset.FromUnixTimeSeconds(u.Value).ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    // --- perfil
    var p = (await Get("ISteamUser/GetPlayerSummaries/v2/", $"steamids={id}"))?["response"]?["players"]?[0];
    if (p == null) return "FALHA: resposta sem jogador (SteamID errado?)";
    bool pub = (int?)p["communityvisibilitystate"] == 3;
    Console.WriteLine($"  nome: {p["personaname"]} | nome real: {p["realname"]} | país: {p["loccountrycode"]}");
    Console.WriteLine($"  steamid: {p["steamid"]} | perfil: {p["profileurl"]} | público: {pub}");
    Console.WriteLine($"  estado: {State(p)} | último logoff: {Date((long?)p["lastlogoff"])} | conta criada: {Date((long?)p["timecreated"])}");
    Console.WriteLine($"  avatar: {p["avatarfull"]}");
    Console.WriteLine($"  jogando agora: {(p["gameextrainfo"] != null ? $"{p["gameextrainfo"]} (appid {p["gameid"]})" : "(nada)")}");
    if (!pub) notes.Add("perfil privado: biblioteca/amigos podem vir vazios");

    // --- nível
    try { Console.WriteLine("  nível Steam: " + (await Get("IPlayerService/GetSteamLevel/v1/", $"steamid={id}"))?["response"]?["player_level"]); }
    catch (Exception e) { notes.Add("nível: " + e.Message); }

    // --- biblioteca
    try
    {
        var games = (await Get("IPlayerService/GetOwnedGames/v1/", $"steamid={id}&include_appinfo=1&include_played_free_games=1"))?["response"]?["games"]?.AsArray();
        if (games == null) notes.Add("biblioteca oculta (Detalhes do jogo privados)");
        else
        {
            long totalMin = games.Sum(g => (long?)g["playtime_forever"] ?? 0);
            Console.WriteLine($"  biblioteca: {games.Count} jogos | {totalMin / 60} h jogadas no total");
            Console.WriteLine("  top 10 por horas:");
            foreach (var g in games.OrderByDescending(g => (long?)g["playtime_forever"] ?? 0).Take(10))
                Console.WriteLine($"    {g["name"],-40} {((long?)g["playtime_forever"] ?? 0) / 60.0,7:F1} h (2 sem: {((long?)g["playtime_2weeks"] ?? 0) / 60.0:F1} h)");
        }
    }
    catch (Exception e) { notes.Add("biblioteca: " + e.Message); }

    // --- jogados recentemente
    try
    {
        var rec = (await Get("IPlayerService/GetRecentlyPlayedGames/v1/", $"steamid={id}&count=5"))?["response"]?["games"]?.AsArray();
        Console.WriteLine("  jogados nas últimas 2 semanas: " + (rec == null || rec.Count == 0 ? "(nenhum / oculto)" : ""));
        if (rec != null) foreach (var g in rec) Console.WriteLine($"    {g["name"]} — {((long?)g["playtime_2weeks"] ?? 0) / 60.0:F1} h");
    }
    catch (Exception e) { notes.Add("recentes: " + e.Message); }

    // --- amigos
    try
    {
        var ids = (await Get("ISteamUser/GetFriendList/v1/", $"steamid={id}&relationship=friend"))?["friendslist"]?["friends"]?.AsArray()
            .Select(f => (string)f["steamid"]).ToList();
        if (ids == null) notes.Add("lista de amigos vazia/privada");
        else
        {
            var friends = new List<JsonNode>();
            for (int i = 0; i < ids.Count; i += 100)
            {
                var chunk = await Get("ISteamUser/GetPlayerSummaries/v2/", "steamids=" + string.Join(",", ids.Skip(i).Take(100)));
                friends.AddRange(chunk["response"]["players"].AsArray().Select(x => (JsonNode)x));
            }
            var online = friends.Where(f => (int?)f["personastate"] > 0).ToList();
            Console.WriteLine($"  amigos: {friends.Count} | online: {online.Count} | em jogo: {online.Count(f => f["gameextrainfo"] != null)}");
            foreach (var f in online.Take(15))
                Console.WriteLine($"    {f["personaname"],-25} {State(f),-10} {f["gameextrainfo"]}");
        }
    }
    catch (Exception e) { notes.Add("amigos: " + e.Message); }

    return notes.Count == 0 ? "OK" : "PARCIAL: " + string.Join("; ", notes);
}

// ---------------------------------------------------------------- 10. clima

async Task<string> Weather()
{
    var lat = Env("PCPROBE_LAT"); var lon = Env("PCPROBE_LON");
    if (lat == "" || lon == "") return "PULADO: PCPROBE_LAT/PCPROBE_LON vazios";
    using var http = new HttpClient();
    var json = await http.GetStringAsync($"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,relative_humidity_2m,wind_speed_10m,weather_code");
    Console.WriteLine("  " + json);
    return "OK";
}

// ---------------------------------------------------------------- 11. Discord

async Task<string> Discord()
{
    var cid = Env("DISCORD_CLIENT_ID"); var sec = Env("DISCORD_CLIENT_SECRET");
    if (cid == "" || sec == "") return "PULADO: DISCORD_CLIENT_ID/SECRET vazios";
    var redirect = Env("DISCORD_REDIRECT_URI"); if (redirect == "") redirect = "http://localhost";

    NamedPipeClientStream pipe = null;
    for (int i = 0; i < 10 && pipe == null; i++)
    {
        var p = new NamedPipeClientStream(".", $"discord-ipc-{i}", PipeDirection.InOut, PipeOptions.Asynchronous);
        try { await p.ConnectAsync(1000); pipe = p; Console.WriteLine($"  pipe discord-ipc-{i} conectado"); }
        catch { p.Dispose(); }
    }
    if (pipe == null) return "FALHA: pipe discord-ipc-* não encontrado (Discord DESKTOP aberto?)";

    using (pipe)
    {
        async Task Send(int op, object o)
        {
            var b = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o));
            var h = new byte[8];
            BitConverter.GetBytes(op).CopyTo(h, 0); BitConverter.GetBytes(b.Length).CopyTo(h, 4);
            await pipe.WriteAsync(h); await pipe.WriteAsync(b); await pipe.FlushAsync();
        }
        async Task<JsonNode> Recv(CancellationToken ct = default)
        {
            var h = new byte[8]; await pipe.ReadExactlyAsync(h, ct);
            var b = new byte[BitConverter.ToInt32(h, 4)]; await pipe.ReadExactlyAsync(b, ct);
            return JsonNode.Parse(b);
        }
        async Task<JsonNode> Cmd(string cmd, object args, string evt = null)
        {
            var nonce = Guid.NewGuid().ToString();
            var payload = new JsonObject { ["cmd"] = cmd, ["args"] = JsonSerializer.SerializeToNode(args), ["nonce"] = nonce };
            if (evt != null) payload["evt"] = evt;
            await Send(1, payload);
            while (true) { var m = await Recv(); if ((string)m["nonce"] == nonce) return m; }
        }
        string Short(JsonNode n) { var s = n?.ToJsonString() ?? "null"; return s.Length > 200 ? s[..200] + "…" : s; }
        using var http = new HttpClient();
        async Task<JsonNode> Token(Dictionary<string, string> f)
        {
            var r = await http.PostAsync("https://discord.com/api/oauth2/token", new FormUrlEncodedContent(f));
            var t = await r.Content.ReadAsStringAsync();
            if (!r.IsSuccessStatusCode) throw new Exception($"token HTTP {(int)r.StatusCode}: {t}");
            return JsonNode.Parse(t);
        }

        await Send(0, new { v = 1, client_id = cid });
        var ready = await Recv();
        Console.WriteLine("  handshake: " + ready["evt"] + " | versão do cliente: " + ready["data"]?["v"]);

        string access = null; var refresh = Env("DISCORD_REFRESH_TOKEN");
        if (refresh != "")
        {
            try
            {
                var t = await Token(new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh, ["client_id"] = cid, ["client_secret"] = sec });
                access = (string)t["access_token"]; SaveEnv("DISCORD_REFRESH_TOKEN", (string)t["refresh_token"]);
            }
            catch (Exception e) { Console.WriteLine("  refresh falhou, reautorizando: " + e.Message); }
        }
        if (access == null)
        {
            Console.WriteLine("  >>> Aceite o pop-up de autorização no Discord (até ~2 min)...");
            var a = await Cmd("AUTHORIZE", new { client_id = cid, scopes = new[] { "rpc", "rpc.voice.read", "identify" } });
            if ((string)a["evt"] == "ERROR") return "FALHA: AUTHORIZE recusado: " + a["data"];
            var t = await Token(new() { ["grant_type"] = "authorization_code", ["code"] = (string)a["data"]["code"], ["redirect_uri"] = redirect, ["client_id"] = cid, ["client_secret"] = sec });
            access = (string)t["access_token"]; SaveEnv("DISCORD_REFRESH_TOKEN", (string)t["refresh_token"]);
        }

        var au = await Cmd("AUTHENTICATE", new { access_token = access });
        if ((string)au["evt"] == "ERROR") return "FALHA: AUTHENTICATE: " + au["data"];
        var notes = new List<string>();
        var u = au["data"]?["user"];
        Console.WriteLine($"  usuário: {u?["username"]} | nome exibido: {u?["global_name"]} | id: {u?["id"]} | avatar: {u?["avatar"]}");
        Console.WriteLine($"  escopos: {au["data"]?["scopes"]?.ToJsonString()} | token expira: {au["data"]?["expires"]}");

        // --- configurações de voz
        var vs = await Cmd("GET_VOICE_SETTINGS", new { });
        if ((string)vs["evt"] == "ERROR") notes.Add("GET_VOICE_SETTINGS: " + vs["data"]);
        else
        {
            var d = vs["data"];
            Console.WriteLine($"  voz: mute={d?["mute"]} deaf={d?["deaf"]} modo={d?["mode"]?["type"]} | cancelamento de eco={d?["echo_cancellation"]} | supressão de ruído={d?["noise_suppression"]} | AGC={d?["automatic_gain_control"]}");
            Console.WriteLine($"  volume: entrada={d?["input"]?["volume"]} saída={d?["output"]?["volume"]}");
            Console.WriteLine($"  dispositivo de entrada: {d?["input"]?["available_devices"]?.AsArray().FirstOrDefault(x => (string)x["id"] == (string)d["input"]["device_id"])?["name"] ?? d?["input"]?["device_id"]}");
            Console.WriteLine($"  dispositivo de saída: {d?["output"]?["available_devices"]?.AsArray().FirstOrDefault(x => (string)x["id"] == (string)d["output"]["device_id"])?["name"] ?? d?["output"]?["device_id"]}");
        }

        // --- servidores
        var guilds = await Cmd("GET_GUILDS", new { });
        var guildMap = new Dictionary<string, string>();
        if ((string)guilds["evt"] == "ERROR") notes.Add("GET_GUILDS: " + guilds["data"]);
        else
        {
            var arr = guilds["data"]?["guilds"]?.AsArray();
            if (arr != null) foreach (var g in arr) guildMap[(string)g["id"]] = (string)g["name"];
            Console.WriteLine($"  servidores: {guildMap.Count} -> {string.Join(", ", guildMap.Values.Take(15))}{(guildMap.Count > 15 ? ", …" : "")}");
        }

        // --- canal de voz atual
        var ch = await Cmd("GET_SELECTED_VOICE_CHANNEL", new { });
        var cd = ch["data"];
        string chId = (string)cd?["id"];
        if (cd == null) Console.WriteLine("  canal de voz: (nenhum — entre num canal para ver detalhes)");
        else
        {
            var gname = guildMap.GetValueOrDefault((string)cd["guild_id"] ?? "", "(DM/grupo)");
            Console.WriteLine($"  canal de voz: {cd["name"]} | servidor: {gname} | bitrate: {cd["bitrate"]} | limite: {cd["user_limit"]} | id: {chId}");
            foreach (var vsn in cd["voice_states"]?.AsArray() ?? new JsonArray())
            {
                var st = vsn["voice_state"];
                Console.WriteLine($"    - {vsn["nick"] ?? vsn["user"]?["username"]} ({vsn["user"]?["username"]}) | mute={st?["mute"]} deaf={st?["deaf"]} self_mute={st?["self_mute"]} self_deaf={st?["self_deaf"]} | volume local={vsn["volume"]}");
            }
        }

        // --- eventos em tempo real (prova de "quem está falando")
        var subs = new List<(string evt, object args)> { ("VOICE_SETTINGS_UPDATE", new { }), ("VOICE_CHANNEL_SELECT", new { }) };
        if (chId != null)
            foreach (var e in new[] { "SPEAKING_START", "SPEAKING_STOP", "VOICE_STATE_CREATE", "VOICE_STATE_UPDATE", "VOICE_STATE_DELETE" })
                subs.Add((e, new { channel_id = chId }));
        foreach (var (evt, args) in subs)
        {
            var r = await Cmd("SUBSCRIBE", args, evt);
            if ((string)r["evt"] == "ERROR") notes.Add($"SUBSCRIBE {evt}: {r["data"]?["message"]}");
        }
        Console.WriteLine($"  ouvindo eventos por 8s ({subs.Count} assinaturas) — fale, mute/desmute ou troque de canal para testar...");
        using var cts = new CancellationTokenSource(8000); int count = 0;
        try
        {
            while (true)
            {
                var m = await Recv(cts.Token);
                if ((string)m["cmd"] == "DISPATCH") { count++; Console.WriteLine($"    evento {m["evt"]}: {Short(m["data"])}"); }
            }
        }
        catch (OperationCanceledException) { }
        Console.WriteLine($"  eventos recebidos: {count}");

        return notes.Count == 0 ? "OK" : "PARCIAL: " + string.Join("; ", notes);
    }
}

class Tee(TextWriter a, TextWriter b) : TextWriter
{
    public override Encoding Encoding => a.Encoding;
    public override void Write(char c) { a.Write(c); b.Write(c); }
    public override void Write(string s) { a.Write(s); b.Write(s); }
}

static class Native
{
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int n);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
