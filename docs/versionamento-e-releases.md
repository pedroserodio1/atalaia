# Versionamento, ramos e releases do Atalaia

Status: **aprovado em 10/11/2026** pelo Pedro. Vale para o agente (Windows) e para o app do tablet (Android), que sempre saem juntos, com o mesmo número de versão.

## 1. Resumo

| Canal | Ramo | Tag | Release no GitHub | Quem usa |
|---|---|---|---|---|
| Estável | `main` | `vX.Y.Z` | normal (a "latest") | todo mundo |
| Beta | `development` | `vX.Y.Z-beta.N` | pré-lançamento | quem escolheu o canal Beta |
| Trabalho | `beta/X.Y.Z` e sub-ramos | sem tag | nenhuma | só quem desenvolve |

Em uma frase: subiu para `main` com tag de versão é estável; subiu para `development` com tag de beta é beta; o trabalho de cada versão acontece em `beta/X.Y.Z` e só entra em `development` quando está pronto.

## 2. Como funciona o número da versão (SemVer 2.0.0)

Formato `MAJOR.MINOR.PATCH`, por exemplo `1.4.2`. Cada número só sobe. Quando um sobe, os da direita voltam a zero (`1.4.2` vira `1.5.0`).

- **PATCH** (`0.1.1` para `0.1.2`): correção compatível. Conserta bug, ajusta texto ou visual, melhora desempenho, atualiza dependência sem mudar comportamento. Não mexe no protocolo nem na configuração.
- **MINOR** (`0.1.2` para `0.2.0`): funcionalidade nova que continua compatível. Tela nova, fonte de dados nova, opção nova cujo padrão preserva o comportamento antigo, campo novo e opcional no protocolo.
- **MAJOR** (`0.9.0` para `1.0.0`, ou `1.x` para `2.0.0`): mudança que quebra. Protocolo incompatível (v1 para v2), configuração antiga que deixa de funcionar sem migração automática, funcionalidade removida, requisito mínimo maior (versão do Android ou do Windows).

Exemplos do projeto: corrigir a reconexão depois da suspensão do PC é patch. Adicionar a tela Deck é minor. Trocar o protocolo para v2 é major.

### Pré-lançamento (beta)

`-beta.N` marca uma versão candidata: `0.1.1-beta.1`, `0.1.1-beta.2` e assim por diante. Um beta vem antes da estável de mesmo número. A ordem de precedência é:

```
0.1.1-beta.1 < 0.1.1-beta.2 < 0.1.1-beta.10 < 0.1.1 < 0.1.2-beta.1 < 0.1.2
```

O `N` é comparado como número (`beta.10` vem depois de `beta.2`). Não usamos outros sufixos (alpha, rc) por enquanto. Builds locais, fora da esteira, saem com o sufixo `-dev` (por exemplo `0.1.1-dev`) e nunca são publicados.

### Antes da 1.0.0

Pelo SemVer, `0.y.z` é "desenvolvimento inicial": qualquer coisa pode mudar. Regra do projeto nessa fase: MINOR pode quebrar compatibilidade e traz o aviso nas notas; PATCH continua só para mudanças compatíveis. A `1.0.0` é declarada quando o Pedro considerar o produto e o protocolo estáveis.

Exceção registrada: a `0.1.1` (janela do agente, onboarding e atualização automática) foi escolhida pelo Pedro como primeira release pública. Pelo SemVer estrito, funcionalidade nova seria `0.2.0`; em `0.y.z` isso é permitido, então a `0.1.1` fica. Dali em diante vale a regra acima.

### Protocolo e versão do produto

O protocolo tem número próprio (hoje v1, em `docs/protocolo-v1.md`). Agente e app só conversam se falarem o mesmo protocolo: o agente fecha a conexão com o código 4400 quando a versão é incompatível. Versões do produto que usam o mesmo protocolo são compatíveis entre si.

- Quebrar o protocolo: protocolo v2 e MAJOR (antes da 1.0, MINOR com aviso nas notas).
- Acrescentar campo opcional: MINOR.
- Corrigir bug sem mexer no contrato: PATCH.

## 3. Ramos

```
feat/x ─┐
fix/y  ─┴─> beta/0.1.1 ──PR──> development ──PR──> main
 (squash)                (merge commit)       (merge commit)
                                  │ tag v0.1.1-beta.N     │ tag v0.1.1
                                  └─ release beta         └─ release estável

hotfix/0.1.2 (sai de main) ──PR──> main (tag v0.1.2) ──back-merge──> development
```

