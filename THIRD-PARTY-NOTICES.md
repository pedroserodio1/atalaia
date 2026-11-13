# Componentes de terceiros

O Atalaia é distribuído sob a GPL-3.0 ou posterior (veja [`LICENSE`](LICENSE) e [`NOTICE`](NOTICE)). Ele usa os componentes
abaixo, cada um sob a sua própria licença, todas compatíveis com a GPL-3.0. As licenças foram conferidas nos metadados dos
próprios pacotes (`nuspec` e `pom`) nas versões fixadas no projeto.

Esta lista cobre as **dependências diretas**. As transitivas (puxadas por elas) seguem as licenças dos respectivos pacotes.

## Agente do PC (.NET)

| Componente | Versão | Licença | Uso |
|---|---|---|---|
| [NAudio](https://naudio.github.io/NAudio/) | 3.3.0 | MIT | Volume e sessões de áudio do Windows |
| Microsoft.Data.Sqlite | 10.0.14 | MIT | Leitura (somente leitura) do banco do nobreak |
| Microsoft.Extensions.Configuration (Abstractions, Binder) | 10.0.14 | MIT | Configuração |
| Microsoft.Extensions.Logging.Abstractions | 10.0.14 | MIT | Log |
| System.Diagnostics.PerformanceCounter | 10.0.14 | MIT | CPU e memória |
| [Serilog.AspNetCore](https://github.com/serilog/serilog-aspnetcore) | 10.0.0 | Apache-2.0 | Log |
| [Serilog.Sinks.File](https://github.com/serilog/serilog-sinks-file) | 7.0.0 | Apache-2.0 | Arquivo de log |
| [QRCoder](https://github.com/codebude/QRCoder) | 1.8.1 | MIT | QR do link da release no passo "App no tablet" (gera SVG localmente) |
| [Microsoft.Web.WebView2](https://aka.ms/webview) (SDK) | 1.0.4314.35 | BSD-3-Clause | Janela do agente (a página local roda num WebView2) |
| .NET e ASP.NET Core (runtime) | 10 | MIT | Plataforma |

O **WebView2 Runtime** (o navegador embutido que a janela usa) é um componente do Windows da Microsoft, instalado à parte e **não redistribuído**
pelo Atalaia. Sem ele a janela não abre, e o agente segue funcionando na bandeja.

Só nos testes do agente (não fazem parte do programa distribuído): xunit 2.9.3 (Apache-2.0), xunit.runner.visualstudio 3.1.4
(Apache-2.0), Microsoft.NET.Test.Sdk 17.14.1 (MIT) e coverlet.collector 6.0.4 (MIT).

## App do tablet (Kotlin / Android)

| Componente | Versão | Licença | Uso |
|---|---|---|---|
| AndroidX (core-ktx, activity-compose, lifecycle-runtime-compose) | 1.19.1 / 1.13.0 / 2.11.0 | Apache-2.0 | Base do app |
| Jetpack Compose (BOM, ui, foundation) | 2026.09.00 | Apache-2.0 | Interface |
| [OkHttp](https://square.github.io/okhttp/) | 4.12.0 | Apache-2.0 | WebSocket e HTTP |
| kotlinx.serialization (json) | 1.12.0 | Apache-2.0 | JSON |
| kotlinx.coroutines (core) | 1.11.0 | Apache-2.0 | Concorrência |
| Kotlin (biblioteca padrão) | 2.4.21 | Apache-2.0 | Linguagem |

Só nos testes do app (não entram no APK): JUnit Jupiter 6.1.3 (**EPL-2.0**), kotlinx-coroutines-test 1.11.0 (Apache-2.0) e
OkHttp MockWebServer 4.12.0 (Apache-2.0). O JUnit usa a EPL-2.0, que tem restrições de combinação com a GPL, e por isso ele só é
usado para rodar os testes: não é distribuído junto com o programa.

## Ícones (MIT)

- **Tabler Icons** v3.19.0, Copyright (c) 2020-2024 Paweł Kuna ([tabler/tabler-icons](https://github.com/tabler/tabler-icons)), licença MIT.
  Só seis ícones do estilo outline (microfone, microfone cortado, fones, fones cortados, volume e pessoas) têm o desenho copiado em
  `app/tablet/src/main/kotlin/org/atalaia/app/ui/Icons.kt`; não há dependência da biblioteca. A licença original está em
  [`app/third-party/tabler-icons-LICENSE.txt`](app/third-party/tabler-icons-LICENSE.txt).

## Fontes (SIL Open Font License 1.1)

As fontes ficam em [`app/third-party/`](app/third-party/) com as licenças originais. A janela do agente usa as mesmas fontes, copiadas em
`agent/src/Atalaia.Agent.Host/Ui/web/fonts/` junto com as licenças (e carregadas do disco, sem Google Fonts).

- **Manrope**, Copyright 2018 The Manrope Project Authors ([googlefonts/manrope](https://github.com/googlefonts/manrope)).
  **Os arquivos `manrope_500.ttf`, `manrope_700.ttf` e `manrope_800.ttf` são versões modificadas**: instâncias estáticas
  geradas da fonte variável oficial (com `fontTools`, `varLib.instancer`). A fonte não declara Nome de Fonte Reservado, então a
  OFL permite a modificação; a licença original acompanha os arquivos.
- **IBM Plex Mono** (peso Medium, sem modificação), Copyright © 2017 IBM Corp., com o Nome de Fonte Reservado "Plex".

A OFL permite usar, estudar, modificar e redistribuir as fontes junto com software, mas não vendê-las isoladamente.

A logo e as imagens de `assets/brand/` (geradas por `scripts/marca/gerar-svgs.py`) trazem o texto como **contornos** de Manrope 800/700 e IBM Plex Mono
Medium, sem embutir a fonte: a OFL permite usar os contornos em arte, e a licença original acompanha as fontes em `app/third-party/`.

## Ferramentas de build e release (não fazem parte do código do programa)

Rodam só na esteira (`.github/workflows/`) ou na máquina de quem constrói a release. Nenhuma delas é redistribuída como biblioteca,
com exceção do módulo de instalação do Inno Setup, que acompanha o instalador gerado.

| Ferramenta | Versão | Licença | Uso |
|---|---|---|---|
| [Inno Setup](https://jrsoftware.org/isinfo.php) | 6.x (a do runner) | Licença do Inno Setup (livre, permite uso e redistribuição do instalador gerado, inclusive em software livre) | Gera `Atalaia-Agent-Setup-<versão>.exe`; o instalador inclui o módulo de instalação do Inno |
| [git-cliff](https://github.com/orhun/git-cliff) | 2.14.2 (fixada por soma em `scripts/requisitos-cliff.txt`) | MIT ou Apache-2.0 | Gera as notas de release e o `CHANGELOG.md` a partir dos Conventional Commits |
| GitHub Actions oficiais (`actions/checkout`, `setup-dotnet`, `setup-java`, `setup-python`, `upload-artifact`, `download-artifact`) | v4/v5 | MIT | Esteira de CI e release |
