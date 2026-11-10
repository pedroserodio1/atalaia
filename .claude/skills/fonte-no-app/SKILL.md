---
name: fonte-no-app
description: Faz o app do tablet (Kotlin) entender e mostrar uma fonte nova do agente do Atalaia (modelo de dados, decodificação, cartão na tela, textos e testes). Use depois que o plugin existe no agente e o formato de data está no protocolo.
argument-hint: <id-da-fonte>
---

# Fonte nova no app

Argumento: `$ARGUMENTS` (o `id` da fonte, igual ao `source` do protocolo; peça se vier vazio).

Pré-requisitos: o plugin já existe no agente e o formato de `data` está em `docs/protocolo-v1.md` (use `/novo-plugin-agente`).
Leia `.claude/rules/app-kotlin.md` e `app/README.md`. O modelo completo e pequeno é a fonte **weather**: procure por `weather` em `app/`.

## Passos

1. **Modelo** em `app/core/src/main/kotlin/org/atalaia/core/protocol/Sources.kt`: `@Serializable data class <Nome>Data(...) : SourceData`.
   Todo campo com valor padrão (texto `""`, número/objeto `?` = `null`); o JSON do agente manda `null` onde não há leitura.
   Acrescente o `"<id>" -> json.decodeFromJsonElement(<Nome>Data.serializer(), data)` no `when` de `Sources.decode`.
2. **Lista de fontes** em `app/tablet/.../PanelApp.kt` (`SOURCES`): acrescente o id, para o app assinar a fonte.
3. **Tela** em `app/tablet/.../ui/Screens.kt`: um `@Composable` que lê `state.dataOf<<Nome>Data>("<id>")` e `state.freshness("<id>", now)`
   (como o `WeatherCard`). Todo texto em `ui/Texts.kt`. Sem leitura: `Texts.NO_VALUE`. Botão de ação só com
   `state.hasAction("<id>", "<ação>")`. Respeite o orçamento de 862 × 505 dp e toque ≥ 48 dp.
4. **Testes** (`app/core/src/test/...`):
   - `SourcesTest`: decodificação com JSON real do agente, `null`, campo ausente e campo extra.
   - `CapturedAgentTest`: se a fonte entrou no simulador, acrescente as linhas dela a `fixtures/agent-simulate.jsonl` (`seq` crescente,
     `host` `"PC"`) e o id ao conjunto `modeled`.
5. **Verificar**: `cd app && ./gradlew :core:test :tablet:assembleDebug`. Se não houver tablet para ver a tela, **diga** que a parte
   visual não foi conferida no aparelho.

## Cuidados
- Mudou nome ou tipo de campo? Isso é mudança de contrato: pare e combine com o mantenedor.
- Só API do Java 8 no `:core` (a compilação recusa o resto).
- Não mexa no que não faz parte da fonte (reformatar, renomear). Commit só quando o usuário pedir.
