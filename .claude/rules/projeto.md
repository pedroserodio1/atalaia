# Regras gerais do projeto Atalaia

Valem para qualquer mudança no repositório.

## Antes de escrever código
- Leia o `CONTRIBUTING.md`, o `docs/protocolo-v1.md` (o contrato entre agente e app; **manda sobre qualquer outro documento**) e o
  README da parte que você vai mexer (`agent/README.md` ou `app/README.md`).
- Dúvida, ou conflito entre um documento e o código que funciona: **pergunte antes de decidir**. Não presuma regra de negócio,
  formato de dado nem restrição de infraestrutura.
- Uma mudança que altera o contrato (`docs/protocolo-v1.md`) precisa ser combinada com o mantenedor antes.

## Onde fica o quê
- `agent/` agente do PC (C#, .NET 10, Windows). `app/` app do tablet (Kotlin). `docs/` protocolo e plano.
- **`probe_tests/` é a sondagem antiga: somente leitura.** Não edite nada lá, e nunca leia nem imprima um arquivo `.env`.
- `docs/prompt-original.md` é registro histórico: não edite.
- `agent/tools/test-client.html` é cópia exata de `docs/test-client.html`: não altere uma sem a outra, nem corrija a página
  sozinho; avise o mantenedor.

## Idioma e estilo
- Documentação, comentários, mensagens de erro, textos de interface e commits em **português do Brasil**.
- Mantenha o estilo do arquivo em que está mexendo. Comentário só onde o *porquê* não é óbvio.
- Mudança pequena e focada: não reformate nem renomeie o que não faz parte do assunto.

## Licença
- Todo arquivo novo de código (`.cs`, `.kt`, `.kts`) começa com:
  `// SPDX-License-Identifier: GPL-3.0-or-later` e `// Copyright (C) <ano> <nome de quem escreveu>`.
- Dependência nova: versão **fixa** (sem `*` nem faixa), licença compatível com a GPL-3.0 (MIT, BSD, Apache-2.0), e uma linha em
  `THIRD-PARTY-NOTICES.md`. Na dúvida sobre a licença, não adicione.

## Segurança e privacidade (inegociável)
- **Nada pessoal no código nem na documentação:** caminho de usuário, IP, domínio, nome de máquina, número de série, e-mail,
  coordenadas. Tudo vem de configuração do usuário. Exemplos usam valores falsos (`192.168.0.12`, `FAKESERIAL0123456`).
- **Nunca** coloque segredo (token, chave de API, `.env`) no repositório, em teste, em log, em issue ou em PR.
- O agente só **lê** o sistema do usuário. O que ele faz de efetivo são as ações declaradas por cada plugin, e nada além.
- O app só aceita endereços de loopback e de rede local, e o código de acesso nunca é exibido de volta.

## Git
- Commits em Conventional Commits, em português (`feat(agent): ...`, `fix(app): ...`, `docs: ...`, `test: ...`).
- **Sem linhas de atribuição** nas mensagens (`Co-Authored-By`, "Generated with ...").
- Não commite sem o usuário pedir. Nunca altere a configuração do git nem use `push --force` em branch compartilhada.
- Nunca inclua `appsettings.Local.json`, `.env`, `local.properties`, chaves ou builds.
- Ramos, versões e releases seguem `docs/versionamento-e-releases.md`: trabalho em `beta/X.Y.Z` (sub-ramos por squash), `development`
  recebe o ciclo (merge commit) e gera betas (`vX.Y.Z-beta.N`), `main` só recebe estável (`vX.Y.Z`). Nada de commit direto em
  `main` nem em `development`. **Nunca crie, mova nem apague tag**: quem cria a tag é o mantenedor.

## Verificar antes de dizer que terminou
- `dotnet test` (em `agent/`) e `./gradlew :core:test` (em `app/`) precisam passar. Se não conseguiu rodar algo, diga isso, e
  não afirme que passou.
- Teste que depende de hardware, rede ou tablet que você não tem: diga o que **não** foi verificado.
