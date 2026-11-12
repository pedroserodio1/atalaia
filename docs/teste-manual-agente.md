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
| 14b | **Manter o código** (passo do código ou tela Conexão) | Ligar **Manter o mesmo código** não muda o código mostrado; reiniciar o agente (Sair e abrir) mostra o **mesmo** código e o tablet reconecta sem digitar. **Gerar novo** pede um segundo toque, troca o código e derruba o tablet, que mostra "Código inválido" até digitar o novo (o menu da bandeja já mostra o novo). Desligar a opção volta ao código novo a cada início. `secrets.dat` não tem o código em texto puro |
| 15 | **Fontes** | As dez, ligar e desligar religa o agente sem perder o código. O que falta (clima, nobreak, Steam, Discord) aparece em coral com a variável a definir, nunca o valor |
| 16 | **Pronto:** ligar **Iniciar com o Windows**, reiniciar a sessão do Windows | O agente sobe sozinho na bandeja, sem abrir a janela (o onboarding já foi concluído). Desligar remove o valor `Atalaia` da chave Run |

**O que ainda não foi provado** (precisa de Windows com permissão de administrador, tablet real ou release publicada): a criação da regra pelo processo
elevado, a instalação real no tablet e o download de uma release de verdade (só existe a partir da `0.1.1-beta.1`).

## Fase 3: telas de configuração

Depois de concluir (ou usar "Já está tudo configurado" na Boas-vindas), a janela abre no **Início** das configurações, com Conexão, Fontes de dados,
Jogos e ações e Atualizações. Para abrir: menu da bandeja > **Abrir Atalaia**.

| # | Passo | Esperado |
|---|---|---|
| 1 | **Conexão** | Código de acesso (o mesmo da bandeja), tablets conectados agora (USB ou Wi-Fi, atualiza sozinho), e embaixo as mesmas telas de Rede e Firewall e de USB e adb do onboarding, com o estado real |
| 2 | **"Em breve"** na Conexão | "Gerar novo", "Cancelar", "Revogar", mDNS e Wake-on-LAN aparecem **desabilitados** e marcados "em breve": nada deles responde ao clique |
| 3 | **Iniciar com o Windows** | Liga e desliga como no passo 6 do onboarding (valor `Atalaia` na chave Run) |
| 4 | **Fontes de dados** | A mesma lista do onboarding; ligar e desligar uma fonte religa o agente sem mudar o código de acesso. Clima, Steam e Discord têm o botão **Configurar** (veja a seção abaixo) |
| 5 | **Jogos e ações** | Só texto explicando o que vem: cenas por jogo e ações extras, tudo "em breve" e desabilitado |
| 6 | **Atualizações** | A versão real e a busca de atualizações (veja a Fase 4 abaixo) |
| 7 | **Refazer a primeira configuração** (rodapé da barra lateral) | Volta à Boas-vindas com o onboarding do zero; se o onboarding não foi concluído, o botão é "Retomar" e leva ao passo em que parou |
| 8 | **Fechar a janela** (rodapé) | Fecha só a janela; o agente segue na bandeja |

## Fase 4: atualização automática