- **`main`**: só estável. Só recebe PR de `development` ou de `hotfix/*`. Tag `vX.Y.Z` (sem sufixo) gera a release estável.
- **`development`**: integração e beta. Recebe PR de `beta/X.Y.Z` e de correções menores. Tag `vX.Y.Z-beta.N` gera a release beta (pré-lançamento).
- **`beta/X.Y.Z`**: ramo de trabalho do ciclo da versão `X.Y.Z`. Sai de `development`. Sub-ramos opcionais (`feat/<nome>`, `fix/<nome>`, `docs/<nome>`, `chore/<nome>`) saem dele e voltam por PR.
- **`hotfix/X.Y.Z`**: correção urgente de uma estável. Sai de `main`, volta para `main` por PR (tag estável) e depois é trazida de volta para `development`.

Regras fixas:
- Nada de commit direto em `main` nem em `development`: tudo por PR, com CI verde.
- Tag estável só em commit de `main`. Tag beta só em commit de `development`.
- Tag publicada nunca é movida, reescrita nem apagada. Se saiu errado, sai uma versão nova.
- Beta `X.Y.Z-beta.N` só é aceito se `X.Y.Z` for maior que a última estável publicada.

### Estratégia de merge

| De | Para | Como |
|---|---|---|
| sub-ramo (`feat/...`, `fix/...`, `chore/...`) | `beta/X.Y.Z` | **squash** (um commit por assunto, em Conventional Commits) |
| `beta/X.Y.Z` | `development` | **merge commit** (preserva o rastro do ciclo) |
| `development` | `main` | **merge commit** |
| `main` (hotfix) | `development` | **merge commit** (back-merge) |

## 4. Fluxo de um ciclo (exemplo: 0.1.1)

1. `git switch development && git pull && git switch -c beta/0.1.1`. O primeiro commit abre o ciclo (`chore: abre o ciclo 0.1.1`) e põe `0.1.1` no arquivo `VERSION`.
2. Trabalhar com commits em Conventional Commits. Sub-ramos entram no ramo do ciclo por PR (squash).
3. Com o CI verde, PR de `beta/0.1.1` para `development` e merge (merge commit).
4. O **Pedro** cria a tag `v0.1.1-beta.1` em `development`. A esteira valida, constrói e gera a release beta com as notas. Bug achado no beta: `fix/...` para `development` e `v0.1.1-beta.2`.
5. Beta aprovado: gerar o `CHANGELOG.md` da versão (`scripts/changelog.ps1`), PR de `development` para `main`, merge, tag `v0.1.1` em `main`. A esteira gera a release estável.
6. Se `main` ficou com commit que `development` não tem (hotfix, changelog), fazer o back-merge de `main` em `development`.
7. Próximo ciclo: `beta/0.1.2` (ou `beta/0.2.0`) a partir de `development`.

## 5. Commits e notas de release

