## Resumo

<!-- O que muda e por quê, em poucas linhas. Link para a issue, se houver. -->

## Plano de teste

<!-- O que você rodou e o que NÃO conseguiu verificar (hardware, tablet, rede). -->
- [ ] `dotnet test` (em `agent/`)
- [ ] `./gradlew :core:test` (em `app/`), se mexeu no app

## Checklist

- [ ] Commits em Conventional Commits, em português, sem linhas de atribuição
- [ ] Branch certo: sub-ramo para `beta/X.Y.Z` (squash), ou correção de beta direto para `development`
- [ ] Nada pessoal (IP, caminho de usuário, nome de máquina, e-mail) nem segredo no código, em teste ou nas descrições
- [ ] Mudou o contrato (`docs/protocolo-v1.md`)? Combinei com o mantenedor antes
- [ ] Arquivo novo de código com o cabeçalho SPDX; dependência nova com versão fixa e linha em `THIRD-PARTY-NOTICES.md`
