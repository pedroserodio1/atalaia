# Contribuindo com o Atalaia

Obrigado por querer ajudar! O Atalaia tem duas partes: o **agente** do PC (C#, Windows) e o **app** do tablet (Kotlin,
Android). A forma mais comum de contribuir é com um **plugin do agente**: uma nova fonte de dados (ou de ações) que aparece
no tablet. Este guia explica como.

Em dúvida, abra uma *issue* antes de escrever código. É mais rápido do que refazer um pull request.

## O que é bem-vindo

- **Plugins novos** do agente (uma fonte de dados nova, ou ações novas numa fonte existente).
- Telas ou modelos novos no app para uma fonte que o agente já publica.
- Correções, testes e documentação.

**Converse antes** (abra uma issue) se a ideia mexe no contrato entre agente e app (`docs/protocolo-v1.md`), traz uma
dependência nova, ou está na lista "fora do escopo" do [README](README.md) (Deck, Wake-on-LAN, modo kiosk, etc.).

## Idioma

Documentação, comentários, mensagens de erro, textos da interface e mensagens de commit são em **português do Brasil**.
Nomes no código seguem o arquivo em que você está mexendo.

## Preparando o ambiente

Requisitos: Windows 11 e o SDK .NET 10 (agente); JDK 17 ou mais novo e o Android SDK com a plataforma 37 (app).

```bash
# agente (a partir de agent/)
dotnet test
dotnet run --project src/Atalaia.Agent.Host -- --console --simulate   # dados falsos, sem precisar de hardware

# app (a partir de app/; informe o SDK em ANDROID_HOME ou em local.properties)
./gradlew :core:test :tablet:assembleDebug
```

O modo `--simulate` liga todas as fontes com dados falsos. Dá para ver o resultado em `agent/tools/painel.html` (um painel
de navegador que conversa com o agente) sem tablet nenhum. Detalhes em [`agent/README.md`](agent/README.md) e
[`app/README.md`](app/README.md).

## Contribuindo com um plugin do agente

Um plugin é um projeto C# que implementa `IPlugin` (em `agent/src/Atalaia.Agent.Core/Contracts.cs`) e referencia **só** o
`Atalaia.Agent.Core`. O `Atalaia.Agent.Host` é a raiz de composição: lá o plugin é registrado. O agendador chama o seu
`CollectAsync` no intervalo configurado e só envia `update` ao tablet quando o dado muda (ou a cada 10 s sem mudança).

O caminho completo, em ordem:

1. **Abra uma issue** descrevendo a fonte: qual dado, de onde vem, que campos, se há ações e se precisa de credencial.
2. **Crie o projeto** `agent/src/Atalaia.Agent.Plugins.<Nome>/` com a classe do plugin (use o `Weather` como modelo).
3. **Registre**: `Atalaia.Agent.sln`, `ProjectReference` no `Atalaia.Agent.Host.csproj` e a linha em `PluginRegistry.cs`.
4. **Configure**: a entrada em `agent/appsettings.json` (`enabled` e `intervalMs`). Segredos ficam só em variáveis de ambiente
   (`ATALAIA_Plugins__<id>__<chave>`), documentadas em `agent/.env.example`, nunca no `appsettings.json`.
5. **Simule**: um plugin simulado com o mesmo `id` e o mesmo formato, em `Atalaia.Agent.Plugins.Simulator`, para que todos
   possam ver a fonte sem o hardware dela.
6. **Teste**: a lógica de leitura/conversão como um método puro e estático, com testes xUnit que não dependem de rede,
   hardware nem do estado do seu Windows.
7. **Documente o formato**: a seção da fonte em `docs/protocolo-v1.md` ("Fontes opcionais") e a configuração em
   `agent/README.md`. Acrescentar uma fonte é um acréscimo ao protocolo v1; **nunca** renomeie nem mude o significado de
   um campo que já existe.
8. Rode a verificação (abaixo) e abra o pull request.

**Regras do código de um plugin** (o resumo; a lista completa está em
[`.claude/rules/plugins-agente.md`](.claude/rules/plugins-agente.md)):

- Somente leitura no sistema do usuário. Ações só as que o plugin declara em `Actions`; nunca um comando livre nem um caminho
  vindo do cliente.
- Nada pessoal no código (caminho de usuário, IP, domínio, nome de dispositivo): tudo vem de configuração.
- Nunca registrar no log token, segredo, título de janela ou conteúdo de notificações.
- Todo número e data passam por `CultureInfo.InvariantCulture` (o Windows em pt-BR usa vírgula decimal).
- Campo sem leitura vai como `null`. Falhar não derruba o agente: lance exceção e o agendador marca o plugin com erro e tenta de
  novo.

### Com o Claude Code

