# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Pedro Serôdio

<#
.SYNOPSIS
  Gera os arquivos derivados da marca a partir dos SVGs de assets/brand/ (a fonte única):
    - agent/src/Atalaia.Agent.Host/Assets/atalaia.ico   (16, 24, 32, 48, 64 e 256 px; usado no .exe, na bandeja e na janela)
    - assets/brand/png/icone-<tamanho>.png               (16 a 512 px)
    - assets/brand/instalador-grande.bmp e instalador-pequena.bmp   (imagens do assistente do Inno Setup)
    - assets/brand/social-preview.png                    (1280 x 640: pré-visualização do repositório e banner do README)
  Rasteriza com o Microsoft Edge em modo headless (já vem no Windows 11): nenhuma ferramenta nova. Mesma entrada, mesmos arquivos.
  Para mudar o desenho, edite scripts/marca/gerar-svgs.py, rode-o e depois rode este script.
.EXAMPLE
  pwsh scripts/gerar-marca.ps1
#>
param(
    [string] $Edge = ''   # caminho do msedge.exe; vazio = procura nos locais padrão
)

Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$raiz = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$marca = Join-Path $raiz 'assets/brand'
$pngDir = Join-Path $marca 'png'
$icoSaida = Join-Path $raiz 'agent/src/Atalaia.Agent.Host/Assets/atalaia.ico'

if (-not $Edge) {
    foreach ($c in @((Join-Path ${env:ProgramFiles(x86)} 'Microsoft\Edge\Application\msedge.exe'), (Join-Path $env:ProgramFiles 'Microsoft\Edge\Application\msedge.exe'))) {
        if (Test-Path -LiteralPath $c) { $Edge = $c; break }
    }
}
if (-not $Edge -or -not (Test-Path -LiteralPath $Edge)) { throw 'Microsoft Edge não encontrado. Passe -Edge <caminho do msedge.exe>.' }

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) ('atalaia-marca-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tmp | Out-Null
New-Item -ItemType Directory -Force -Path $pngDir | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $icoSaida) | Out-Null

function ConvertTo-Png {
    # Rasteriza um SVG em W x H, com fundo transparente, e confere o tamanho do PNG gerado.
    param([string] $Svg, [int] $W, [int] $H, [string] $Destino)
    $html = Join-Path $tmp ([guid]::NewGuid().ToString('N') + '.html')
    $uri = ([Uri] $Svg).AbsoluteUri
    Set-Content -LiteralPath $html -Encoding UTF8 -Value "<!doctype html><meta charset=utf-8><body style='margin:0;background:transparent'><img src='$uri' style='width:${W}px;height:${H}px;display:block'>"
    $args2 = @('--headless=new', '--disable-gpu', '--hide-scrollbars', '--force-device-scale-factor=1', '--default-background-color=00000000',
        "--user-data-dir=$(Join-Path $tmp 'perfil')", "--window-size=$W,$H", "--screenshot=$Destino", ([Uri] $html).AbsoluteUri)
    $p = Start-Process -FilePath $Edge -ArgumentList $args2 -Wait -PassThru -NoNewWindow -RedirectStandardError (Join-Path $tmp 'edge.log')
    if ($p.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $Destino)) { throw "Edge falhou ao gerar $Destino (código $($p.ExitCode))." }
    $img = [System.Drawing.Image]::FromFile($Destino)
    try { if ($img.Width -ne $W -or $img.Height -ne $H) { throw "$Destino saiu com $($img.Width)x$($img.Height), esperado ${W}x${H}." } }
    finally { $img.Dispose() }
}

function ConvertTo-Bmp24 {
    # BMP de 24 bits, opaco (o assistente do Inno não aceita transparência).
    param([string] $Png, [string] $Destino)
    $src = [System.Drawing.Image]::FromFile($Png)
    try {
        $bmp = New-Object System.Drawing.Bitmap($src.Width, $src.Height, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
        try {
            $bmp.SetResolution(96, 96)
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            try { $g.Clear([System.Drawing.Color]::White); $g.DrawImage($src, 0, 0, $src.Width, $src.Height) } finally { $g.Dispose() }
            $bmp.Save($Destino, [System.Drawing.Imaging.ImageFormat]::Bmp)
        }
        finally { $bmp.Dispose() }
    }
    finally { $src.Dispose() }
}

function New-Ico {
    # .ico com entradas PNG (aceito do Windows Vista em diante): [(tamanho, caminho do PNG)] em ordem crescente.
    param($Entradas, [string] $Destino)
    $fs = [System.IO.File]::Create($Destino)
    $w = New-Object System.IO.BinaryWriter($fs)
    try {
        $w.Write([uint16] 0); $w.Write([uint16] 1); $w.Write([uint16] $Entradas.Count)
        $offset = 6 + 16 * $Entradas.Count
        $dados = @()
        foreach ($e in $Entradas) {
            $bytes = [System.IO.File]::ReadAllBytes($e.Png)
            $dim = [byte] 0
            if ($e.Tamanho -lt 256) { $dim = [byte] $e.Tamanho }
            $w.Write($dim); $w.Write($dim); $w.Write([byte] 0); $w.Write([byte] 0)
            $w.Write([uint16] 1); $w.Write([uint16] 32); $w.Write([uint32] $bytes.Length); $w.Write([uint32] $offset)
            $offset += $bytes.Length
            $dados += , $bytes
        }
        foreach ($b in $dados) { $w.Write($b) }
    }
    finally { $w.Dispose(); $fs.Dispose() }
}

try {
    $app = Join-Path $marca 'icone-app.svg'
    $peq = Join-Path $marca 'icone-pequeno.svg'

    # PNGs soltos (16 e 24 usam a versão simplificada do desenho).
    foreach ($t in 16, 24, 32, 48, 64, 128, 256, 512) {
        $svg = $app
        if ($t -le 24) { $svg = $peq }
        ConvertTo-Png $svg $t $t (Join-Path $pngDir "icone-$t.png")
    }
    # .ico
    $entradas = foreach ($t in 16, 24, 32, 48, 64, 256) { [pscustomobject] @{ Tamanho = $t; Png = (Join-Path $pngDir "icone-$t.png") } }
    New-Ico $entradas $icoSaida

    # Assistente do Inno Setup.
    ConvertTo-Png (Join-Path $marca 'instalador-grande.svg') 164 314 (Join-Path $tmp 'grande.png')
    ConvertTo-Bmp24 (Join-Path $tmp 'grande.png') (Join-Path $marca 'instalador-grande.bmp')
    ConvertTo-Png (Join-Path $marca 'instalador-pequena.svg') 55 58 (Join-Path $tmp 'pequena.png')
    ConvertTo-Bmp24 (Join-Path $tmp 'pequena.png') (Join-Path $marca 'instalador-pequena.bmp')

    # Pré-visualização do repositório / banner do README.
    ConvertTo-Png (Join-Path $marca 'social-preview.svg') 1280 640 (Join-Path $marca 'social-preview.png')

    Write-Output "Marca gerada: $icoSaida, $pngDir, instalador-*.bmp e social-preview.png."
}
finally {
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
