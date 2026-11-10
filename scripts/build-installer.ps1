# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Pedro Serôdio

<#
.SYNOPSIS
  Publica o agente (win-x64, .NET autocontido) e monta o instalador com o Inno Setup.
  Saída: <Saida>/Atalaia-Agent-Setup-<Versao>.exe (e a pasta publish/ ao lado).
.EXAMPLE
  pwsh scripts/build-installer.ps1 -Versao 0.1.1-beta.1 -Pre beta.1 -Saida dist
  pwsh scripts/build-installer.ps1 -Versao 0.1.1-dev -Pre dev       # build local, nunca publicado
#>
param(
    [Parameter(Mandatory)] [string] $Versao,   # X.Y.Z[-beta.N|-dev], como sai do versao.ps1 (campo version)
    [Parameter(Mandatory)] [string] $Pre,      # beta.N | stable | dev (vai para -p:AtalaiaPre)
    [string] $Saida = 'dist'
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

if ($Versao -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(-(beta\.[0-9]+|dev))?\z') { throw "Versão inválida: '$Versao'." }
if ($Pre -notmatch '^(beta\.[0-9]+|stable|dev)\z') { throw "Pre inválido: '$Pre' (use beta.N, stable ou dev)." }

$raiz = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$saidaAbs = [System.IO.Path]::GetFullPath((Join-Path $raiz $Saida))
$publish = Join-Path $saidaAbs 'publish'
New-Item -ItemType Directory -Force -Path $saidaAbs | Out-Null
if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }

function Find-Iscc {
    if ($env:ISCC -and (Test-Path -LiteralPath $env:ISCC)) { return $env:ISCC }
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }
    $candidatos = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    )
    foreach ($c in $candidatos) { if (Test-Path -LiteralPath $c) { return $c } }
    return $null
}

# 1) Publica o agente. O sufixo da versão vem de -p:AtalaiaPre (agent/Directory.Build.props).
dotnet publish (Join-Path $raiz 'agent/src/Atalaia.Agent.Host') -c Release -r win-x64 --self-contained true "-p:AtalaiaPre=$Pre" -o $publish
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish falhou.' }

# 2) A versão embutida no .exe precisa ser a do instalador.
$produto = (Get-Item -LiteralPath (Join-Path $publish 'AtalaiaAgent.exe')).VersionInfo.ProductVersion
if (-not $produto.StartsWith($Versao)) { throw "ProductVersion do AtalaiaAgent.exe ($produto) não começa com $Versao." }

# 3) Inno Setup.
$iscc = Find-Iscc
if (-not $iscc -and $env:GITHUB_ACTIONS) {
    choco install innosetup --no-progress -y
    $iscc = Find-Iscc
}
if (-not $iscc) { throw 'Inno Setup 6 não encontrado. Instale (https://jrsoftware.org/isinfo.php) ou defina a variável ISCC com o caminho do ISCC.exe.' }

& $iscc /Q "/DAppVersion=$Versao" "/DPublishDir=$publish" "/O$saidaAbs" (Join-Path $raiz 'installer/atalaia.iss')
if ($LASTEXITCODE -ne 0) { throw 'ISCC falhou.' }

$exe = Join-Path $saidaAbs "Atalaia-Agent-Setup-$Versao.exe"
if (-not (Test-Path -LiteralPath $exe)) { throw "Instalador esperado não foi gerado: $exe" }
Write-Output ("{0}  ({1:N1} MB)" -f $exe, ((Get-Item -LiteralPath $exe).Length / 1MB))
