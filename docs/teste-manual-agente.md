# Roteiro de teste manual do agente (Windows)

Cobre o que os testes automáticos **não** conseguem provar: janela, bandeja, WebView2, instalação, rede, Firewall, tablet e atualização.
Cada fase acrescenta a sua seção. Antes de começar, rode `dotnet test` em `agent/` e `./gradlew :core:test` em `app/` (devem passar).

> Para não mexer na sua configuração de verdade ao testar, use uma conta de usuário do Windows de testes ou uma VM. O agente grava em
> `%LOCALAPPDATA%\AtalaiaAgent\` (logs, `settings.json`, `webview2\`). Para voltar à "primeira execução", feche o Atalaia e apague essa pasta.
> Compilando da fonte, use `--simulate` e, para não colidir com um agente já rodando, `ATALAIA_Agent__Port=18765`,
> `ATALAIA_Agent__ListenOnLan=false` e `ATALAIA_Usb__Enabled=false` (o `appsettings.Local.json` do desenvolvedor pode ligar o Wi-Fi e o USB).

## Fase 1: janela, bandeja e instância única

Rodar: `dotnet run --project agent/src/Atalaia.Agent.Host -- --simulate` (ou o `AtalaiaAgent.exe` instalado).

| # | Passo | Esperado |
|---|---|---|
| 1 | Primeira execução (sem `%LOCALAPPDATA%\AtalaiaAgent\settings.json`) | A janela abre sozinha na tela **Boas-vindas**, escura, com o ícone da torre na barra de título e na bandeja. A versão mostrada é a do programa (`0.1.1-dev` na fonte, `0.1.1-beta.N` instalado) |
| 2 | Clicar em **Começar** | Vai para o passo 1 ("Rede e Firewall", com o aviso "Em construção"); o `settings.json` passa a ter `Onboarding.current = 1` |
| 3 | **Continuar** e **Pular este passo** em passos diferentes, depois ver o passo 6 | O passo vira "feito" (✓) ou "pulado" (tracejado); o resumo do passo 6 lista cada um como Feito, Pulado ou Pendente, com **Retomar** nos pulados |
| 4 | **Sair da configuração** no meio | Vai para o Início. Fechar tudo e abrir de novo: a janela **não** abre sozinha. Pelo ícone, o Início oferece **Retomar a primeira configuração** |
| 5 | Fechar a janela no X | A janela some, o ícone continua na bandeja e o agente segue rodando (`Clientes conectados` no menu responde). O processo do WebView2 (`msedgewebview2.exe` filho do agente) some junto |
| 6 | Abrir o `AtalaiaAgent.exe` de novo com o agente rodando | **Não** sobe outro agente (só um `AtalaiaAgent.exe` na lista de processos); a janela do primeiro volta para a frente, no mesmo passo |
| 7 | Menu da bandeja → **Abrir Atalaia**; clique esquerdo no ícone | Abre a janela (se já estiver aberta, só a traz para a frente) |
| 8 | Conectar o `tools/painel.html` (ou um tablet) ao agente, com a janela aberta no Início | O cartão **Tablets conectados** passa a listar o cliente, com **USB** se veio de `127.0.0.1` e **Wi-Fi** se veio de outro endereço, e atualiza sozinho ao conectar e desconectar |
| 9 | Menu da bandeja → **Sair** | A janela e o agente fecham; não sobra `AtalaiaAgent.exe` nem `msedgewebview2.exe` dele |
| 10 | Corromper de propósito o `settings.json` (apague uma chave `}`) e abrir o agente | O agente sobe normalmente, na primeira execução; ao lado do arquivo aparece `settings.json.corrompido-<data>`; o log (`Abrir pasta de logs`) avisa |
| 11 | Tela com zoom de 125% ou 150% | A janela abre com tamanho proporcional, sem texto borrado |
| 12 | **Sem o WebView2** (VM sem o runtime, ou renomear a pasta do runtime) | Aparece a mensagem "A janela do Atalaia precisa do WebView2…", o agente continua na bandeja |

**O que este roteiro não cobre ainda** (chega nas próximas fases): os passos reais do onboarding (rede, Firewall, adb, instalação do APK, código,
fontes), as telas de configuração, a atualização automática e a instalação pelo `Atalaia-Agent-Setup`.
