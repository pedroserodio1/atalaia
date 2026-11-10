<#
  Mede memória, CPU, quadros e partida a frio do app no tablet (via adb). Só leitura, nada além de abrir o app.
  Uso (tablet conectado por USB, app release instalado):
    powershell -ExecutionPolicy Bypass -File tools\measure-perf.ps1 -Minutes 30
  Saída: tools\out\perf-<data>.csv (uma linha por amostra) e perf-<data>.txt (resumo).
#>
param(
    [int]$Minutes = 30,
    [int]$IntervalSec = 60,
    [int]$ColdStarts = 5,
    [string]$Package = 'org.pcpanel.app',
    [string]$Activity = '.MainActivity',
    [string]$Serial = ''
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Adb {
    # adb.exe explícito: "adb" sem extensão resolveria para esta própria função (recursão infinita).
    if ($Serial) { & adb.exe -s $Serial @args } else { & adb.exe @args }
}
function AdbText { (Adb @args) -join "`n" }

$outDir = Join-Path $PSScriptRoot 'out'
New-Item -ItemType Directory -Force $outDir | Out-Null
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$csv = Join-Path $outDir "perf-$stamp.csv"
$txt = Join-Path $outDir "perf-$stamp.txt"
$report = New-Object System.Collections.Generic.List[string]
function Say([string]$s) { Write-Host $s; $report.Add($s) }

# núcleos presentes (ex.: "0-3" = 4); os offline por economia de energia não aparecem em cpuinfo
$present = (AdbText shell cat /sys/devices/system/cpu/present).Trim()
$cores = if ($present -match '^(\d+)-(\d+)$') { [int]$Matches[2] - [int]$Matches[1] + 1 } else { 1 }
Say "Aparelho: $((AdbText shell getprop ro.product.model).Trim()), $cores núcleos, ABI $((AdbText shell getprop ro.product.cpu.abilist).Trim()), Android $((AdbText shell getprop ro.build.version.release).Trim())"

# --- partida a frio
$starts = @()
for ($i = 0; $i -lt $ColdStarts; $i++) {
    Adb shell am force-stop $Package | Out-Null
    Start-Sleep -Seconds 2
    $t = AdbText shell am start -W -n "$Package/$Activity"
    if ($t -match 'TotalTime:\s*(\d+)') { $starts += [int]$Matches[1] }
    Start-Sleep -Seconds 3
}
if ($starts.Count) {
    $sorted = $starts | Sort-Object
    Say ("Partida a frio (ms, TotalTime): {0} | mediana {1} | máximo {2}" -f ($starts -join ', '), $sorted[[int][math]::Floor($sorted.Count / 2)], $sorted[-1])
}

# --- regime
Start-Sleep -Seconds 20
Adb shell dumpsys gfxinfo $Package reset | Out-Null

function Get-AppPid { ((AdbText shell pidof $Package).Trim() -split '\s+')[0] }
function Get-Ticks([string]$p) {
    $s = (AdbText shell cat "/proc/$p/stat").Trim()
    $rest = ($s.Substring($s.LastIndexOf(')') + 2)) -split ' '
    [long]$rest[11] + [long]$rest[12]   # utime + stime (ticks de 10 ms)
}
function Get-Mem {
    $m = AdbText shell dumpsys meminfo $Package
    $g = { param($rx) if ($m -match $rx) { [int]$Matches[1] } else { -1 } }
    [pscustomobject]@{
        Pss      = & $g 'TOTAL:\s+(\d+)'
        Java     = & $g 'Java Heap:\s+(\d+)'
        Native   = & $g 'Native Heap:\s+(\d+)'
        Graphics = & $g 'Graphics:\s+(\d+)'
    }
}
function Get-MemAvail { $t = AdbText shell cat /proc/meminfo; if ($t -match 'MemAvailable:\s+(\d+)') { [int]$Matches[1] } else { -1 } }

$appPid = Get-AppPid
if (-not $appPid) { throw "App não está rodando ($Package)." }
"minuto,pid,pss_kb,java_kb,native_kb,graphics_kb,cpu_pct_total,memavail_kb,frames,janky,p50_ms,p90_ms,p95_ms,p99_ms" | Set-Content $csv -Encoding ascii
$prevTicks = Get-Ticks $appPid
$prevAt = Get-Date
$begin = Get-Date
$rows = @()
while (((Get-Date) - $begin).TotalMinutes -lt $Minutes) {
    Start-Sleep -Seconds $IntervalSec
    $now = Get-Date
    $curPid = Get-AppPid
    if ($curPid -ne $appPid) { Say "ATENÇÃO: o processo mudou de $appPid para '$curPid' (app reiniciou ou morreu)."; if (-not $curPid) { break }; $appPid = $curPid; $prevTicks = Get-Ticks $appPid; $prevAt = $now; continue }
    $ticks = Get-Ticks $appPid
    $cpu = ($ticks - $prevTicks) / (100.0 * ($now - $prevAt).TotalSeconds) / $cores * 100
    $prevTicks = $ticks; $prevAt = $now
    $mem = Get-Mem
    $gt = AdbText shell dumpsys gfxinfo $Package reset   # lê e zera: cada amostra tem só os quadros da última janela
    $gv = { param($rx) if ($gt -match $rx) { [int]$Matches[1] } else { 0 } }
    $row = [pscustomobject]@{ Frames = (& $gv 'Total frames rendered:\s+(\d+)'); Janky = (& $gv 'Janky frames:\s+(\d+)'); P50 = (& $gv '50th percentile:\s+(\d+)ms'); P90 = (& $gv '90th percentile:\s+(\d+)ms'); P95 = (& $gv '95th percentile:\s+(\d+)ms'); P99 = (& $gv '99th percentile:\s+(\d+)ms'); Min = [math]::Round(($now - $begin).TotalMinutes, 1); Pss = $mem.Pss; Java = $mem.Java; Native = $mem.Native; Gfx = $mem.Graphics; Cpu = [math]::Round($cpu, 1); Avail = Get-MemAvail }
    $rows += $row
    ("{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13}" -f $row.Min, $appPid, $row.Pss, $row.Java, $row.Native, $row.Gfx, $row.Cpu, $row.Avail, $row.Frames, $row.Janky, $row.P50, $row.P90, $row.P95, $row.P99) | Add-Content $csv -Encoding ascii
    Write-Host ("  min {0,5}  PSS {1,7} KB  CPU {2,5}%  quadros {3,4} lentos {4,4}  p50 {5}ms p95 {6}ms" -f $row.Min, $row.Pss, $row.Cpu, $row.Frames, $row.Janky, $row.P50, $row.P95)
}

# --- resumo
if ($rows.Count -ge 2) {
    $pss = $rows | ForEach-Object { $_.Pss }
    $n = $rows.Count; $xs = $rows | ForEach-Object { $_.Min }
    $mx = ($xs | Measure-Object -Average).Average; $my = ($pss | Measure-Object -Average).Average
    $num = 0.0; $den = 0.0
    for ($i = 0; $i -lt $n; $i++) { $num += ($xs[$i] - $mx) * ($pss[$i] - $my); $den += [math]::Pow($xs[$i] - $mx, 2) }
    $slopeKbPerMin = if ($den -gt 0) { $num / $den } else { 0 }
    Say ("Amostras: {0} em {1} min" -f $n, $Minutes)
    Say ("PSS total (MB): mín {0:N1} | média {1:N1} | máx {2:N1} | final {3:N1}" -f (($pss | Measure-Object -Minimum).Minimum / 1024), ($my / 1024), (($pss | Measure-Object -Maximum).Maximum / 1024), ($pss[-1] / 1024))
    Say ("Tendência do PSS: {0:N2} MB/h (extrapolação para 8 h: {1:N1} MB; com poucas amostras é só indicativo)" -f ($slopeKbPerMin * 60 / 1024), ($slopeKbPerMin * 60 * 8 / 1024))
    $java = $rows | ForEach-Object { $_.Java }; $nat = $rows | ForEach-Object { $_.Native }; $gfx = $rows | ForEach-Object { $_.Gfx }
    Say ("Java heap (MB): primeiro {0:N1} -> último {1:N1} | Native: {2:N1} -> {3:N1} | Graphics: {4:N1} -> {5:N1}" -f ($java[0] / 1024), ($java[-1] / 1024), ($nat[0] / 1024), ($nat[-1] / 1024), ($gfx[0] / 1024), ($gfx[-1] / 1024))
    $cpus = $rows | ForEach-Object { $_.Cpu }
    Say ("CPU do app (% do total de {0} núcleos): média {1:N1} | máx {2:N1}" -f $cores, ($cpus | Measure-Object -Average).Average, ($cpus | Measure-Object -Maximum).Maximum)
    Say ("MemAvailable do sistema (MB): primeiro {0:N0} -> último {1:N0}" -f ($rows[0].Avail / 1024), ($rows[-1].Avail / 1024))
}
$tf = ($rows | Measure-Object Frames -Sum).Sum; $tj = ($rows | Measure-Object Janky -Sum).Sum
if ($tf -gt 0) {
    $p95s = $rows | Where-Object { $_.Frames -gt 0 } | ForEach-Object { $_.P95 } | Sort-Object
    $p50s = $rows | Where-Object { $_.Frames -gt 0 } | ForEach-Object { $_.P50 } | Sort-Object
    Say ("Quadros (soma das janelas, dumpsys gfxinfo): {0} quadros, {1} lentos ({2:N1}%)" -f $tf, $tj, (100.0 * $tj / $tf))
    Say ("  p50 por janela: mediana {0} ms | p95 por janela: mediana {1} ms, pior {2} ms" -f $p50s[[int][math]::Floor($p50s.Count / 2)], $p95s[[int][math]::Floor($p95s.Count / 2)], $p95s[-1])
}
$report | Set-Content $txt -Encoding utf8
Write-Host "Resumo salvo em $txt"