Commits seguem [Conventional Commits](https://www.conventionalcommits.org/pt-br/): `feat`, `fix`, `docs`, `refactor`, `perf`, `test`, `build`, `ci`, `chore`. Um `!` depois do tipo (ou `BREAKING CHANGE:` no corpo) marca quebra. Quem decide o número é o Pedro, e a esteira confere o que dá: se há `feat`, o número não pode ser só patch; se há quebra, o número precisa ser minor (antes da 1.0) ou major.

As notas saem dos commits com o **git-cliff** (`cliff.toml`, em português), agrupadas em **Novidades** (`feat`), **Correções** (`fix`) e **Outros**. Beta: desde a beta anterior da mesma versão (a primeira, desde a última estável). Estável: tudo desde a última estável, somando o que passou pelos betas. As notas usam o `cliff-notas.toml`, que não reconhece tag nenhuma como fronteira: a faixa vem pronta do `release.yml` e o job falha se um grupo sair repetido. O texto das notas aparece na tela Atualizações do agente. O `CHANGELOG.md` é gerado **só nas estáveis**, no PR `development` para `main`; a esteira confere que a seção da versão existe.

## 6. O que cada release publica

- `Atalaia-Agent-Setup-<versão>.exe`: instalador do agente (Windows x64, por usuário, .NET autocontido).
- `Atalaia-Tablet-<versão>.apk`: app do tablet, assinado com a chave de release.
- `SHA256SUMS.txt`: soma de verificação dos arquivos acima.
- Notas da release. Tag com sufixo `-beta.N` vira pré-lançamento; tag sem sufixo vira release normal.

## 7. Atualização automática (o que o agente faz)

- Canal **Estável**: só considera releases normais. Canal **Beta**: considera também os pré-lançamentos.
- Escolhe a maior versão pela precedência do SemVer (seção 2) e só oferece versão maior que a instalada. Nunca faz downgrade sozinho.
- Quem está num beta e vê a estável do mesmo número (`0.1.1-beta.2` para `0.1.1`) recebe a estável.
- Baixa os arquivos da release, confere o SHA-256 contra o `SHA256SUMS.txt` da mesma release e só então aplica. Falhou em qualquer etapa: a versão instalada continua.
- Só HTTPS, só do repositório configurado (`Updates:Repository`, padrão `pedroserodio1/atalaia`), sem enviar dado pessoal (apenas a versão no `User-Agent`).
- O APK do tablet é atualizado pelo agente por USB (`adb install -r`) ou pelo link da release.

## 8. Versão no código

- A tag é a fonte da versão publicada. A esteira injeta a versão completa nos dois builds.
- O arquivo `VERSION`, na raiz, guarda o `X.Y.Z` do ciclo atual. O agente o lê em `agent/Directory.Build.props` (`VersionPrefix`; o sufixo vem de `-p:VersionSuffix=`) e o app em `app/tablet/build.gradle.kts` (o sufixo vem de `-PappPre=`; o padrão está em `app/gradle.properties`, porque o Gradle não lê arquivo em `gradle.properties`). A esteira falha se o `X.Y.Z` da tag for diferente do `VERSION`, o que evita esquecer de abrir o ciclo.
- `versionCode` do Android precisa crescer sempre, inclusive de beta para estável: `((MAJOR * 1000 + MINOR) * 1000 + PATCH) * 100 + PRE`, com `PRE` = N do beta (1 a 98), 99 para estável e 0 para build local (`dev`). Exemplos: `0.1.1-beta.1` = 100101, `0.1.1` = 100199, `0.1.2-beta.1` = 100201. Limites: MINOR e PATCH até 999.
- Um build local (`dev`) tem `versionCode` menor que o de qualquer beta: para instalá-lo por cima de um beta no tablet é preciso `adb install -r -d`.

## 9. Assinatura e segredos

- **Android:** o APK de release precisa de uma chave própria (keystore). Ela e as senhas ficam nos secrets do GitHub (environment `release`), nunca no repositório, e o Pedro guarda uma cópia de segurança fora do computador de trabalho. Perder a chave significa nunca mais atualizar o app instalado por cima. Atenção: o APK que existe hoje no tablet foi assinado com a chave de debug; ao passar para a chave de release, o Android recusa a atualização por cima e o app precisa ser desinstalado uma vez (perde a configuração do app). O passo a passo está em [`assinatura-apk.md`](assinatura-apk.md).
- **Windows:** **sem assinatura de código** nesta fase (decisão do Pedro), então o SmartScreen avisa na instalação ("Mais informações" e "Executar assim mesmo"). Como a integridade da atualização automática depende só do SHA-256 da mesma release, quem controlasse a release poderia trocar o arquivo e a soma; por isso a release só é publicada pela esteira, a partir de tag criada pelo Pedro.

## 10. Proteção de ramos e CI

- `main` e `development`: PR obrigatório, CI verde obrigatório (checks `agente`, `app` e `scripts`), sem force push, sem apagar o ramo. Tags `v*`: sem atualizar nem apagar. O Pedro aplica as proteções na interface do GitHub ou por `gh api`.
- CI em todo PR e push (`main`, `development`, `beta/**`): testes do agente (`dotnet test`), testes do app (`./gradlew :core:test`), build dos dois e testes dos scripts.
- A esteira de release roda ao subir uma tag `v*`, valida o formato e o ramo da tag, constrói tudo, gera as notas e cria a release. Também existe um modo de ensaio (`workflow_dispatch` com `dry_run`) que constrói sem publicar. Como o GitHub só permite `workflow_dispatch` de um workflow que existe na branch padrão, `release.yml` entra em `main` por um PR mínimo antes da primeira beta; o ensaio usa `gh workflow run release.yml --ref development -f dry_run=true`.

### Comandos das proteções (o Pedro aplica; só depois que o CI existir no repositório)

Os checks obrigatórios têm o nome dos jobs de `ci.yml`: `agente`, `app` e `scripts`. Com `enforce_admins: true` nem o dono empurra direto
em `main`/`development`; troque para `false` se quiser uma saída de emergência.

```bash
for ramo in main development; do
gh api -X PUT "repos/pedroserodio1/atalaia/branches/$ramo/protection" --input - <<'JSON'
{
  "required_status_checks": { "strict": true, "contexts": ["agente", "app", "scripts"] },
  "enforce_admins": true,
  "required_pull_request_reviews": { "required_approving_review_count": 0 },
  "restrictions": null,
  "allow_force_pushes": false,
  "allow_deletions": false
}
JSON
done
```

Tags `v*` (criar é permitido; mover e apagar, não):

```bash
gh api -X POST repos/pedroserodio1/atalaia/rulesets --input - <<'JSON'
{
  "name": "tags de versao",
  "target": "tag",
  "enforcement": "active",
  "conditions": { "ref_name": { "include": ["refs/tags/v*"], "exclude": [] } },
  "rules": [ { "type": "deletion" }, { "type": "update" } ]
}
JSON
```

## 11. Quando dá errado

- Release com defeito: não apagar a tag. Publicar a versão seguinte (patch ou próximo beta) e, se preciso, marcar a ruim como pré-lançamento para o atualizador parar de oferecê-la.
- Tag com formato errado ou no ramo errado: a esteira recusa e nada é publicado.
