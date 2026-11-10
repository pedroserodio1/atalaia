# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Pedro Serôdio

<#
.SYNOPSIS
  Testes de scripts/versao.ps1. Sem framework: roda em Windows PowerShell 5.1 e em pwsh; termina com código 1 se algo falhar.
  Uso: pwsh -NoProfile -File scripts/tests/run.ps1
#>
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

. (Join-Path (Join-Path $PSScriptRoot '..') 'versao.ps1')
$casos = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $PSScriptRoot 'semver-casos.json') | ConvertFrom-Json

$script:total = 0
$script:falhas = New-Object System.Collections.Generic.List[string]

function Teste {
    param([string] $Nome, [scriptblock] $Corpo)
    $script:total++
    try { & $Corpo | Out-Null }
    catch { $script:falhas.Add("$Nome :: $($_.Exception.Message)") }
}
function Igual {
    param($Esperado, $Atual, [string] $Msg = '')
    if ($Esperado -ne $Atual) { throw "esperava '$Esperado', veio '$Atual'. $Msg" }
}
function Falha {
    # O bloco precisa lançar erro; se $Trecho vier, a mensagem tem que conter o trecho.
    param([scriptblock] $Corpo, [string] $Trecho = '')
    $erro = $null
    try { & $Corpo } catch { $erro = $_.Exception.Message }
    if ($null -eq $erro) { throw 'esperava erro, mas não houve' }
    if ($Trecho -and $erro -notlike "*$Trecho*") { throw "erro sem o trecho '$Trecho': $erro" }
}

# --- Comparador (ordem do Anexo A, seção 2) ---------------------------------------------------------------------------
$ordem = @($casos.ordem)
for ($i = 0; $i -lt $ordem.Count; $i++) {
    for ($j = 0; $j -lt $ordem.Count; $j++) {
        $a = $ordem[$i]; $b = $ordem[$j]
        $esperado = [Math]::Sign($i - $j)
        Teste "comparar $a x $b" { Igual $esperado (Compare-AtalaiaVersao (ConvertFrom-AtalaiaVersao $a) (ConvertFrom-AtalaiaVersao $b)) }
    }
}
Teste 'beta.10 maior que beta.2' { Igual 1 (Compare-AtalaiaVersao (ConvertFrom-AtalaiaVersao '0.1.1-beta.10') (ConvertFrom-AtalaiaVersao '0.1.1-beta.2')) }
Teste 'beta menor que estável' { Igual -1 (Compare-AtalaiaVersao (ConvertFrom-AtalaiaVersao '0.1.1-beta.9') (ConvertFrom-AtalaiaVersao '0.1.1')) }

# --- Versões e tags inválidas ------------------------------------------------------------------------------------------
foreach ($texto in $casos.versoesInvalidas) {
    Teste "versão inválida '$texto'" { Falha { ConvertFrom-AtalaiaVersao $texto } }
}
foreach ($texto in $casos.tagsInvalidas) {
    Teste "tag inválida '$texto'" { Falha { ConvertFrom-AtalaiaTag $texto } 'Tag inválida' }
}
Teste 'tag nula' { Falha { ConvertFrom-AtalaiaTag $null } 'Tag inválida' }
Teste 'tag beta válida' {
    $v = ConvertFrom-AtalaiaTag 'v0.1.1-beta.3'
    Igual '0.1.1-beta.3' $v.Versao; Igual '0.1.1' $v.Core; Igual 'beta.3' $v.Pre; Igual $true $v.EhBeta
}
Teste 'tag estável válida' {
    $v = ConvertFrom-AtalaiaTag 'v1.4.2'
    Igual '1.4.2' $v.Versao; Igual 'stable' $v.Pre; Igual $false $v.EhBeta
}

# --- versionCode -------------------------------------------------------------------------------------------------------
foreach ($c in $casos.versionCode) {
    $versao = $c.versao; $codigo = $c.codigo
    Teste "versionCode $versao" { Igual $codigo (Get-AtalaiaVersionCode (ConvertFrom-AtalaiaVersao $versao)) }
}
Teste 'versionCode cresce na ordem do SemVer' {
    $anterior = -1
    foreach ($texto in $ordem) {
        $cod = Get-AtalaiaVersionCode (ConvertFrom-AtalaiaVersao $texto)
        if ($cod -le $anterior) { throw "$texto ($cod) não é maior que o anterior ($anterior)" }
        $anterior = $cod
    }
}

