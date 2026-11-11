// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

using System.Drawing;

namespace Atalaia.Agent.Host.Ui;

/// <summary>O ícone da marca (<c>Assets/atalaia.ico</c>, embutido no programa), no tamanho pedido: o .ico traz 16, 24, 32, 48, 64 e 256 px.</summary>
internal static class BrandIcon
{
    public static Icon Load(Size size)
    {
        using var stream = typeof(BrandIcon).Assembly.GetManifestResourceStream("atalaia.ico")
            ?? throw new InvalidOperationException("O ícone da marca não está embutido no programa.");
        return new Icon(stream, size);
    }

    /// <summary>Ícone da bandeja: o tamanho pequeno do sistema (16 px a 100% de escala, maior em telas com mais zoom).</summary>
    public static Icon Tray() => Load(SystemInformation.SmallIconSize);

    /// <summary>Ícone da janela e da barra de tarefas.</summary>
    public static Icon Window() => Load(SystemInformation.IconSize);
}
