# SPDX-License-Identifier: GPL-3.0-or-later
# Copyright (C) 2026 Pedro Serôdio
"""
Gera os SVGs da marca do Atalaia em assets/brand/ (símbolo, ícones, logos, imagens do instalador e do repositório).

Os textos viram curvas (contornos das fontes OFL Manrope e IBM Plex Mono que já estão em app/tablet/src/main/res/font/),
para o SVG não depender de nenhuma fonte instalada. Só precisa ser rodado quando o desenho mudar:
    python -m venv .venv && .venv/Scripts/pip install fonttools && .venv/Scripts/python scripts/marca/gerar-svgs.py
(fonttools é só uma ferramenta de desenho, licença MIT; não faz parte do programa.) Depois rode scripts/gerar-marca.ps1.
"""
from pathlib import Path

from fontTools.pens.svgPathPen import SVGPathPen
from fontTools.pens.transformPen import TransformPen
from fontTools.ttLib import TTFont

RAIZ = Path(__file__).resolve().parents[2]
SAIDA = RAIZ / "assets" / "brand"
FONTES = RAIZ / "app" / "tablet" / "src" / "main" / "res" / "font"

GRAFITE, PAINEL, BORDA = "#0B0D10", "#14171C", "#232830"
MENTA, TEXTO, TEXTO2 = "#6EE7B7", "#EEF1F5", "#8A94A3"
MENTA_CLARO = "#0E9F76"   # a menta em fundo claro precisa de mais contraste (peça gráfica, 3:1)

# Fontes usadas só para os contornos do texto.
_cache = {}


def fonte(nome):
    if nome not in _cache:
        f = TTFont(FONTES / f"{nome}.ttf")
        _cache[nome] = (f, f.getGlyphSet(), f.getBestCmap(), f["head"].unitsPerEm)
    return _cache[nome]


def num(v):
    return f"{v:.2f}".rstrip("0").rstrip(".")


def texto(nome, s, tamanho, x, y_base, cor, tracking_em=0.0):
    """Texto como um único <path> (contornos já posicionados). Devolve (svg, largura)."""
    f, gs, cmap, upm = fonte(nome)
    esc = tamanho / upm
    pen = SVGPathPen(gs, ntos=num)
    cursor = 0.0
    for ch in s:
        g = cmap[ord(ch)]
        tp = TransformPen(pen, (esc, 0, 0, -esc, x + cursor * esc, y_base))
        gs[g].draw(tp)
        cursor += f["hmtx"][g][0] + tracking_em * upm
    largura = (cursor - tracking_em * upm) * esc
    return f'<path fill="{cor}" d="{pen.getCommands()}"/>', largura


def torre(corpo=TEXTO, telhado=MENTA, corte=GRAFITE, pequena=False):
    """A torre de vigia no espaço de 64 x 64 (ocupa x 17..47, y 9..55). 'pequena' é a versão para 16 e 24 px."""
    if pequena:
        return (
            f'<polygon points="15,20 32,7 49,20" fill="{telhado}"/>'
            f'<rect x="17" y="20" width="30" height="12" rx="2.5" fill="{corpo}"/>'
            f'<circle cx="32" cy="26" r="3.4" fill="{corte}"/>'
            f'<polygon points="24,32 40,32 45,57 19,57" fill="{corpo}"/>'
        )
    return (
        f'<polygon points="17,19 32,9 47,19" fill="{telhado}"/>'
        f'<rect x="19" y="19" width="26" height="11" rx="2.5" fill="{corpo}"/>'
        f'<circle cx="32" cy="24.5" r="2.6" fill="{corte}"/>'
        f'<polygon points="25,30 39,30 43,55 21,55" fill="{corpo}"/>'
        f'<path d="M29 55 V47 a3 3 0 0 1 6 0 V55 Z" fill="{corte}"/>'
    )


def azulejo(fundo=PAINEL, borda=BORDA):
    return f'<rect x="2" y="2" width="60" height="60" rx="14" fill="{fundo}" stroke="{borda}" stroke-width="2"/>'


def svg(w, h, conteudo, titulo, vb=None):
    vb = vb or f"0 0 {num(w)} {num(h)}"
    return (
        f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{vb}" width="{num(w)}" height="{num(h)}" role="img" aria-label="{titulo}">'
        f"<title>{titulo}</title>{conteudo}</svg>\n"
    )


def grava(nome, conteudo):
    SAIDA.mkdir(parents=True, exist_ok=True)
    (SAIDA / nome).write_text(conteudo, encoding="utf-8", newline="\n")
    print("gerado", nome)


