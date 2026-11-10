---
name: novo-plugin-agente
description: Cria o esqueleto completo de um plugin novo do agente do Atalaia (projeto C#, registro no Host, par simulado, entrada no appsettings, testes e documentação do protocolo). Use quando o usuário quiser acrescentar uma fonte de dados ou ações ao agente do PC, ou "contribuir com um plugin".
argument-hint: <nome-do-plugin>
---

# Novo plugin do agente

Argumento: `$ARGUMENTS` (o nome do plugin; peça se vier vazio).

Antes de tudo, leia `.claude/rules/plugins-agente.md` e `docs/protocolo-v1.md`, e o plugin de referência
`agent/src/Atalaia.Agent.Plugins.Weather/WeatherPlugin.cs`. Siga as regras de `.claude/rules/projeto.md`.

## 0. Combine o desenho com o usuário (não presuma)

Pergunte e confirme, em português, antes de escrever código:

- **id** do plugin (camelCase, único; vira o `source` do protocolo, e **não pode ser renomeado depois**) e o **nome legível**.
- **De onde vem o dado** (API pública, API do Windows, arquivo, processo) e **como será lido** (somente leitura).
- **Campos de `data`**: nome em camelCase, tipo e unidade. Campo sem leitura é `null`. Siga as unidades do protocolo.
- **Intervalo padrão** (`DefaultIntervalMs`).
- **Ações** (se houver): nome, `args` e efeito. Só ações de uma lista fixa; nunca comando livre.
- **Precisa de credencial ou dado pessoal?** Se sim: nasce `enabled: false` e o segredo só entra por variável de ambiente.
- **Dependência nova?** Só se for indispensável, com versão fixa e licença compatível com a GPL-3.0.
- Lembre ao usuário: acrescentar uma fonte é um **acréscimo ao protocolo v1** e deve ser combinado com o mantenedor (issue).

## 1. Projeto do plugin

Crie `agent/src/Atalaia.Agent.Plugins.<Nome>/` (`<Nome>` em PascalCase) com dois arquivos:

- `Atalaia.Agent.Plugins.<Nome>.csproj`: copie o do `Weather` (só referencia o `Atalaia.Agent.Core`). Se precisar de pacote NuGet,
  acrescente um `PackageReference` com versão exata.
- `<Nome>Plugin.cs`: parta de `PluginTemplate.cs.template` (nesta pasta), troque os marcadores e preencha. Mantenha o cabeçalho
  SPDX com **o nome de quem escreve**.

A lógica de leitura e conversão fica em um método **estático e puro** (como o `Build` do modelo), separado da coleta.

## 2. Registrar o plugin (3 lugares)

```powershell
# a partir de agent/
dotnet sln Atalaia.Agent.sln add src/Atalaia.Agent.Plugins.<Nome>/Atalaia.Agent.Plugins.<Nome>.csproj
```

- `agent/src/Atalaia.Agent.Host/Atalaia.Agent.Host.csproj`: acrescente
  `<ProjectReference Include="..\Atalaia.Agent.Plugins.<Nome>\Atalaia.Agent.Plugins.<Nome>.csproj" />` (junto das outras).
- `agent/src/Atalaia.Agent.Host/PluginRegistry.cs`: `using Atalaia.Agent.Plugins.<Nome>;` e a linha
  `("<id>", () => new <Nome>Plugin()),` no fim da lista `Real`.

## 3. Configuração

Em `agent/appsettings.json`, dentro de `Plugins`, acrescente `"<id>": { "enabled": true, "intervalMs": <ms> }` (mais as chaves próprias do
plugin). Com credencial ou dado pessoal: `"enabled": false`. Segredo: **nunca** no `appsettings.json`; documente (sem valor)
`# ATALAIA_Plugins__<id>__<chave>=` em `agent/.env.example`.

## 4. Par simulado

Em `agent/src/Atalaia.Agent.Plugins.Simulator/`: crie `Sim<Nome>` (parta de `SimPluginTemplate.cs.template` e veja o `SimWeather` em
`SimOptionalPlugins.cs`) com o **mesmo id e o mesmo formato de `data`**, e acrescente `new Sim<Nome>()` à lista de
`SimulatorPlugins.All()` (`SimPlugins.cs`). Atualize as **duas** listas de ids esperados em
`agent/tests/Atalaia.Agent.Tests/`: a do `SimulatorTests.cs` (linha com `"weather", "notifications"`) e `AllIds` em
`OptionalPluginTests.cs`. Se esquecer uma, `dotnet test` falha dizendo qual.

## 5. Testes

Crie `agent/tests/Atalaia.Agent.Tests/<Nome>Tests.cs` (parta de `TestTemplate.cs.template`): a conversão pura com entradas reais
da fonte (inclusive campo ausente virando `null` e, se houver número decimal, em cultura `pt-BR`), e as ações (válida, inválida e
desconhecida). Os testes não dependem de rede, hardware nem do seu Windows.

## 6. Documentação

- `docs/protocolo-v1.md`: uma subseção em "Fontes opcionais" (formato de `data`, origem, intervalo padrão, observações) e, se
  houver ações, as linhas na tabela de "Ações por fonte". **Avise o usuário** de que está mexendo no documento do contrato.
- `agent/README.md`: a(s) chave(s) de configuração na tabela.
- `THIRD-PARTY-NOTICES.md`: se acrescentou pacote.

## 7. Verificar e entregar

```powershell
cd agent
dotnet test                                                        # tudo verde
dotnet run --project src/Atalaia.Agent.Host -- --console --simulate # abra agent/tools/painel.html e veja a fonte
```

Depois rode `/verificar-contribuicao`. Se o app deve mostrar a fonte, use `/fonte-no-app <id>`.
Diga ao usuário, ao final, **o que foi testado de verdade e o que não foi** (por exemplo, "li a API real" ou "só com o simulador").
Não faça commit sem o usuário pedir.

## Erros comuns
- Esquecer o `ProjectReference` no Host (o plugin compila, mas não aparece no agente) ou o `using` no `PluginRegistry.cs`.
- Número com vírgula por falta de `CultureInfo.InvariantCulture` (quebra só no Windows em pt-BR).
- `CollectAsync` devolvendo `null` para esconder um erro: lance a exceção, o agendador cuida do backoff e do status.
- Campo que muda a cada coleta (um "agora") fazendo o plugin enviar sem parar.
- Segredo no `appsettings.json` ou no log.
