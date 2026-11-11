# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Pedro Serôdio

<#
.SYNOPSIS
  Versão e validação de release do Atalaia (regras em docs/versionamento-e-releases.md).

.DESCRIPTION
  -Acao calcular  só lê a tag (sem git): imprime version, core, pre, prerelease e versionCode.
  -Acao validar   além disso confere: tag x arquivo VERSION, versão maior que a última estável, ordem dos betas,
                  ramo do commit (estável em origin/main, beta em origin/development) e o número contra os
                  Conventional Commits desde a última estável. Qualquer falha termina com código 1 e nada é publicado.
  A saída é "chave=valor", uma por linha, pronta para >> $GITHUB_OUTPUT.

.EXAMPLE
  pwsh scripts/versao.ps1 -Acao calcular -Tag v0.1.1-beta.1
  pwsh scripts/versao.ps1 -Acao validar -Tag v0.1.1-beta.1 -Sha HEAD
#>
param(
    [ValidateSet('calcular', 'validar')] [string] $Acao,
    [string] $Tag,
    [string] $Sha = 'HEAD',
    [string] $ArquivoVersion = (Join-Path (Join-Path $PSScriptRoot '..') 'VERSION'),
    [string] $RefMain = 'origin/main',
    [string] $RefDevelopment = 'origin/development',
    [switch] $Ensaio   # dry_run: não exige que o commit já esteja em main/development
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

# [0-9] e \z de propósito: \d aceita dígitos de outros alfabetos e $ aceita uma quebra de linha no fim.
$script:VersaoRegex = '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-beta\.([1-9][0-9]*))?\z'

function ConvertFrom-AtalaiaVersao {
    # "X.Y.Z" ou "X.Y.Z-beta.N" (sem o "v"). Limites do versionCode: MAJOR até 20, MINOR e PATCH até 999, beta de 1 a 98.
    param([string] $Texto)
    $m = [regex]::Match($Texto, $script:VersaoRegex)
    if (-not $m.Success) { throw "Versão inválida: '$Texto' (use X.Y.Z ou X.Y.Z-beta.N)." }
    $maior = [long] $m.Groups[1].Value
    $menor = [long] $m.Groups[2].Value
    $correcao = [long] $m.Groups[3].Value
    $beta = $null
    if ($m.Groups[4].Success) { $beta = [long] $m.Groups[4].Value }
    if ($maior -gt 20 -or $menor -gt 999 -or $correcao -gt 999) {
        throw "Versão fora dos limites do versionCode (MAJOR até 20, MINOR e PATCH até 999): '$Texto'."
    }
    if ($null -ne $beta -and $beta -gt 98) { throw "Número de beta fora do limite (1 a 98): '$Texto'." }
    $core = "$maior.$menor.$correcao"
    $ehBeta = $null -ne $beta
    [pscustomobject] @{
        Major  = [int] $maior
        Minor  = [int] $menor
        Patch  = [int] $correcao
        Beta   = if ($ehBeta) { [int] $beta } else { 0 }
        EhBeta = $ehBeta
        Core   = $core
        Versao = if ($ehBeta) { "$core-beta.$beta" } else { $core }
        Pre    = if ($ehBeta) { "beta.$beta" } else { 'stable' }
    }
}

function ConvertFrom-AtalaiaTag {
    param([string] $Tag)
    if ($Tag -cnotlike 'v*') { throw "Tag inválida: '$Tag' (formato vX.Y.Z ou vX.Y.Z-beta.N)." }
    try { ConvertFrom-AtalaiaVersao $Tag.Substring(1) }
    catch { throw "Tag inválida: '$Tag' (formato vX.Y.Z ou vX.Y.Z-beta.N). $($_.Exception.Message)" }
}

function Get-AtalaiaVersionCode {
    # ((MAJOR*1000+MINOR)*1000+PATCH)*100+PRE, PRE = N do beta ou 99 (estável). Build local (dev) = 0 só no Gradle.
    param($Versao)
    $pre = 99
    if ($Versao.EhBeta) { $pre = $Versao.Beta }
    [int] ((($Versao.Major * 1000 + $Versao.Minor) * 1000 + $Versao.Patch) * 100 + $pre)
}

function Compare-AtalaiaVersao {
    # Precedência do SemVer: -1, 0 ou 1. Sem sufixo vem depois de beta; beta compara como número (beta.10 > beta.2).
    param($A, $B)
    foreach ($campo in 'Major', 'Minor', 'Patch') {
        if ($A.$campo -ne $B.$campo) { return [Math]::Sign($A.$campo - $B.$campo) }
    }
    if ($A.EhBeta -and -not $B.EhBeta) { return -1 }
    if (-not $A.EhBeta -and $B.EhBeta) { return 1 }
    if ($A.EhBeta) { return [Math]::Sign($A.Beta - $B.Beta) }
    return 0
}

function Compare-AtalaiaCore {
    param($A, $B)
    foreach ($campo in 'Major', 'Minor', 'Patch') {
        if ($A.$campo -ne $B.$campo) { return [Math]::Sign($A.$campo - $B.$campo) }
    }
    return 0
}

function Test-AtalaiaCoerenciaVersion {
    # O X.Y.Z da tag precisa ser o do arquivo VERSION (evita esquecer de abrir o ciclo).
    param($Versao, [string] $ConteudoVersion)
    $core = $ConteudoVersion.Trim()
    if ($core -ne $Versao.Core) {
        throw "O X.Y.Z da tag ($($Versao.Core)) é diferente do arquivo VERSION ($core). Abra o ciclo: atualize o VERSION."
    }
}

function ConvertTo-AtalaiaTagsValidas {
    # Só as tags no formato do projeto; as demais são ignoradas.
    param([string[]] $Tags)
    foreach ($t in $Tags) {
        try { ConvertFrom-AtalaiaTag $t } catch { }
    }
}

function Get-AtalaiaUltimaEstavel {
    param([string[]] $Tags, [string] $Ignorar)
    $ultima = $null
    foreach ($v in ConvertTo-AtalaiaTagsValidas $Tags) {
        if ($v.EhBeta -or "v$($v.Versao)" -eq $Ignorar) { continue }
        if ($null -eq $ultima -or (Compare-AtalaiaVersao $v $ultima) -gt 0) { $ultima = $v }
    }
    $ultima
}

function Test-AtalaiaSequencia {
    # O número precisa ser maior que a última estável (beta ou não) e o beta, maior que os betas já publicados do mesmo X.Y.Z.
    param($Versao, [string[]] $Tags)
    $tagAtual = "v$($Versao.Versao)"
    $ultima = Get-AtalaiaUltimaEstavel $Tags $tagAtual
    if ($null -ne $ultima -and (Compare-AtalaiaCore $Versao $ultima) -le 0) {
        throw "A versão $($Versao.Core) não é maior que a última estável publicada (v$($ultima.Core))."
    }
    foreach ($outra in ConvertTo-AtalaiaTagsValidas $Tags) {
        if ("v$($outra.Versao)" -eq $tagAtual) { continue }
        if ($Versao.EhBeta -and $outra.EhBeta -and $outra.Core -eq $Versao.Core -and $outra.Beta -ge $Versao.Beta) {
            throw "A beta $($Versao.Versao) vem depois de v$($outra.Versao), que já existe: use um número maior."
        }
    }
    $ultima
}

function Test-AtalaiaNumeroContraCommits {
    # Se há feat, o número não pode ser só patch; se há quebra, precisa ser minor (antes da 1.0) ou major.
    param($Versao, $UltimaEstavel, [string[]] $Mensagens)
    if ($null -eq $UltimaEstavel) { return }
    $temFeat = $false
    $temQuebra = $false
    foreach ($msg in $Mensagens) {
        $assunto = ($msg -split "`n")[0]
        if ($assunto -match '^feat(\([^)]*\))?!?:') { $temFeat = $true }
        if ($assunto -match '^[a-z]+(\([^)]*\))?!:' -or $msg -match '(?m)^BREAKING[ -]CHANGE:') { $temQuebra = $true }
    }
    $subiuMajor = $Versao.Major -gt $UltimaEstavel.Major
    $subiuMinor = $subiuMajor -or $Versao.Minor -gt $UltimaEstavel.Minor
    if ($temQuebra) {
        $ok = $subiuMajor -or ($Versao.Major -eq 0 -and $subiuMinor)
        if (-not $ok) {
            throw "Há commit com quebra (! ou BREAKING CHANGE) desde v$($UltimaEstavel.Core): a versão $($Versao.Core) precisa subir o minor (antes da 1.0) ou o major."
        }
    }
    elseif ($temFeat -and -not $subiuMinor) {
        throw "Há commit feat desde v$($UltimaEstavel.Core): a versão $($Versao.Core) não pode ser só patch."
    }
}

function Test-AtalaiaRamo {
    # Estável só em commit de main; beta só em commit de development.
    param($Versao, [string] $Sha, [string] $RefMain, [string] $RefDevelopment)
    $ref = $RefMain
    if ($Versao.EhBeta) { $ref = $RefDevelopment }
    $null = git rev-parse --verify --quiet "$ref^{commit}"
    if ($LASTEXITCODE -ne 0) { throw "Ramo '$ref' não encontrado (faça git fetch dele)." }
    $null = git merge-base --is-ancestor $Sha $ref
    if ($LASTEXITCODE -ne 0) {
        $tipo = 'estável'
        if ($Versao.EhBeta) { $tipo = 'beta' }
        throw "O commit $Sha não está em ${ref}: tag $tipo só vale em commit desse ramo."
    }
}

function Get-AtalaiaMensagens {
    param([string] $Desde, [string] $Ate)
    $faixa = $Ate
    if ($Desde) { $faixa = "$Desde..$Ate" }
    $bruto = (git log --format='%B%x00' $faixa) -join "`n"
    if ($LASTEXITCODE -ne 0) { throw "git log falhou para $faixa." }
    $bruto -split "`0" | Where-Object { $_.Trim() }
}

function Invoke-AtalaiaValidar {
    param([string] $Tag, [string] $Sha, [string] $ArquivoVersion, [string] $RefMain, [string] $RefDevelopment, [switch] $Ensaio)
    $v = ConvertFrom-AtalaiaTag $Tag
    Test-AtalaiaCoerenciaVersion $v (Get-Content -Raw -LiteralPath $ArquivoVersion)
    $tags = @(git tag --list 'v*')
    $ultima = Test-AtalaiaSequencia $v $tags
    if (-not $Ensaio) { Test-AtalaiaRamo $v $Sha $RefMain $RefDevelopment }
    if ($null -ne $ultima) {
        Test-AtalaiaNumeroContraCommits $v $ultima (Get-AtalaiaMensagens "v$($ultima.Core)" $Sha)
    }
    $v
}

function Format-AtalaiaSaida {
    param($Versao)
    "version=$($Versao.Versao)"
    "core=$($Versao.Core)"
    "pre=$($Versao.Pre)"
    "prerelease=$(([string] $Versao.EhBeta).ToLowerInvariant())"
    "versionCode=$(Get-AtalaiaVersionCode $Versao)"
}

# Quando executado (e não importado por "." nos testes), faz o que -Acao pediu.
if ($Acao) {
    try {
        if ($Acao -eq 'calcular') { $resultado = ConvertFrom-AtalaiaTag $Tag }
        else { $resultado = Invoke-AtalaiaValidar -Tag $Tag -Sha $Sha -ArquivoVersion $ArquivoVersion -RefMain $RefMain -RefDevelopment $RefDevelopment -Ensaio:$Ensaio }
        Format-AtalaiaSaida $resultado
    }
    catch {
        [Console]::Error.WriteLine("erro: $($_.Exception.Message)")
        exit 1
    }
}