def torre_em(x, y, escala, **kw):
    """A torre com o canto superior esquerdo da caixa (17, 9) em (x, y), na escala dada."""
    return f'<g transform="translate({num(x)} {num(y)}) scale({num(escala)}) translate(-17 -9)">{torre(**kw)}</g>'


def main():
    # Ícone do app: a torre sobre o azulejo grafite arredondado (é o ícone do .exe, do instalador e do launcher do tablet).
    grava("icone-app.svg", svg(64, 64, azulejo() + torre(), "Atalaia"))
    grava("icone-pequeno.svg", svg(64, 64, azulejo() + torre(pequena=True), "Atalaia (versão para 16 e 24 px)"))
    # Símbolo sozinho (fundo escuro): só a torre, sem azulejo.
    grava("simbolo.svg", svg(30, 46, torre_em(0, 0, 1), "Atalaia"))

    # Logo horizontal: torre + palavra (fundo escuro e fundo claro).
    for nome, corpo, telhado, corte, cor_txt in (
        ("logo-horizontal.svg", TEXTO, MENTA, GRAFITE, TEXTO),
        ("logo-horizontal-claro.svg", PAINEL, MENTA_CLARO, "#F4F2EC", PAINEL),
    ):
        t, larg = texto("manrope_800", "Atalaia", 44, 44, 46, cor_txt, tracking_em=-0.02)
        w = 44 + larg
        conteudo = torre_em(0, 0, 1, corpo=corpo, telhado=telhado, corte=corte) + t
        grava(nome, svg(w + 8, 54, conteudo, "Atalaia", vb=f"-4 -4 {num(w + 8)} 54"))

    # Logo vertical: torre sobre a palavra.
    t, larg = texto("manrope_800", "Atalaia", 40, 0, 0, TEXTO, tracking_em=-0.02)
    w = max(larg, 30)
    cx = w / 2
    conteudo = torre_em(cx - 15, 0, 1) + texto("manrope_800", "Atalaia", 40, (w - larg) / 2, 46 + 14 + 29, TEXTO, tracking_em=-0.02)[0]
    grava("logo-vertical.svg", svg(w + 8, 46 + 14 + 29 + 12, conteudo, "Atalaia", vb=f"-4 -4 {num(w + 8)} {num(46 + 14 + 29 + 12)}"))

    # Assistente do instalador (Inno): 164 x 314 e 55 x 58.
    t, larg = texto("manrope_800", "Atalaia", 30, 0, 0, TEXTO, tracking_em=-0.02)
    esc = 1.9
    x_torre = (164 - 30 * esc) / 2
    conteudo = (
        f'<rect width="164" height="314" fill="{GRAFITE}"/>'
        + torre_em(x_torre, 78, esc)
        + texto("manrope_800", "Atalaia", 30, (164 - larg) / 2, 78 + 46 * esc + 44, TEXTO, tracking_em=-0.02)[0]
        + f'<rect x="52" y="{num(78 + 46 * esc + 62)}" width="60" height="3" rx="1.5" fill="{MENTA}"/>'
    )
    grava("instalador-grande.svg", svg(164, 314, conteudo, "Atalaia"))
    esc = 52 / 64
    grava(
        "instalador-pequena.svg",
        svg(55, 58, f'<rect width="55" height="58" fill="#FFFFFF"/><g transform="translate(1.5 3) scale({num(esc)})">{azulejo()}{torre()}</g>', "Atalaia"),
    )

    # Imagem de pré-visualização do repositório (1280 x 640): também é o banner do README.
    esc = 4.2
    t, larg = texto("manrope_800", "Atalaia", 190, 0, 0, TEXTO, tracking_em=-0.02)
    grupo_w = 30 * esc + 56 + larg
    x0 = (1280 - grupo_w) / 2          # o grupo (torre + palavra) fica centrado na horizontal
    y0 = (640 - (46 * esc + 156)) / 2 - 8   # e na vertical (a linha de baixo pesa menos, daí o ajuste)
    base = y0 + 46 * esc
    x_txt = x0 + 30 * esc + 56
    conteudo = f'<rect width="1280" height="640" fill="{GRAFITE}"/>' + torre_em(x0, y0, esc)
    conteudo += texto("manrope_800", "Atalaia", 190, x_txt, base, TEXTO, tracking_em=-0.02)[0]
    conteudo += texto("manrope_700", "O painel do seu PC, no tablet.", 46, x0, base + 92, TEXTO2)[0]
    conteudo += texto("ibmplexmono_medium", "código aberto · GPL-3.0 ou posterior", 26, x0, base + 156, MENTA)[0]
    grava("social-preview.svg", svg(1280, 640, conteudo, "Atalaia: o painel do seu PC, no tablet"))


if __name__ == "__main__":
    main()