O repositório já traz regras e *skills* para quem usa o [Claude Code](https://claude.com/claude-code). Abrindo o projeto
nele, as regras de `.claude/rules/` são carregadas sozinhas, e estas skills ajudam a seguir o caminho acima:

| Skill | Para quê |
|---|---|
| `/novo-plugin-agente <nome>` | Cria o esqueleto completo de um plugin do agente (projeto, registro, simulador, testes, docs) |
| `/fonte-no-app <id>` | Mostra uma fonte nova no app do tablet (modelo, assinatura, tela, testes) |
| `/verificar-contribuicao` | Roda os testes e confere cabeçalhos de licença, segredos e dados pessoais antes do PR |

Elas são um atalho, não uma exigência: o resultado precisa passar nos mesmos testes e seguir as mesmas regras.

## Contribuindo com o app

O app tem o núcleo em `app/core` (Kotlin/JVM puro: protocolo, conexão, estado, regras) e a interface em `app/tablet`
(Compose). Regras principais:

- O tablet alvo é um Android 8.1 (API 27), 32 bits, com ~1,4 GB de RAM. Nada de API do Java 11 ou mais novo no `:core` (ele é
  compilado para Java 8 e o erro aparece na compilação), e nada de animação contínua, blur ou sombra pesada.
- A lógica vai no `:core`, com teste na JVM; a tela só desenha o estado.
- Textos da interface ficam em `ui/Texts.kt`, em português.
- Um botão só existe se o `hello` do agente declarou a ação.

Regras completas em [`.claude/rules/app-kotlin.md`](.claude/rules/app-kotlin.md).

## Verificação antes do pull request

- [ ] `dotnet test` (em `agent/`) e `./gradlew :core:test` (em `app/`) passam.
- [ ] Arquivos novos de código têm o cabeçalho de licença (abaixo).
- [ ] Nenhum segredo, token, `.env`, caminho de usuário, IP ou número de série de aparelho no diff.
- [ ] Se acrescentou uma dependência: versão fixa, licença compatível com a GPL-3.0 e linha em `THIRD-PARTY-NOTICES.md`.
- [ ] Se mexeu em protocolo, configuração ou comportamento visível: a documentação foi atualizada.
- [ ] Se mexeu em algo que depende de hardware ou de tablet: descreva no PR o que você testou de verdade e o que não testou.

A skill `/verificar-contribuicao` (ou o script `.claude/skills/verificar-contribuicao/verificar.ps1`) faz as checagens
mecânicas.

## Ramos, versões e releases

O detalhe está em [`docs/versionamento-e-releases.md`](docs/versionamento-e-releases.md). Em resumo:

| Canal | Ramo | Tag | Release |
|---|---|---|---|
| Estável | `main` | `vX.Y.Z` | normal |
| Beta | `development` | `vX.Y.Z-beta.N` | pré-lançamento |
| Trabalho | `beta/X.Y.Z` e sub-ramos (`feat/`, `fix/`, `docs/`, `chore/`) | sem tag | nenhuma |

- Abra o PR **para o ramo do ciclo** (`beta/X.Y.Z`, o mais recente), não para `main` nem `development`. Quem integra o ciclo é o mantenedor.
- Merge: sub-ramo para `beta/X.Y.Z` por *squash*; `beta/X.Y.Z` para `development` e `development` para `main` por *merge commit*.
- Versão (SemVer): **patch** é correção compatível; **minor** é recurso novo compatível (plugin, tela, campo opcional no protocolo);
  **major** é quebra (protocolo incompatível, configuração que deixa de funcionar, requisito mínimo maior). Antes da 1.0, o minor
  pode quebrar, com aviso nas notas. **Beta** (`-beta.N`) é uma candidata: vem antes da estável de mesmo número.
- Só o mantenedor cria tags e releases. Tag publicada nunca é movida nem apagada.

## Commits e pull requests

- Mensagens no padrão [Conventional Commits](https://www.conventionalcommits.org/pt-br/), em português: `feat(agent): ...`,
  `fix(app): ...`, `docs: ...`, `test: ...`, `refactor: ...`.
- Sem linhas de atribuição automática nas mensagens (`Co-Authored-By`, "Generated with ...").
- Um pull request por assunto, pequeno e fácil de revisar. Descreva o que muda, por quê e como testou.
- Não mexa em `probe_tests/` (a sondagem antiga, somente leitura) nem em `docs/prompt-original.md` (registro histórico).

## Licença das contribuições

O Atalaia é distribuído sob a **GNU GPL, versão 3 ou posterior** ([`LICENSE`](LICENSE), [`NOTICE`](NOTICE)). Ao enviar uma
contribuição você concorda que ela seja licenciada sob esses mesmos termos.

Todo arquivo novo de código (`.cs`, `.kt`, `.kts`) começa com:

```
// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) <ano> <seu nome>
```

Você mantém o seu copyright sobre o que escreve. Bibliotecas de terceiros só entram se a licença for compatível com a
GPL-3.0 (MIT, BSD e Apache-2.0 servem; licenças proprietárias ou com restrição de uso, não) e devem ser listadas em
[`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

## Segurança

Encontrou uma vulnerabilidade (por exemplo, uma forma de executar algo no PC sem autorização ou de burlar o código de acesso)?
**Não descreva a falha numa issue pública.** Se a aba *Security* do repositório mostrar o botão *Report a vulnerability*,
use-o. Se não mostrar, abra uma issue pedindo apenas um contato privado com o mantenedor, **sem detalhes técnicos**, e combine
o envio por lá.

Nunca inclua em issues, PRs ou logs: o código de acesso do agente, chaves de API, o conteúdo do seu `.env` ou o IP do seu PC.
