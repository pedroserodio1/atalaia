# Atalaia: painel de tablet para o PC

- Responda sempre em português.
- Antes de escrever código, leia `CONTRIBUTING.md`, `docs/protocolo-v1.md` (o contrato; manda sobre qualquer outro documento) e
  `docs/marco-1.md`, além do README da parte em que for mexer.
- Código do agente em `agent/` (C#), do app em `app/` (Kotlin). As regras de cada parte estão em `.claude/rules/`
  e os fluxos prontos em `.claude/skills/` (`/novo-plugin-agente`, `/fonte-no-app`, `/verificar-contribuicao`).
- `probe_tests/` é a sondagem antiga (PcProbe): somente leitura, não edite. Não leia nem imprima `.env`.
- Dúvida ou conflito entre os documentos e o código que funciona: pergunte antes de decidir.
- Commits: pode commitar por conta própria toda mudança concluída que faça sentido como um commit (regras em `.claude/rules/projeto.md`, seção Git).
  Conventional Commits, sem linhas de atribuição. Push, PR e tag continuam só quando pedido.
- Ramos, versões e releases: leia `docs/versionamento-e-releases.md`. Resumo: `main` = estável (tag `vX.Y.Z`), `development` = beta
  (tag `vX.Y.Z-beta.N`), trabalho em `beta/X.Y.Z` e sub-ramos (`feat/`, `fix/`, `docs/`, `chore/`), tudo por PR com CI verde. Merge:
  squash para `beta/X.Y.Z`, merge commit para `development` e `main`. Versão: SemVer (patch = correção compatível, minor = recurso
  novo compatível, major = quebra; antes da 1.0 o minor pode quebrar, com aviso). O `X.Y.Z` fica no arquivo `VERSION`. Tag só o mantenedor
  cria; nunca mova nem apague uma tag.
