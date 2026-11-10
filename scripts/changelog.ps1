# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Pedro Serôdio

<#
.SYNOPSIS
  Gera a seção da versão estável no CHANGELOG.md (só nas estáveis; as betas ficam nas notas da release).
  Rode no ramo do PR de development para main, antes do merge. Precisa do git-cliff:
  pip install --require-hashes -r scripts/requisitos-cliff.txt
.EXAMPLE
  pwsh scripts/changelog.ps1 -Versao 0.1.1
#>
param([Parameter(Mandatory)] [string] $Versao)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

if ($Versao -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\z') { throw "Versão estável inválida: '$Versao' (use X.Y.Z)." }
if (-not (Get-Command git-cliff -ErrorAction SilentlyContinue)) { throw 'git-cliff não encontrado. Veja scripts/requisitos-cliff.txt.' }

$raiz = Join-Path $PSScriptRoot '..'
$arquivo = Join-Path $raiz 'CHANGELOG.md'
Push-Location $raiz
try {
    # tag_pattern do cliff.toml só enxerga estáveis: as betas entram somadas na seção da estável.
    if (Test-Path -LiteralPath $arquivo) {
        git-cliff --config cliff.toml --unreleased --tag "v$Versao" --prepend $arquivo
    }
    else {
        git-cliff --config cliff.toml --tag "v$Versao" --output $arquivo
    }
    if ($LASTEXITCODE -ne 0) { throw 'git-cliff falhou.' }
    Write-Output "CHANGELOG.md atualizado para $Versao. Confira o texto antes de commitar."
}
finally { Pop-Location }
