---
paths:
  - "agent/**/*.cs"
  - "agent/appsettings.json"
  - "agent/.env.example"
---

# Regras para plugins e código do agente

O contrato está em `agent/src/Atalaia.Agent.Core/Contracts.cs` (`IPlugin`, `IPluginContext`). O formato de cada fonte está em
`docs/protocolo-v1.md`. O modelo de plugin pequeno e completo é o `Atalaia.Agent.Plugins.Weather`.

## Estrutura
- Um plugin é um projeto `Atalaia.Agent.Plugins.<Nome>` que referencia **somente** `Atalaia.Agent.Core`. O Core não conhece
  nenhuma fonte; o `Atalaia.Agent.Host` (`PluginRegistry.cs`) é a raiz de composição. Não faça carga dinâmica de DLL.
- `Id`: camelCase, único, igual ao `source` do protocolo e à seção `Plugins:<id>` do `appsettings.json`. **Nunca renomeie** um id
  que já foi publicado.
- Lógica de leitura e conversão em **método estático e puro** (como `WeatherPlugin.Parse`), separado da coleta (rede, API do
  Windows, processo). É o que os testes chamam.
- Todo plugin tem um par simulado em `Atalaia.Agent.Plugins.Simulator`, com o mesmo `id` e o mesmo formato de `data`.

## Dados (`CollectAsync`)
- Devolva um objeto (`Json.ToElement(new { ... })`), nomes em **camelCase**. `null` = "sem dado nesta rodada" (não envia nada).
- **Campo sem leitura vai como `null`**, nunca `0` nem texto vazio fingindo leitura (texto sem valor é `""`, como no protocolo).
- Unidades do protocolo: `*Mb` e `*Gb` em base 1024; `*Kbps` em kilobits por segundo; percentuais, `*Mb`, `*Kbps`, `tempC`,
  `volume` e segundos como **inteiros**; `*Gb`, tensões, `freqHz` e `powerW` com **uma casa decimal**.
- Datas: `Json.Iso(...)` (ISO-8601 com offset, sem frações).
- O agendador só envia `update` quando o JSON mudou, ou a cada `MaxSilenceMs` (10 s). Evite campos que mudam sozinhos a cada
  coleta sem necessidade (um relógio "agora" faz o plugin enviar a cada ciclo).
- **Todo parse e formatação de número ou data usa `CultureInfo.InvariantCulture`.** O Windows em pt-BR usa vírgula decimal e
  isso já quebrou leitura de `nvidia-smi`.

## Ciclo de vida e falhas
- `StartAsync` sem a configuração necessária: lance `InvalidOperationException` com uma mensagem em português dizendo **qual
  chave** configurar. O plugin fica com `status: "error"` e o agente tenta de novo sozinho; nunca derrube o agente.
- `CollectAsync` que lança exceção ou passa de 10 s: o plugin vira `error` e o agendador aplica backoff. **Não engula o erro**
  devolvendo `null`; deixe a exceção subir. Um plugin que falha nunca afeta os outros.
- Libere tudo em `StopAsync` (`HttpClient`, processos, handles). Processo externo: sempre com timeout e `Kill`.
- Chamada de rede: `HttpClient` com `Timeout` curto. Sem retry próprio (o backoff é do agendador).

## Ações (`ExecuteAsync`)
- Só existem as ações declaradas em `Actions`. **Nunca** execute comando livre, caminho ou URL vindos do cliente.
- `args` inválido: `new ActionResult(false, "invalid_args")`. Ação desconhecida: `"unknown_action"`. Valide e **limite** os
  valores (`value` de volume entre 0 e 100, por exemplo).
- Ação com `Ok = true` dispara uma coleta imediata do plugin; não é preciso atualizar o estado à mão.
- O tempo máximo de uma ação é 5 s. Ação destrutiva e sem volta (como "limpar tudo") precisa de confirmação na interface do app.
- Códigos de erro de ação já usados: `unknown_plugin`, `unknown_action`, `invalid_args`, `unknown_session`, `no_session`,
  `not_supported`, `not_connected`, `timeout`. Reuse antes de inventar um novo.

## Configuração e segredos
- Entrada em `agent/appsettings.json`: `{ "enabled": ..., "intervalMs": ... }` mais as chaves próprias. Plugin que precisa de
  credencial ou de dado pessoal nasce `enabled: false`.
- **Segredo nunca no `appsettings.json`:** só por variável de ambiente `ATALAIA_Plugins__<id>__<chave>` ou `.env`, documentado
  (sem valor) em `agent/.env.example`.
- Leia a configuração em `ctx.Options["chave"]`. Nada de caminho, IP, usuário, localização ou nome de dispositivo no código.

## Privacidade e sistema do usuário
- O plugin só **lê**. Nunca abra porta serial (COM). Em banco de dados de terceiros, abra somente leitura e leia só as tabelas
  de que precisa.
- **Nunca registre no log** token, segredo, título de janela, conteúdo de notificação ou qualquer dado do usuário. Registre
  transições de estado ("plugin em erro: motivo"), uma vez, e não a cada tentativa.
- O que o plugin publica passa pela rede local: publique o mínimo necessário (por exemplo, nome do app e contagem, nunca o texto
  da notificação).

## Protocolo
- Fonte nova é um **acréscimo ao v1**: documente a seção em `docs/protocolo-v1.md` ("Fontes opcionais") com o formato de `data`,
  a origem, o intervalo padrão e as ações. **Nunca** mude nome, tipo ou significado de campo existente. Clientes antigos
  ignoram fonte que não conhecem, e é assim que a compatibilidade se mantém.
- Combine com o mantenedor (issue) antes de acrescentar a fonte ao contrato.
- Se mudar os plugins registrados, atualize os dois testes que listam os ids do simulador (`SimulatorTests.cs` e `AllIds` em `OptionalPluginTests.cs`) e o README do agente.

## Testes (xUnit, em `agent/tests/Atalaia.Agent.Tests`)
- Teste a conversão pura com entradas reais do que a fonte devolve (inclusive campos ausentes, que viram `null`).
- Teste em cultura `pt-BR` quando houver número com decimal. Teste a ação: inválida, desconhecida e válida.
- Teste não depende de rede, hardware, relógio nem do estado do seu Windows.