# --- Coerência com o VERSION -------------------------------------------------------------------------------------------
Teste 'VERSION igual' { Test-AtalaiaCoerenciaVersion (ConvertFrom-AtalaiaTag 'v0.1.1-beta.1') "0.1.1`r`n" }
Teste 'VERSION diferente' { Falha { Test-AtalaiaCoerenciaVersion (ConvertFrom-AtalaiaTag 'v0.1.2') '0.1.1' } 'VERSION' }

# --- Sequência e número contra os commits (funções puras) --------------------------------------------------------------
Teste 'beta não maior que a estável' { Falha { Test-AtalaiaSequencia (ConvertFrom-AtalaiaTag 'v0.1.0-beta.1') @('v0.1.0') } 'não é maior' }
Teste 'estável menor que a última' { Falha { Test-AtalaiaSequencia (ConvertFrom-AtalaiaTag 'v0.1.0') @('v0.1.0', 'v0.2.0') } 'não é maior' }
Teste 'beta fora de ordem' { Falha { Test-AtalaiaSequencia (ConvertFrom-AtalaiaTag 'v0.2.0-beta.1') @('v0.1.0', 'v0.2.0-beta.2') } 'já existe' }
Teste 'beta nova ok' { $null = Test-AtalaiaSequencia (ConvertFrom-AtalaiaTag 'v0.2.0-beta.3') @('v0.1.0', 'v0.2.0-beta.2') }
Teste 'a própria tag não conta' { $null = Test-AtalaiaSequencia (ConvertFrom-AtalaiaTag 'v0.2.0-beta.1') @('v0.1.0', 'v0.2.0-beta.1') }
Teste 'tag fora do padrão é ignorada' { $null = Test-AtalaiaSequencia (ConvertFrom-AtalaiaTag 'v0.2.0') @('v0.1.0', 'vfoo', 'release-1') }
Teste 'sem estável anterior' { $null = Test-AtalaiaSequencia (ConvertFrom-AtalaiaTag 'v0.1.1-beta.1') @() }

$ult = ConvertFrom-AtalaiaVersao '0.1.0'
Teste 'feat com patch recusa' { Falha { Test-AtalaiaNumeroContraCommits (ConvertFrom-AtalaiaTag 'v0.1.1') $ult @('feat(agent): janela') } 'feat' }
Teste 'feat com minor ok' { Test-AtalaiaNumeroContraCommits (ConvertFrom-AtalaiaTag 'v0.2.0') $ult @('feat: janela') }
Teste 'fix com patch ok' { Test-AtalaiaNumeroContraCommits (ConvertFrom-AtalaiaTag 'v0.1.1') $ult @('fix: reconexão', 'docs: texto') }
Teste 'quebra com ! antes da 1.0 pede minor' { Falha { Test-AtalaiaNumeroContraCommits (ConvertFrom-AtalaiaTag 'v0.1.1') $ult @('refactor(app)!: remove opção') } 'quebra' }
Teste 'quebra com ! e minor ok' { Test-AtalaiaNumeroContraCommits (ConvertFrom-AtalaiaTag 'v0.2.0') $ult @('refactor(app)!: remove opção') }
Teste 'BREAKING CHANGE no corpo' { Falha { Test-AtalaiaNumeroContraCommits (ConvertFrom-AtalaiaTag 'v0.1.1') $ult @("fix: x`n`nBREAKING CHANGE: muda formato") } 'quebra' }
Teste 'quebra depois da 1.0 pede major' {
    $u1 = ConvertFrom-AtalaiaVersao '1.2.0'
    Falha { Test-AtalaiaNumeroContraCommits (ConvertFrom-AtalaiaTag 'v1.3.0') $u1 @('feat!: novo protocolo') } 'quebra'
    Test-AtalaiaNumeroContraCommits (ConvertFrom-AtalaiaTag 'v2.0.0') $u1 @('feat!: novo protocolo')
}
Teste 'sem estável anterior não confere' { Test-AtalaiaNumeroContraCommits (ConvertFrom-AtalaiaTag 'v0.1.1') $null @('feat!: tudo') }