Precisa de **internet** e de **releases de verdade** no repositório configurado (`Updates:Repository`): o agente só fala com `github.com`, então não dá para apontar para um
servidor de teste. O caminho completo (`0.1.1-beta.1` para `0.1.1-beta.2`) só existe depois da primeira release; antes dela dá para fazer os passos 1 a 4 e 12. Use uma **VM** ou uma conta de testes
(a atualização reinstala o programa em `%LOCALAPPDATA%\Programs\Atalaia`). O agente guarda em `%LOCALAPPDATA%\AtalaiaAgent\updates\<versão>\` o instalador baixado.

| # | Passo | Esperado |
|---|---|---|
| 1 | **Atualizações** > **Verificar agora** | "Verificando…" e, em seguida, "Você está na versão mais recente" **ou** a versão nova com as notas. A hora ("verificado há N min") aparece. Em `settings.json`, `Updates:LastCheck` ganha a hora |
| 2 | **Sem internet** (desligue a rede e verifique) | "Não foi possível falar com o GitHub. Confira a conexão com a internet." (estado, não erro fatal). Na **Boas-vindas** a pílula diz "Sem internet para buscar atualizações" e **não** diz "versão mais recente". Religue a rede e verifique de novo: volta ao normal |
| 3 | **Versão dispensada** | **Lembrar depois** na oferta: some o cartão e a tela diz que você escolheu lembrar depois da versão X (sem afirmar "mais recente"). Uma release mais nova volta a ser oferecida |
| 4 | **Canal** | Em **Estável**, pré-lançamento não aparece; trocar para **Beta** mostra a oferta na hora, sem nova consulta. Quem instalou um `-beta.N` começa em Beta (sem ter escolhido) |
| 5 | **Beta.1 para beta.2** (VM com a `0.1.1-beta.1` instalada pelo `Atalaia-Agent-Setup`) | Canal Beta, oferta da `0.1.1-beta.2` com as notas. **Baixar e instalar**: "Baixando…" com porcentagem, "Conferindo o SHA-256…", "Instalando…" com o aviso do código novo. O Atalaia fecha, o instalador roda **sem janelas** e o Atalaia **reabre sozinho** na versão nova (veja o número na barra lateral). Em `updates\0.1.1-beta.2\` ficam o `.exe` e o `SHA256SUMS.txt` |
| 6 | **Código de acesso depois de atualizar** | O código é **outro** (processo novo). O tablet mostra "Código inválido" e volta a conectar com o novo (menu da bandeja ou tela Conexão) |
| 7 | **Notas como texto** (uma release de teste com `<b>` e `<img onerror=...>` nas notas) | Aparecem **literalmente**, sem negrito nem imagem, e nada é executado |
| 8 | **Ver no GitHub** | Abre a página da release no navegador padrão, e só ela |
| 9 | **Instalar sozinho** ligado | Na próxima checagem com versão nova: "Já baixada e conferida. Será instalada quando você fechar o Atalaia pelo menu da bandeja". Nada é executado antes. **Sair** na bandeja: o instalador roda e o Atalaia reabre na versão nova. Desligar a opção antes de sair: nada é instalado ao sair |
| 10 | **Verificar automaticamente** desligado | Nenhuma consulta é feita sozinha (confira o log: sem "Checagem de atualização"); "Verificar agora" continua funcionando. Ligar de novo: a checagem vem em até ~1 min, sem reiniciar |
| 11 | **App do tablet** (tablet por cabo, app mais antigo que a release) | "No tablet: X · a release traz Y" e **Atualizar por USB**: baixa, confere e instala por cima. Com o app assinado por outra chave (o APK de debug da 0.0.1): a falha explica e só **Desinstalar e instalar** (com a confirmação) apaga o app e instala. Sem tablet, ou com o app em dia: botão desabilitado |
| 12 | **Build de desenvolvimento** (`-dev`, compilado da fonte) | A tela diz que o build de desenvolvimento não se atualiza sozinho e "Verificar agora" fica desabilitado |
| 13 | **Zoom do Windows em 125% e 150%** | A aba Atualizações (notas com rolagem, botões, canal) cabe e não corta texto |

**Risco conhecido, a provar na VM:** o agente fecha **antes** de o instalador terminar. Se o instalador falhar depois disso (antivírus bloqueando, disco cheio, arquivo travado), ninguém reabre o Atalaia.
Provoque uma falha (por exemplo, bloqueie o `.exe` baixado no meio) e anote: o que o Windows mostra, se a versão antiga continua utilizável (abrir pelo menu Iniciar) e se algum arquivo ficou pela metade.
Se for um problema na prática, a correção é reabrir o agente por outro caminho (tarefa agendada ou o próprio instalador com `/REABRIR=1` em caso de erro); ainda não há nada disso.

**O que não é provado à mão** (coberto só por teste automático, porque não dá para provocar com uma release de verdade): limite da API do GitHub (403/429 com a hora de volta), soma errada no
`SHA256SUMS.txt` e instalador trocado entre a conferência e a execução.

**O que este roteiro não cobre ainda:** a instalação limpa pelo `Atalaia-Agent-Setup` numa VM e o onboarding de ponta a ponta com o tablet real (Fase 5).

## Credenciais das fontes pela janela (0.1.2)

Depende do Windows de verdade (DPAPI) e das contas reais. Use uma pasta de dados à parte (`ATALAIA_DATA_DIR`) se não quiser mexer na configuração do dia a dia, e
rode **sem** `--simulate` (no modo simulado todas as fontes ficam ligadas com dados falsos e o resultado não prova nada). **Nunca cole chave, `clientSecret` ou
coordenada de verdade em issue, PR ou log.**

| # | Passo | Esperado |
|---|---|---|
| 1 | **Fontes de dados**, fonte **Clima**, botão **Configurar** | Abre o formulário com Latitude e Longitude; a consulta periódica não apaga o que você digita |
| 2 | Latitude `-23,55` (vírgula), `91`, `abc`, vazio; Longitude `181` | Mensagens em português (ponto decimal, intervalo −90 a 90 e −180 a 180, "preencha"); nada é gravado e o formulário continua aberto |
| 3 | Latitude e Longitude válidas com ponto, **Salvar**, ligar o Clima | O agente religa (o código de acesso é o mesmo), o formulário fecha, a fonte sai de "faltam a latitude e a longitude" e o tablet mostra o clima. O `settings.json` tem os dois valores; não existe `secrets.dat` ainda |
| 4 | **Steam**: chave vazia, `steamId64` com 16 ou 18 dígitos ou com letra | Recusado com mensagem em português; nada gravado |
| 5 | **Steam** com chave e `steamId64` (17 dígitos) válidos | Salva e religa. O campo da chave passa a mostrar **Configurada**, nunca o valor (confira também no DevTools/rede: a resposta de `sources.get` não traz a chave). Existe `%LOCALAPPDATA%\AtalaiaAgent\secrets.dat`; abri-lo no Bloco de Notas mostra bytes ilegíveis; a chave **não** está no `settings.json` nem no log |
| 6 | Reabrir a janela e **Configurar** de novo, deixar a chave em branco, trocar só o `steamId64` | A chave continua valendo (o plugin segue sem erro); trocar a chave por outra a substitui |
| 7 | **Remover** na Steam (dois toques: o primeiro pede confirmação) | Apaga o `steamId64` do `settings.json` e a chave do cofre; a fonte volta a pedir configuração |
| 8 | **Discord**: `clientId` e `clientSecret` do aplicativo criado no portal de desenvolvedores | Salva; ligar o Discord com o Discord aberto: **na primeira vez** o Discord mostra um pop-up pedindo autorização (aceite). O `discord.json` aparece em `%LOCALAPPDATA%\AtalaiaAgent\` e o cartão mostra a chamada de voz |
| 9 | **Variável de ambiente manda:** definir `ATALAIA_Plugins__steam__apiKey` (ou pôr no `.env`), reiniciar o agente | O campo da chave aparece travado com o nome da variável e "ela manda sobre este campo"; o plugin usa o valor do ambiente, não o do cofre. Remover a variável e reiniciar: volta a valer o cofre |
| 10 | **Desfazer em falha:** com a janela aberta, ocupar a porta do agente por outro programa e salvar uma fonte | Se o agente não subir, a mudança é desfeita (o valor anterior continua; confira o `settings.json` e que a chave antiga ainda funciona) e a janela explica |
| 11 | **Outra conta do Windows** (ou copiar o `secrets.dat` para outro PC) | O agente sobe normalmente sem as credenciais; o log avisa que o cofre estava ilegível, o arquivo é guardado como `secrets.dat.ilegivel-<data>` e as fontes pedem a configuração de novo |
| 12 | **Zoom do Windows em 125% e 150%** | O formulário cabe e não corta texto |

**O que é coberto só por teste automático:** a validação campo a campo, a precedência (ambiente acima do cofre acima do `settings.json`), o desfazer em falha de religar e
em valor sobreposto e o cofre com uma cifra falsa no lugar da DPAPI (a ida e volta da DPAPI de verdade também é testada, no Windows de quem roda).
