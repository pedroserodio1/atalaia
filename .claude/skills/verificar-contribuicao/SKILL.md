---
name: verificar-contribuicao
description: Confere uma contribuição ao Atalaia antes do pull request - cabeçalho de licença nos arquivos novos, segredos e dados pessoais no diff, arquivos que não devem ir, e roda os testes. Use quando o usuário disser que terminou, ou antes de abrir PR.
---

# Verificar contribuição

1. Rode a verificação automática (a partir da raiz do repositório):

```powershell
powershell -NoProfile -File .claude/skills/verificar-contribuicao/verificar.ps1          # só confere o diff
powershell -NoProfile -File .claude/skills/verificar-contribuicao/verificar.ps1 -Testes  # e roda dotnet test + :core:test
```

Ele olha o que mudou em relação à `main` (mais o que está sem commit) e acusa:
- arquivo `.cs`/`.kt`/`.kts` **novo** sem `SPDX-License-Identifier: GPL-3.0-or-later` e `Copyright (C)`;
- arquivo que não deve ir (`.env`, `appsettings.Local.json`, `local.properties`, `*.jks`, `*.keystore`, `bin/`, `obj/`, `build/`);
- no texto acrescentado: IP de rede local, caminho de usuário (`C:\Users\...`), e-mail, e padrões de token/chave;
- alterações em `probe_tests/` ou em `docs/prompt-original.md` (somente leitura);
- `agent/tools/test-client.html` diferente de `docs/test-client.html`.

2. Leia o resultado com o usuário. Cada achado é **suspeita**: confirme no arquivo, não apague às cegas. Falso positivo
   (por exemplo `192.168.0.12` num exemplo de documentação, que é o valor falso permitido) é esperado.

3. Confira à mão o que o script não pega:
   - plugin novo: está em `PluginRegistry.cs`, no `.sln`, no Host (`ProjectReference`), no simulador e nas duas listas de ids dos testes (`SimulatorTests.cs`, `AllIds` em `OptionalPluginTests.cs`)?
   - documentou em `docs/protocolo-v1.md` e `agent/README.md`? Dependência nova está em `THIRD-PARTY-NOTICES.md`, com versão fixa?
   - mensagem de commit em Conventional Commits, em português, sem linhas de atribuição?

4. Relate ao usuário, sem arredondar: o que passou, o que falhou, e o que **não foi verificado** (hardware, rede, tablet).