# --- Marca: arquivos gerados por scripts/gerar-marca.ps1 (só confere os arquivos já versionados; não precisa do Edge) -----
$raizRepo = (Resolve-Path (Join-Path (Join-Path $PSScriptRoot '..') '..')).Path
$marca = Join-Path $raizRepo 'assets/brand'
function Get-PngTamanho([string] $Caminho) {
    $b = [System.IO.File]::ReadAllBytes($Caminho)
    $assinatura = ($b[0..7] | ForEach-Object { '{0:X2}' -f $_ }) -join ''
    if ($assinatura -ne '89504E470D0A1A0A') { throw "$Caminho não é PNG" }
    $w = ([int] $b[16] -shl 24) + ([int] $b[17] -shl 16) + ([int] $b[18] -shl 8) + [int] $b[19]
    $h = ([int] $b[20] -shl 24) + ([int] $b[21] -shl 16) + ([int] $b[22] -shl 8) + [int] $b[23]
    "${w}x${h}"
}
foreach ($t in 16, 24, 32, 48, 64, 128, 256, 512) {
    Teste "marca: icone-$t.png tem ${t}x${t}" { Igual "${t}x${t}" (Get-PngTamanho (Join-Path $marca "png/icone-$t.png")) }
}
Teste 'marca: social-preview.png é 1280x640' { Igual '1280x640' (Get-PngTamanho (Join-Path $marca 'social-preview.png')) }
Teste 'marca: atalaia.ico tem as 6 resoluções, em PNG' {
    $b = [System.IO.File]::ReadAllBytes((Join-Path $raizRepo 'agent/src/Atalaia.Agent.Host/Assets/atalaia.ico'))
    Igual 0 ([int] $b[0] + [int] $b[1]); Igual 1 $b[2]; Igual 6 $b[4]
    $tamanhos = foreach ($i in 0..5) { $d = $b[6 + 16 * $i]; if ($d -eq 0) { 256 } else { [int] $d } }
    Igual '16,24,32,48,64,256' ($tamanhos -join ',')
    foreach ($i in 0..5) {
        $off = [BitConverter]::ToUInt32($b, 6 + 16 * $i + 12)
        Igual '89504E47' ((($b[$off..($off + 3)]) | ForEach-Object { '{0:X2}' -f $_ }) -join '')
    }
}
foreach ($par in @(@('instalador-grande.bmp', 164, 314), @('instalador-pequena.bmp', 55, 58))) {
    $arqBmp = $par[0]; $w = $par[1]; $h = $par[2]
    Teste "marca: $arqBmp é BMP de 24 bits ${w}x${h}" {
        $b = [System.IO.File]::ReadAllBytes((Join-Path $marca $arqBmp))
        Igual 'BM' ([System.Text.Encoding]::ASCII.GetString($b, 0, 2))
        Igual $w ([BitConverter]::ToInt32($b, 18)); Igual $h ([Math]::Abs([BitConverter]::ToInt32($b, 22))); Igual 24 ([BitConverter]::ToUInt16($b, 28))
    }
}
foreach ($svg in Get-ChildItem -LiteralPath $marca -Filter '*.svg') {
    $arq = $svg.FullName
    Teste "marca: $($svg.Name) é autocontido" {
        $texto = [System.IO.File]::ReadAllText($arq)
        foreach ($proibido in 'http://', 'https://', '<image', '<text', 'font-family', '@import', 'xlink:href', '<script', 'url(') {
            # o único endereço permitido é o namespace do SVG
            $limpo = $texto.Replace('http://www.w3.org/2000/svg', '')
            if ($limpo.Contains($proibido)) { throw "contém '$proibido'" }
        }
    }
}

