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

## Fase 2: os seis passos do onboarding

Rodar como na Fase 1. Com `ATALAIA_DATA_DIR` apontando para uma pasta de teste, o `settings.json` real não é tocado.

| # | Passo | Esperado |
|---|---|---|
| 1 | **Rede:** abrir o passo 1 | Mostra o IP da placa com gateway; com mais de uma placa lista todas (a sem gateway marcada) e dá para trocar. O tipo da rede (privada/pública) bate com Configurações > Rede do Windows |
| 2 | **Firewall sem regra:** apagar a regra `Atalaia-Agente` (e qualquer outra que libere a porta) e ver o passo | "Pendente" em coral e o botão **Liberar no Firewall** |
| 3 | **Liberar** | O Windows pede permissão de administrador (UAC). Aceitar: o estado passa a "Liberada" e existe a regra `Atalaia-Agente` (TCP, só a porta, perfil Privado, sub-rede local) em Firewall do Windows com Segurança Avançada. **Cancelar** o UAC: aparece "Você cancelou o pedido de permissão do Windows" e nada muda |
| 4 | **Trocar a porta** (ex.: 8800) com o Wi-Fi ligado | O agente religa na porta nova **sem mudar o código de acesso**; a regra do Firewall passa a ser da porta nova (não sobra a antiga). Porta ocupada por outro programa, ou fora de 1024 a 65535, é recusada com explicação |
| 5 | **Verificar** com o Wi-Fi ligado e a regra criada | Três itens confirmados e a nota dizendo que o tablet só é provado no passo 4. Mudar a rede para Pública e verificar de novo: o item do tipo de rede fica em coral |
| 6 | **Wi-Fi com variável de ambiente fixando** (`ATALAIA_Agent__ListenOnLan=false`) e tentar ligar | A chave volta sozinha e a mensagem cita a variável; o `settings.json` não muda |
| 7 | **USB:** sem o `adb` no PATH | "Não encontrado", com o botão para abrir a página do Google (nada é baixado) e **Procurar…** aceitando só um `adb.exe` |
| 8 | **Tablet plugado, sem aceitar a depuração** | O aparelho aparece como "aguardando você tocar em Permitir"; ao aceitar no tablet, vira "Online" sozinho (a tela consulta a cada 3 s) |
| 9 | **Cabo desplugado e replugado** com "Manter o túnel USB" ligado | "Túnel" vai a "Ativo", volta a "Aguardando o tablet" ao desplugar e a "Ativo" ao replugar |
| 10 | **App no tablet, build instalada de uma release** (`0.1.1-beta.N`) com o app antigo `0.0.1` (chave de debug) no tablet | O botão baixa e confere o APK, e a instalação falha com a explicação da assinatura diferente. **Desinstalar e instalar** só aparece depois, com o aviso de que apaga a configuração do app; confirmar instala. Cancelar não desinstala nada |
| 11 | **App no tablet, adulterar a soma** (mudar uma linha do `SHA256SUMS.txt` de uma release de teste) | "A soma de verificação do arquivo não confere": nada é instalado e o arquivo baixado é apagado |
| 12 | **App no tablet em build `-dev`** | Aviso de que não há release e o botão **Escolher APK…**: só aceita um arquivo que seja um APK; instala e mostra "App instalado" |
| 13 | **QR** | A câmera do tablet abre a página da release (ou a lista de releases em build `-dev`) |
| 14 | **Código:** com o tablet configurado | O código mostrado é o do menu da bandeja; ao tablet conectar, o passo conclui sozinho em 1 s e avança. Com `ATALAIA_TOKEN` definido, o código não aparece nem é copiado |
| 15 | **Fontes** | As onze, ligar e desligar religa o agente sem perder o código. O que falta (clima, nobreak, Steam, Discord) aparece em coral com a variável a definir, nunca o valor |
| 16 | **Pronto:** ligar **Iniciar com o Windows**, reiniciar a sessão do Windows | O agente sobe sozinho na bandeja, sem abrir a janela (o onboarding já foi concluído). Desligar remove o valor `Atalaia` da chave Run |

**O que ainda não foi provado** (precisa de Windows com permissão de administrador, tablet real ou release publicada): a criação da regra pelo processo
elevado, a instalação real no tablet e o download de uma release de verdade (só existe a partir da `0.1.1-beta.1`).

**O que este roteiro não cobre ainda** (chega nas próximas fases): as telas de configuração, a atualização automática e a instalação pelo
`Atalaia-Agent-Setup`.
