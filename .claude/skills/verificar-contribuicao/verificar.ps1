# SPDX-License-Identifier: GPL-3.0-or-later
# Confere o diff de uma contribuição. Uso: powershell -NoProfile -File verificar.ps1 [-Testes] [-Base main]
param([switch]$Testes, [string]$Base = "main")

$ErrorActionPreference = "Stop"
[Console]::OutputEncoding = [Text.Encoding]::UTF8   # git e o console falam UTF-8
$raiz = (git rev-parse --show-toplevel).Trim()
Set-Location $raiz
$achados = New-Object System.Collections.Generic.List[string]
function Achado($msg) { $achados.Add($msg) | Out-Null; Write-Host "  ! $msg" -ForegroundColor Yellow }

# Base de comparação: ponto de encontro com a main, se existir; senão HEAD.
$mb = git merge-base HEAD $Base 2>$null
if (-not $mb) { $mb = "HEAD" }

$mudados = @(git diff --name-only --diff-filter=AMR $mb) + @(git ls-files --others --exclude-standard) | Sort-Object -Unique | Where-Object { $_ }
$novos = @(git diff --name-only --diff-filter=A $mb) + @(git ls-files --others --exclude-standard) | Sort-Object -Unique | Where-Object { $_ }
Write-Host "Arquivos alterados ou novos: $($mudados.Count)"

Write-Host "`n[1] Cabeçalho de licença nos arquivos de código novos"
foreach ($f in $novos | Where-Object { $_ -match '\.(cs|kt|kts)$' -and $_ -notmatch '(^|/)(bin|obj|build)/' }) {
    $cab = (Get-Content -LiteralPath $f -TotalCount 3 -Encoding UTF8) -join "`n"
    if ($cab -notmatch 'SPDX-License-Identifier: GPL-3\.0-or-later' -or $cab -notmatch 'Copyright \(C\) \d{4} \S') {
        Achado "$f sem cabeçalho SPDX/Copyright nas 3 primeiras linhas"
    }
}

Write-Host "`n[2] Arquivos que não devem ir"
foreach ($f in $mudados) {
    if ($f -match '(^|/)(\.env|appsettings\.Local\.json|local\.properties)$' -or $f -match '\.(jks|keystore|pfx|p12)$' -or $f -match '(^|/)(bin|obj|build)/') {
        Achado "$f não deve entrar no repositório"
    }
    if ($f -match '^probe_tests/') { Achado "$f (probe_tests é somente leitura)" }
    if ($f -eq 'docs/prompt-original.md') { Achado "$f é registro histórico: não edite" }
}
if ((Test-Path docs/test-client.html) -and (Test-Path agent/tools/test-client.html)) {
    if ((Get-FileHash docs/test-client.html).Hash -ne (Get-FileHash agent/tools/test-client.html).Hash) {
        Achado "agent/tools/test-client.html difere de docs/test-client.html (devem ser cópias exatas)"
    }
}

Write-Host "`n[3] Dados pessoais e segredos no texto acrescentado"
$diff = (git diff -U0 $mb -- . ':!*.lock' ':!THIRD-PARTY-NOTICES.md' ':!LICENSE' ':!CONTRIBUTING.md' ':!.claude') -split "`n" | Where-Object { $_ -match '^\+' -and $_ -notmatch '^\+\+\+' }
$textoNovo = @($diff)
foreach ($f in $novos | Where-Object { $_ -notmatch '\.(png|jpg|ico|ttf|otf|jar|apk)$' -and $_ -notmatch '(^|/)(bin|obj|build)/' -and $_ -ne 'CONTRIBUTING.md' -and $_ -notmatch '^\.claude/' }) {
    $textoNovo += (Get-Content -LiteralPath $f -Encoding UTF8 -ErrorAction SilentlyContinue | ForEach-Object { "+$_" })
}
$padroes = @(
    @{ N = "IP de rede local";        R = '\b(192\.168|10\.\d{1,3}|172\.(1[6-9]|2\d|3[01]))\.\d{1,3}\.\d{1,3}\b' },
    @{ N = "caminho de usuário";      R = '(?i)[A-Z]:\\Users\\[^\\\s"'']+|/home/[a-z][^/\s"'']+|/Users/[A-Za-z][^/\s"'']+' },
    @{ N = "e-mail";                  R = '[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}' },
    @{ N = "token/chave";             R = '(?i)(ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|sk-[A-Za-z0-9]{20,}|AKIA[0-9A-Z]{16}|xox[bp]-[A-Za-z0-9-]{10,}|-----BEGIN [A-Z ]*PRIVATE KEY-----)' },
    @{ N = "atribuição de IA";        R = '(?i)Co-Authored-By|Generated with' }
)
$permitidosIp = '192\.168\.0\.12'   # valor falso usado em exemplos
foreach ($p in $padroes) {
    $hits = $textoNovo | Where-Object { $_ -match $p.R -and -not ($p.N -eq "IP de rede local" -and $_ -match $permitidosIp) -and -not ($p.N -eq "e-mail" -and $_ -match '@(example\.(com|org)|noreply\.github\.com)') }
    foreach ($h in ($hits | Select-Object -First 5)) {
        $t = $h.Trim(); if ($t.Length -gt 90) { $t = $t.Substring(0, 90) + "..." }
        Achado "$($p.N): $t"
    }
}

if ($Testes) {
    Write-Host "`n[4] Testes"
    Push-Location agent; dotnet test --nologo -v q; $a = $LASTEXITCODE; Pop-Location
    if ($a -ne 0) { Achado "dotnet test falhou (agent)" }
    if ($mudados | Where-Object { $_ -match '^app/' }) {
        Push-Location app; & ./gradlew.bat :core:test --console=plain -q; $b = $LASTEXITCODE; Pop-Location
        if ($b -ne 0) { Achado ":core:test falhou (app)" }
    }
}

Write-Host ""
if ($achados.Count -eq 0) { Write-Host "Nada suspeito encontrado." -ForegroundColor Green; exit 0 }
Write-Host "$($achados.Count) ponto(s) para conferir." -ForegroundColor Yellow
exit 1