# --- Com git de verdade, num repositório temporário (nunca toca o repositório do projeto) ------------------------------
$raiz = Join-Path ([System.IO.Path]::GetTempPath()) ("atalaia-versao-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $raiz | Out-Null
$local = Get-Location
$arq = $null
try {
    Set-Location $raiz
    function G {
        # git escreve mensagens normais em stderr; no Windows PowerShell 5.1 isso vira erro se $ErrorActionPreference for Stop.
        $ErrorActionPreference = 'Continue'
        & git -c user.name=Teste -c user.email=teste@example.invalid -c commit.gpgsign=false -c core.autocrlf=false @args 2>&1 | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "git $args falhou" }
    }
    function Commit([string] $msg) { Set-Content -LiteralPath (Join-Path $raiz 'arq.txt') -Value ([guid]::NewGuid().ToString()); G add -A; G commit -m $msg }
    G init -b main
    Commit 'chore: início'
    $c1 = (git rev-parse HEAD).Trim()
    G tag v0.1.0                                   # última estável
    G switch -c development
    Commit 'fix: correção'
    $c2 = (git rev-parse HEAD).Trim()
    $arq = Join-Path ([System.IO.Path]::GetTempPath()) ("atalaia-VERSION-" + [guid]::NewGuid().ToString('N'))   # fora do repositório de teste

    Set-Content -LiteralPath $arq -Value '0.1.1'
    Teste 'git: beta em development ok' { $null = Invoke-AtalaiaValidar -Tag 'v0.1.1-beta.1' -Sha $c2 -ArquivoVersion $arq -RefMain main -RefDevelopment development }
    Teste 'git: estável fora de main recusa' { Falha { Invoke-AtalaiaValidar -Tag 'v0.1.1' -Sha $c2 -ArquivoVersion $arq -RefMain main -RefDevelopment development } 'não está em main' }
    Teste 'git: ensaio ignora o ramo' { $null = Invoke-AtalaiaValidar -Tag 'v0.1.1' -Sha $c2 -ArquivoVersion $arq -RefMain main -RefDevelopment development -Ensaio }
    Teste 'git: beta fora de development recusa' {
        G switch -c solto $c1; Commit 'fix: solto'; $solto = (git rev-parse HEAD).Trim(); G switch development
        Falha { Invoke-AtalaiaValidar -Tag 'v0.1.1-beta.1' -Sha $solto -ArquivoVersion $arq -RefMain main -RefDevelopment development } 'não está em development'
    }
    Teste 'git: ramo inexistente' { Falha { Invoke-AtalaiaValidar -Tag 'v0.1.1-beta.1' -Sha $c2 -ArquivoVersion $arq -RefMain main -RefDevelopment nao-existe } 'não encontrado' }
    Teste 'git: VERSION diferente da tag' { Falha { Invoke-AtalaiaValidar -Tag 'v0.1.2-beta.1' -Sha $c2 -ArquivoVersion $arq -RefMain main -RefDevelopment development } 'VERSION' }

    Commit 'feat(agent): janela nova'
    $c3 = (git rev-parse HEAD).Trim()
    Teste 'git: feat com patch recusa' { Falha { Invoke-AtalaiaValidar -Tag 'v0.1.1-beta.1' -Sha $c3 -ArquivoVersion $arq -RefMain main -RefDevelopment development } 'feat' }
    Set-Content -LiteralPath $arq -Value '0.2.0'
    Teste 'git: feat com minor ok' { $null = Invoke-AtalaiaValidar -Tag 'v0.2.0-beta.1' -Sha $c3 -ArquivoVersion $arq -RefMain main -RefDevelopment development }
    Teste 'git: beta atrás de outra já publicada recusa' {
        G tag v0.2.0-beta.2 $c3
        Falha { Invoke-AtalaiaValidar -Tag 'v0.2.0-beta.1' -Sha $c3 -ArquivoVersion $arq -RefMain main -RefDevelopment development } 'já existe'
        $null = Invoke-AtalaiaValidar -Tag 'v0.2.0-beta.3' -Sha $c3 -ArquivoVersion $arq -RefMain main -RefDevelopment development
    }
}
finally {
    Set-Location $local
    Remove-Item -LiteralPath $raiz -Recurse -Force -ErrorAction SilentlyContinue
    if ($arq) { Remove-Item -LiteralPath $arq -Force -ErrorAction SilentlyContinue }
}

# --- Resultado ---------------------------------------------------------------------------------------------------------
if ($script:falhas.Count -gt 0) {
    $script:falhas | ForEach-Object { Write-Output "FALHOU: $_" }
    Write-Output "$($script:falhas.Count) de $($script:total) testes falharam."
    exit 1
}
Write-Output "$($script:total) testes passaram."
