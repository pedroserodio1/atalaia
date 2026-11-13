# Roadmap do Atalaia

Status em **13/11/2026**. Este arquivo organiza o que falta em versões. O que manda sobre o contrato é `docs/protocolo-v1.md`; as regras de ramo,
tag e número de versão estão em [`docs/versionamento-e-releases.md`](docs/versionamento-e-releases.md). Datas não são promessa: a ordem e o
escopo de cada versão podem mudar, e quem decide é o mantenedor.

Como ler os números (SemVer, antes da 1.0): **PATCH** = correção compatível; **MINOR** = recurso novo (pode quebrar, com aviso nas notas);
**MAJOR** só na `1.0.0`. Beta sai em `development` (`vX.Y.Z-beta.N`), estável em `main` (`vX.Y.Z`).

## Onde estamos

- **Agente (Windows):** 10 fontes (`system`, `gpu`, `audio`, `media`, `ups`, `activeWindow`, `steam`, `discord`, `weather`, `notifications`),
  bandeja, janela de configuração (WinForms + WebView2) com onboarding de 6 passos, módulo USB (`adb reverse`), cofre de segredos, atualização
  automática.
- **App (tablet):** 5 abas (Início, Sistema, Mídia, Discord, Jogo), coluna "agora", failover USB/Wi-Fi, alerta de queda de energia, tela de
  configuração.
- **Release:** pipeline por tag, instalador Inno Setup e APK assinado; a 0.1.1, a 0.1.2 e a 0.2.0 (`v0.2.0`, 13/11/2026) estão fechadas; a 0.3.0 está em andamento (ciclo `beta/0.3.0` aberto; `v0.3.0-beta.1` (alertas de limite), `v0.3.0-beta.2` (tela de PC desligado) e `v0.3.0-beta.3` (troca de tela por contexto) e `v0.3.0-beta.4` (aba Discord nova) publicadas em 13/11/2026; `beta.5` (aviso de download, PIN, tela Atração e a correção da #66) em publicação).
- **Issues abertas:** [#14](https://github.com/pedroserodio1/atalaia/issues/14) (controle do Google Meet pelo tablet),
  [#15](https://github.com/pedroserodio1/atalaia/issues/15) (digitar o código no tablet a cada reinício; opção A feita, falta o pareamento),
  [#23](https://github.com/pedroserodio1/atalaia/issues/23) (notas da release estável com grupos repetidos; planejada para a 0.3.1),
  [#66](https://github.com/pedroserodio1/atalaia/issues/66) (a janela dizia que o app não está instalado durante a atualização por USB; corrigida na 0.3.0-beta.5, falta provar).
  Fechadas na 0.1.2: #12 (Steam), #16 (Wi-Fi), #17 (nobreak). Resolvida na 0.2.0 (`beta.3`): #13 (porta ocupada no boot).

## Resumo das versões

| Versão | Tema | Tipo | Principais itens |
|---|---|---|---|
| **0.1.1** | Primeira release pública | fechada | Janela, onboarding, atualização automática, primeira release |
| **0.1.2** | Correções | fechada (`v0.1.2`) | #12, #16 (Wi-Fi), #17 (nobreak), fonte Claude desativada |
| **0.2.0** | Primeira experiência | minor, fechada (`v0.2.0`) | **Tutorial de chaves e serviços externos no onboarding**, "Testar conexão", código de acesso sem redigitar (#15), porta ocupada no boot (#13, feita), app abrindo ao ligar o tablet (feito); provas e #15 (opção A) feitas |
| **0.3.0** | Tablet esperto | minor, em andamento (ciclo `beta/0.3.0` aberto, `beta.4` publicada, `beta.5` em publicação) | Troca de tela por contexto, alertas de limite, tela de PC desligado e modo noite, Wake-on-LAN, tela Atração, Discord (ping, modo de voz, sair da call, tela mais clara) |
| **0.3.1** | Correções | patch | #23 (notas da release estável com grupos repetidos) |
| **0.4.0** | Mais fontes de dados | minor | Agenda (iCal), Rede, Servidores (VPS e GitHub), temperatura e núcleos da CPU e FPS, Discord (volume por pessoa e avatares) |
| **0.5.0** | Ações no PC | minor | Deck, integração com o Google Meet (#14), Discord (entrar em canal pelo tablet) |
| **0.6.0** | Plataforma aberta | minor | Catálogo de widgets, plugins externos, coluna "agora" configurável, docs em inglês, FFXIV e LoL |
| **1.0.0** | Estável | major | Protocolo e plugins estáveis (critérios no fim) |

---

## 0.1.1: primeira release pública (fechada)

Entregue e funcional: versionamento e CI, janela do agente, onboarding, telas de configuração, atualização automática e a primeira release.

Não entrou: assinatura de código do Windows (decisão do mantenedor; o SmartScreen avisa).

## 0.1.2: correções (fechada, `v0.1.2`)

Só correção compatível, sem mexer no protocolo nem na configuração.

- [x] **#12:** o status "jogando agora" da Steam não pode sumir enquanto o processo do jogo continua aberto. Causa: o app trata o dado como
  velho após 15 s e a Steam é coletada a cada 30 s. Correção: limite de dado velho por fonte no app (`max(15 s, 2 × intervalMs` do `hello`)) e,
  no agente, confirmar o jogo em execução pelo `RunningAppID` da Steam quando a API falhar. (A fonte `activeWindow` só vê a janela em primeiro
  plano, então não serve para isso.)
- [x] **#16:** a primeira conexão por Wi-Fi não funciona; só conecta com o USB plugado. Causa ainda não investigada. Verificar, nesta ordem:
  `ListenOnLan` ligado, regra do Firewall (e o perfil de rede, Pública ou Privada), IP mostrado na janela, limite de tentativas errando por
  causa do código que muda, e o agente subindo antes de a rede estar pronta. É o primeiro defeito que a prova de Wi-Fi real (0.2.0) traria, então
  vale antecipá-la.
- [x] **#17:** o nobreak fica marcado "na bateria" para sempre quando a energia volta com o PC desligado (o evento de restauração nunca é gravado).
  Deduzir a volta da rede nas leituras de `status_history`: tensão de entrada normal OU carga da bateria subindo (ideia do mantenedor), com regra
  anti-ruído (a carga oscila 1%) e função pura testada; a tensão em bateria ainda é a confirmar com uma queda real. O alerta do tablet deve sumir sozinho.
- [x] **Desativar a fonte Claude** (plugin `claude`, aba Claude e o bloco na coluna "agora") até existir uma fonte de dados mais confiável.
  Decisão do mantenedor: fica desligada de vez, sem opção de ligar, e some do onboarding e da aba Fontes de dados. O código pode ficar no
  repositório, mas o `hello` não deve declará-la e o app não deve desenhar a aba (restam 5). Os itens que dependem dela (tutorial da 0.2.0 e
  alerta de limite da 0.3.0) ficam suspensos. Voltar a ligar é uma decisão futura, quando houver fonte confiável.
- [ ] Qualquer bug achado na `0.1.1` que não tenha entrado nela.

## 0.2.0: primeira experiência (fechada, `v0.2.0`)

Objetivo: alguém que acabou de instalar consegue deixar tudo funcionando **sozinho**, sem ler código nem pedir ajuda.

### Tutorial das chaves de API e dos serviços externos (novo)

Hoje o passo 5 do onboarding ("Fontes de dados") pede `apiKey` e `steamId64` da Steam, `clientId` e `clientSecret` do Discord e latitude e
longitude do clima, mas **não ensina onde pegar nada disso nem o que configurar do lado de fora**. É a maior barreira de quem instala.

O que fazer:

- [x] Em cada fonte com credencial, um bloco **"Como conseguir"** dentro do próprio formulário (o componente `howto` já existe no passo de USB),
  com passos numerados, curtos, em português, e o que fazer quando der errado. Abre pelo botão de ajuda do campo, sem sair da janela.
  Código e testes prontos (botão **Como conseguir** na linha de cada fonte; Steam, Discord, clima e nobreak). Provado pelo mantenedor na janela, em 11/2026.
- [x] Os mesmos tutoriais na aba **Fontes de dados** das configurações (não só no onboarding) e em `docs/` (uma página por serviço, para
  quem prefere ler no navegador). Texto escrito uma vez e reaproveitado; exemplos com valores falsos (`FAKEKEY0123456789ABCDEF`).
  As páginas estão em `docs/fontes/` (um teste confere que são iguais ao texto da janela); a aba usa o mesmo componente do onboarding. Provado pelo mantenedor, em 11/2026.
- [x] Botão **"Testar conexão"** por fonte: valida a credencial na hora e devolve a causa em português ("chave recusada", "perfil privado",
  "Discord fechado") em vez de a fonte só ficar em erro.
  Código e testes (rede falsa) prontos. O teste do Discord é a versão leve (aberto + Client ID e Secret aceitos, sem o pop-up de autorização). Provado pelo mantenedor com
  credenciais reais, em 11/2026 (roteiro em `docs/teste-manual-agente.md`). Fica no botão **Configurar** de cada fonte.
- [x] Botão **"Abrir página"** que leva ao site certo. Hoje o `LinkPolicy` só aceita links de `https://github.com/{repositório}/...`; abrir
  `steamcommunity.com` e `discord.com/developers` exige **uma lista fechada e pequena de endereços**, definida no código (nunca vinda de
  configuração nem de conteúdo remoto). Mudança sensível de segurança: revisar com o mantenedor.
  Decidido: mapa fixo no C# (`SourceHelp`), a página manda só o id da fonte (`sources.openPage`) e o `LinkPolicy` **não muda**. Provado pelo mantenedor, em 11/2026.
- [x] Texto dos tutoriais é fixo, embutido no agente. Nada é baixado de fora para exibi-los.

Conteúdo mínimo de cada tutorial (a confirmar passo a passo contra os sites reais antes de publicar, porque as telas deles mudam):

| Fonte | O que o tutorial precisa cobrir |
|---|---|
| **Steam** | Onde gerar a chave da Web API (página de chave de desenvolvedor da Steam, campo de domínio aceita qualquer valor; conta limitada pode não conseguir gerar). Como achar o SteamID64 (17 dígitos; vale o endereço do perfil). **Deixar "Detalhes do jogo" público** no perfil, senão biblioteca, recentes e amigos aparecem como "oculto no perfil". Explicar que a chave é secreta e fica no cofre. |
| **Discord** | Criar um aplicativo no Portal de Desenvolvedores do Discord; copiar o **ID do aplicativo** (`clientId`) e gerar o **segredo do cliente** (`clientSecret`, mostrado uma vez só). Cadastrar o redirecionamento `http://localhost` em OAuth2 (é o que o plugin usa por padrão). Escopos usados: `rpc`, `rpc.voice.read`, `identify`. O **Discord para desktop precisa estar aberto** e, na primeira vez, aparece um pop-up de autorização (há 3 minutos para aceitar). Verificar com o plugin real se o escopo `rpc` exige que a conta seja dona ou testadora do aplicativo. |
| **Clima** | Não usa chave (Open-Meteo). Como descobrir latitude e longitude (clicar no local em um mapa), usando **ponto** como separador decimal. Aviso de privacidade: só essas coordenadas saem do PC. |
| **Nobreak** | Em que condição a fonte funciona (software e modelo compatíveis), ou "desligada por padrão". |
| **Tablet (USB)** | Já existe ("Não aparece o tablet?"). Revisar para incluir o passo de autorizar o computador na tela do tablet. |

### Confiança e provas (do relatório de verificação; provadas pelo mantenedor em 11/2026)

- [x] Provar de verdade o **Wi-Fi** e o **failover USB, Wi-Fi e USB** com os dois canais vivos, a queda silenciosa do Wi-Fi e uma ação em andamento
  durante a troca (roteiro: cenários 4 a 7 de `app/docs/teste-manual.md`).
- [x] **Dados reais** (sem `--simulate`) conferidos no app: Windows, nobreak, Steam, Discord e clima.
- [x] Teste de memória de 8 h (precisa rodar de dia, com o PC ligado) e medição de quadros durante o arrasto.
- [x] Casos de borda do app ainda não vistos: perfil Steam oculto, mais de 4 membros na call, reordenar endereços, botão Colar.
- [x] Criação da regra do Firewall pelo UAC e WebView2 ausente / zoom 125% e 150% na janela.

### Conexão sem redigitar o código

- [x] **#15 (opção A feita na `0.2.0-beta.1`):** "Manter o mesmo código" na janela (cofre, DPAPI) e "Gerar novo" para revogar. A opção B
  (pareamento por segredo longo, "revogar tablet") segue na 0.3.0, junto do pareamento com validade.
- [x] **#15 (contexto):** hoje o código de 6 dígitos muda a cada início do agente e é preciso digitá-lo de novo no tablet, o que incomoda quem desliga o PC
  todo dia. Opções em análise: (A) código persistente opcional guardado no cofre, (B) pareamento em que o primeiro código vira um segredo longo
  guardado no tablet, (C) só documentar o `ATALAIA_TOKEN`. A (B) é a mais segura e permite revogar tablet, mas **muda o contrato** e precisa
  da aprovação do mantenedor; a (A) resolve rápido. O limite de tentativas erradas continua valendo em qualquer caso.

### Porta ocupada no boot (#13, adiada da 0.1.2)

- [x] **#13 (feita na `0.2.0-beta.3`):** o agente não pode cair no boot quando a porta padrão está ocupada (inclusive iniciando com o Windows). Hoje o `StartAsync`
  falha (`AgentRuntime.cs`) e o `Program.cs` mostra uma caixa e encerra, sem bandeja. Pool de portas com fallback (faixa contígua a partir da
  padrão), erro só se todas falharem, porta em uso mostrada na janela e na bandeja, e o onboarding avisando que o endereço de Wi-Fi mudou.
  - O tablet guarda `host:porta` e assume 8765 quando só o IP é digitado (`Endpoint.parse`). O USB acompanha sozinho (o `adb reverse` já usa a
    porta real). Decisão do mantenedor: o **app descobrir a porta**. Isso **muda o contrato** (`docs/protocolo-v1.md`), então precisa ser combinado e
    escrito antes de implementar.
  - A regra do Firewall é por porta única (`Firewall.cs`): com fallback, passa a ser uma faixa (o avaliador já entende faixas).
  - Feito no código (faixa de 10 portas, `8765-8774`): o agente cai na próxima porta livre, a regra do Firewall cobre a faixa, o `adb reverse` liga o
    `tcp:8765` do tablet à porta real do PC, a janela e a bandeja mostram a porta em uso, e o app varre a faixa na rede local (contrato em
    `docs/protocolo-v1.md`). Provado pelo mantenedor em 11/2026 (cenário 13 de `app/docs/teste-manual.md`).

### Pequenas melhorias de instalação

- [x] App do tablet abrindo sozinho ao ligar o aparelho (feito na `0.2.0-beta.5`).
  `BootReceiver` (`RECEIVE_BOOT_COMPLETED`, abre a tela e termina, sem serviço). Provado no tablet de verdade (Android 8.1) em 11/2026: o painel abre sozinho,
  mas **leva uns 25 a 30 s depois de o Android ligar** (o aviso de boot é entregue aos apps um por vez e o tablet é lento) e **a tela de bloqueio precisa estar
  desligada**, senão o painel fica atrás dela. Do Android 10 em diante o sistema bloqueia (não afeta o alvo).
- [x] App do tablet abrindo sozinho **depois de instalado ou atualizado por USB**. Hoje o `adb install -r` deixa o app fechado. Depois de uma
  instalação bem-sucedida o agente roda `adb shell am start -n org.atalaia.app/.MainActivity` (argumentos fixos, só o pacote do Atalaia),
  no `AdbService.RunInstallAsync`, que serve à atualização automática e ao passo "App no tablet". Falhar ao abrir não deve virar erro de
  instalação. É comportamento novo (`feat`), por isso fica na 0.2.0 e não na 0.1.2 (a esteira barra `feat` em patch).
  Provado pelo mantenedor no tablet, em 11/2026.
- [x] Avisar na tela quando o código de acesso mudou depois de atualizar ou reiniciar o agente (o tablet precisa do código novo). Some se a #15
  for resolvida pelo pareamento.
  `code.get` devolve `changed` e a janela mostra o aviso. Provado pelo mantenedor, em 11/2026.

## 0.3.0: tablet esperto

Comportamentos que fazem o painel "se virar" sozinho. Quase tudo usa dados que o agente já envia (as exceções estão em "Em risco" e em
"Mudanças de contrato", abaixo).

Objetivo: o tablet deixa de ser só um espelho do PC e passa a reagir ao que acontece (jogo abriu, GPU esquentando, PC desligou) sem ninguém mexer nele.
Fluxo do ciclo em `docs/versionamento-e-releases.md`. O ramo `beta/0.3.0` saiu de `development` com `VERSION` em `0.3.0`.

- [ ] **Troca de tela por contexto:** jogo abriu leva à aba Jogo; call no Discord leva à aba Discord; volta ao Início depois de um tempo sem uso.
  Código e testes prontos (ramo `feat/app-contexto`): gatilho do jogo só `steam.game` (decidido; atraso de até 30 s e jogo fora da Steam não dispara), call depois de 3 s, volta ao
  Início após 5 min sem toque (menos se a aba é a do contexto ainda ativo); ligada por padrão, com opção de desligar na configuração. Não puxa a tela ao reconectar com jogo ou call
  em andamento, nem de quem está mexendo (toque nos últimos 15 s). Provado pelo mantenedor no tablet (Discord e Steam reais, 13/11/2026): a call leva à aba Discord e o jogo da Steam leva à aba Jogo. Falta provar (cenário 17 de `app/docs/teste-manual.md`): a volta ao Início após 5 min, o anti-ruído (reconectar com jogo ou call em andamento, trocar de canal), a opção de desligar e a medição de quadros do arrasto.
- [x] **Alertas de limite:** GPU quente, disco cheio, RAM no fim (o alerta do limite do Claude fica suspenso junto com a fonte). Limites configuráveis e regra anti-ruído
  (o nobreak já chegou a dar 95 quedas em ~2 dias).
  Feito na `0.3.0-beta.1`: padrões GPU 85 °C, RAM 90 % e disco 90 % (cada um pode ser desligado na configuração do tablet), liga depois
  de 10 s acima do limite e só desliga 5 pontos abaixo, aparece no cartão do relógio (a queda de energia continua mandando). Provado pelo mantenedor no tablet, em 13/11/2026
  (roteiro: cenário 15 de `app/docs/teste-manual.md`).
- [ ] **Tela de PC desligado:** o estado "PC desligado ou inalcançável" é o normal da noite (o PC desliga por volta das 22h); hoje a tela fica
  acesa mostrando isso. Decidido: **configurável** na tela de configuração do tablet entre **modo noite por horário**, **relógio discreto sempre** e
  **tela apagada**.
  Código e testes prontos (ramo `feat/app-pc-desligado`): vale depois de **1 min** sem achar o PC (o failover USB e Wi-Fi não conta); modo noite (padrão 22h às 07h; fora do
  horário o painel segue como antes), relógio sempre ou tela apagada (preta no brilho mínimo, com a tela ainda ligada); segurar na tela abre a configuração. Falta provar no
  tablet de verdade (cenário 16 de `app/docs/teste-manual.md`): o preto e o brilho mínimo no T68, o horário virando a meia-noite e o consumo a noite toda.
- [ ] **Wake-on-LAN:** botão "Ligar PC" na tela de PC desligado. Exige o MAC do PC (o agente grava enquanto está ligado), e o PC no cabo ou
  com a placa configurada para acordar. Ação nova no protocolo: campo opcional, então MINOR, mas combinar com o mantenedor.
- [ ] **Tela Atração:** relógio, estado do PC e horas jogadas hoje, para quando ninguém está interagindo.
  Código e testes prontos (ramo `feat/app-atracao`): com o PC conectado e 10 min sem toque, o painel dá lugar a um relógio com a data, o que o PC faz ("PC ligado" ou "jogando" e o nome do jogo) e "Jogado hoje".
  Qualquer toque sai. Não entra por cima de queda de energia, alerta de limite ou call de voz; jogando, entra. Ligada por padrão, com opção de desligar na configuração. "Jogado hoje" é o tempo que o
  tablet viu `steam.game` ativo, só em memória: reiniciar o app zera, o tempo sem conexão não entra e jogo fora da Steam não conta. Falta provar no tablet (cenário 21 de `app/docs/teste-manual.md`).
- [ ] **Discord: sinais e controles novos.** Só usam comandos do RPC local que o plugin já fala (`docs.discord.com/developers/topics/rpc`); campos e
  ações são acréscimos opcionais ao `discord` do v1, então MINOR, mas é mudança de contrato: combinar com o mantenedor antes.
  - [ ] **Qualidade da conexão:** assinar `VOICE_CONNECTION_STATUS` (`state`, `average_ping`) e mostrar "Conectado · 38 ms" no cabeçalho do canal.
    Campo novo e opcional (`null` quando não há leitura).
  - [ ] **Modo de voz:** o `GET_VOICE_SETTINGS` já traz `mode.type` (`PUSH_TO_TALK` ou `VOICE_ACTIVITY`) e o plugin ignora. Mostrar a etiqueta e uma ação para
    alternar o modo (pelo mesmo `SET_VOICE_SETTINGS`).
  - [ ] **Sair da call:** `SELECT_VOICE_CHANNEL` com `channel_id: null`. Ação com **confirmação na própria tela** (regra do app).
  - [ ] **Escopos:** a doc não diz quais escopos cada comando de voz exige. Provar com o Discord real; se pedir escopo novo, atualizar o tutorial
    (`docs/fontes/discord.md` e o texto da janela) e lembrar que o refresh token guardado só tem os escopos antigos (o pop-up de autorização aparece de novo).
- [x] **Discord: tela mais clara.** Sem mudança de protocolo, só o app: contagem no cabeçalho do canal ("5 na call"), quem fala em destaque (no topo da lista ou
  com mais peso), marcar o próprio usuário na lista. Tudo precisa caber na altura útil de 505 dp junto do ping e do modo de voz.
  Código e testes prontos (ramo `feat/app-discord-tela`): layout novo com a faixa de controles à esquerda (botões redondos de microfone e fones no alto, volumes em pé embaixo) e a lista da call à
  direita, com a altura toda; o número de pessoas no cabeçalho do canal (ícone e número); estados por **ícone** (som para quem fala, microfone cortado para mudo, microfone e fones cortados para surdo)
  no lugar dos textos "falando", "mudo" e "surdo"; ensurdecer mostra o microfone desligado também (como no Discord). Ícones do Tabler (MIT), só seis copiados, sem biblioteca. Sem a marca "você":
  decidido tirar, porque só funcionava quando o nome do membro era igual ao `user`. Provado pelo mantenedor no tablet, em 13/11/2026 (`0.3.0-beta.4`, cenário 18 de
  `app/docs/teste-manual.md`): a tela ficou como desenhada.
- [ ] **Aviso de download ou cópia terminando no PC.**
  Download: código e testes prontos (ramo `feat/app-aviso-download`). O app deduz de `system.net.rxKbps`: tráfego a partir de 30 Mbps (configurável de 10 a 100, ou desligado) por 2 min seguidos é um
  download; quando cai abaixo de 2 Mbps por 30 s, o cartão do relógio mostra "Download terminou" (borda menta) por 10 min ou até um toque. Reconectar zera a contagem. Sem mudança de contrato.
  Falta provar no tablet (cenário 19 de `app/docs/teste-manual.md`). **Cópia de arquivos: fora da 0.3.0** (pede leitura de disco nova), vai para a 0.4.0 junto com "Tráfego de rede".
- [ ] **Configurações no tablet com PIN** (o desenho original previa; a base saiu sem PIN).
  Código e testes prontos (ramo `feat/app-pin`): PIN opcional (desligado por padrão) de 4 a 6 dígitos, definido ou removido no cartão "PIN da configuração"; pedido em toda entrada na configuração
  (toque longo no relógio, faixa de erro de conexão e toque longo na tela de PC desligado), menos na primeira configuração. Guarda só sal e hash PBKDF2, nunca o PIN; cinco erros seguidos trancam
  por 30 s. Esqueceu o PIN: reinstalar o app (janela do agente, "Desinstalar e instalar"). Falta provar no tablet (cenário 20 de `app/docs/teste-manual.md`), inclusive o teclado nos 505 dp.
- [ ] **Pareamento por código com validade** e lista de tablets com "Revogar" (hoje o protocolo tem um token único). Pode exigir protocolo
  novo, então só entra se o mantenedor aprovar a mudança de contrato.

### Ordem de trabalho (proposta, quem decide é o mantenedor)

Um sub-ramo por linha, entrando em `beta/0.3.0` por squash. Dá para começar pelas linhas 1 a 3, que são só do app e não mexem no contrato.

| # | Sub-ramo | Itens | Contrato | Depende de |
|---|---|---|---|---|
| 1 | `feat/app-alertas-limite` | Alertas de limite | não (`gpu.tempC`, `system.ram`, `system.disks` já chegam) | nada |
| 2 | `feat/app-contexto` | Troca de tela por contexto; Discord: tela mais clara | não | nada. Decidido: o gatilho "jogo abriu" é só `steam.game` (coletado a cada 30 s); `activeWindow` não entra. Em dois sub-ramos: `feat/app-contexto` (troca de tela) e `feat/app-discord-tela` (tela mais clara) |
| 3 | `feat/app-pc-desligado` | Tela de PC desligado, **configurável** entre modo noite por horário, relógio sempre e tela apagada | não | nada (decisão 1 tomada). Hoje o app só escreve o texto de `ConnectionState.Unreachable` (`Texts.kt`); a escolha fica na tela de configuração do tablet |
| 4 | `feat/app-atracao` | Tela Atração | não | 3 (reaproveita o relógio). "Horas jogadas hoje": o v1 só traz `hours2w` e `library.hours`, então o app teria de somar o tempo com `steam.game` ativo enquanto conectado, zerando à meia-noite (só conta o que ele viu). A confirmar |
| 5 | `feat/discord-sinais` | Discord: qualidade da conexão, modo de voz, sair da call, escopos | sim, campos e ações opcionais em `discord` | aprovação do contrato; **primeiro um teste com o Discord real** para saber os escopos |
| 6 | `feat/wake-on-lan` | Wake-on-LAN | sim, publicar o MAC (campo opcional) | decisões 2 e 7. O pacote mágico sai **do tablet** (UDP na rede local; o agente está desligado junto com o PC), então talvez nem seja uma ação nova, só o MAC; a confirmar. Só funciona pelo Wi-Fi, não pelo USB |
| 7 | `feat/app-pin` | Configurações no tablet com PIN | não | nada |
| 8 | `feat/aviso-transferencia` | Aviso de download ou cópia terminando no PC | a definir (ver "Em risco") | nada para o download; a cópia pode exigir campo novo |
| 9 | `feat/pareamento` | Pareamento com validade e lista de tablets | sim, mensagens novas | aprovação do contrato; é o item maior e mais arriscado, deixar por último |

### Mudanças de contrato desta versão (aprovar antes de codar)

Tudo campo ou ação opcional (MINOR, compatível com o v1), mas cada uma entra no `docs/protocolo-v1.md` no mesmo PR da mudança:

- `discord`: qualidade da conexão (estado e ping, `null` sem leitura) e modo de voz (`PUSH_TO_TALK` ou `VOICE_ACTIVITY`).
- Ações do `discord`: alternar o modo de voz e sair da call (esta com confirmação na tela do tablet).
- Endereço físico (MAC) do PC publicado pelo agente, para o Wake-on-LAN. Onde fica (`hello` ou `system`) é a decidir.
- Pareamento e revogação de tablet: mensagens novas no protocolo, o que o v1 hoje lista como "fora do v1".

### Em risco

- **Aviso de download ou cópia terminando no PC (decidido: fica na 0.3.0):** não existe fonte própria para isso (nem no agente, nem no protocolo).
  Proposta para caber no ciclo: começar pelo **download**, que o app deduz de `system.net.rxKbps` (tráfego alto e contínuo que cai e fica baixo por
  um tempo; função pura testada, com anti-ruído como nos alertas de limite), sem mudar o contrato. A **cópia** de arquivos precisa de leitura de
  disco, que o `system` não tem: seria campo novo e opcional (MINOR), combinar antes. A confirmar com o mantenedor se a cópia entra nesta versão.
- **Pareamento:** é o único item que pode sozinho adiar a versão. Proposta: decidir no começo do ciclo se entra; se não entrar, vai para a 0.4.0 e a #15
  continua só com a opção A (que já resolve o dia a dia).
- **Wake-on-LAN** depende das decisões 2 e 7 (abaixo); sem elas, a linha 6 não começa.

### Pronto quando

- Cada item só vira `[x]` com prova no tablet de verdade; o que depende de hardware (Wake-on-LAN com o PC desligado, Discord real) fica `[ ]` com o que falta provar.
- Roteiros novos em `app/docs/teste-manual.md` e `docs/teste-manual-agente.md` para o que for provado à mão.
- `dotnet test` e `./gradlew :core:test` verdes; `docs/protocolo-v1.md` e os tutoriais (`docs/fontes/discord.md`) atualizados se algo do contrato ou dos escopos mudou.

## 0.3.1: correções

Só correção compatível, sem mexer no protocolo nem na configuração.

- [ ] **#23:** as notas da release estável saem com os grupos repetidos (um bloco "Novidades", "Correções" e "Outros" por beta). Causa: o
  `cliff-notas.toml` não define `tag_pattern`, então o git-cliff trata cada tag de beta da faixa como uma release. Correção: um `tag_pattern`
  que nunca casa, comentado no arquivo, e um teste (ou passo do ensaio `dry_run` do `release.yml`) que falha se um grupo aparecer duas vezes.
  A 0.2.0 ainda saiu com o defeito.
- [ ] **#66 (corrigida no ciclo da 0.3.0, ramo `fix/agente-update-usb`; sai na `0.3.0-beta.5`):** durante "Atualizar por USB" a janela do agente mostrava "O app do Atalaia não está instalado no tablet", embora ele esteja.
  Causa confirmada: `TabletGet` (`UpdateCommands.cs`) não consultava a versão com o job rodando e devolvia `null`, que a página lia como "não instalado". Agora devolve a última versão conhecida e a página
  mostra "Atualizando o app no tablet…". Teste que segura o job rodando (falha sem a correção). Falta provar numa atualização real por USB.

## 0.4.0: mais fontes de dados

Cada fonte nova segue o fluxo `/novo-plugin-agente` e `/fonte-no-app`, desligada por padrão e com tutorial de configuração (item da 0.2.0).

- [ ] **Agenda:** eventos de várias agendas do Google por link iCal secreto, com aviso de evento chegando (antecedência a definir, ex.: 15 e 5
  min). O link é segredo e vai para o cofre. Pergunta em aberto: a agenda da empresa permite o link iCal?
- [ ] **Rede:** ping, jitter, perda, sinal do tablet, velocidade, gráfico de latência e dispositivos da rede.
- [ ] **Servidores:** VPS (CPU, RAM, disco, uptime; por SSH ou coletor), containers, site no ar e status do GitHub (Actions, PRs, notificações).
- [ ] **Temperatura e potência da CPU:** depende do driver PawnIO e de administrador; precisa de processo auxiliar separado. Instalar driver
  é decisão do mantenedor.
- [ ] **FPS:** fonte a decidir (PresentMon ou RTSS).
- [ ] **Nobreak real** fora do `--simulate`, conferido contra os valores do aparelho. Hoje só há compatibilidade com Intelbras (ver
  "Contribuições bem-vindas").
- [ ] **Notificações do Windows** com texto legível (ainda a confirmar se o texto vem completo).
- [ ] **Tráfego de rede** (`system.net`) na aba Sistema.
- [ ] **Núcleos da CPU:** uso por núcleo lógico na aba Sistema (lista ou grade, um item por núcleo). Campo novo e opcional em `system.cpu`, então
  MINOR e compatível com o v1, mas é mudança de contrato: combinar com o mantenedor antes. Só uso; temperatura e frequência por núcleo ficam fora.
- [ ] **Discord: volume e mudo por pessoa.** `SET_USER_VOICE_SETTINGS` (`volume` de 0 a 200, `mute`; `pan` fica fora). Tocar num membro abre o controle dele.
  Exige o plugin publicar o id do membro (decidir com o mantenedor se vai o id do Discord ou um identificador opaco, já que passa pela rede local), duas
  ações novas (`set_user_volume`, `toggle_user_mute`) e id desconhecido devolvendo `unknown_session` ou `invalid_args`. Mesma regra do slider: envia ao soltar.
- [ ] **Discord: avatar dos membros.** O RPC traz só o hash do avatar (`user.avatar`); a imagem vem do CDN do Discord. O app **não** busca nada da internet: o
  agente baixa, reduz (~64 px) e serve pelo `GET /cover/{hash}` que já existe (mesmo hash de 16 caracteres do SHA-256), e o membro ganha um `avatarHash`
  opcional. Cuidados: o agente só fala com **um endereço fixo** do CDN, definido no código (nunca vindo do conteúdo); falha de rede mantém a inicial no círculo;
  no app, `inSampleSize` e cache pequeno (poucos avatares), nada de `Bitmap` grande. A conferir: formato da URL do avatar e se vale guardar em disco ou só em memória.

## 0.5.0: ações no PC

Primeira vez que o agente faz algo além de ler, então esta versão começa por **desenhar o modelo de segurança das ações** (lista fixa,
declarada por plugin, confirmada para o que for destrutivo) e só depois implementa.

- [ ] **Deck:** grade de atalhos e macros em páginas (Geral, Jogo, Live, PC): mutar mic, trocar saída de áudio, gravar, print, bloquear e
  desligar o PC. Cada ação é declarada, nada executa texto livre vindo do tablet.
- [ ] **Google Meet (#14):** extensão WebExtensions (Chromium primeiro: Edge, Chrome e Brave) que detecta a reunião e age no Meet; o agente
  publica o estado (reunião ativa, microfone, câmera) e aceita mutar, câmera e sair. Pontos a decidir antes de codar: ponte
  (Native Messaging ou endpoint local autenticado), distribuição e instalação da extensão, e o esforço de suportar Firefox. Sem expor porta
  nova na rede local. Fora do escopo: Zoom, Teams, compartilhamento de tela, chat, participantes e gravação.
- [ ] **Mixer por jogo** e cenas por jogo.
- [ ] **Discord: entrar em canal pelo tablet.** `GET_GUILDS`, `GET_CHANNELS` e `SELECT_VOICE_CHANNEL`. Fica aqui porque é ação sobre o PC e combina com o Deck
  (um botão "entrar no canal X"). Lista fixa de ações, nada de id livre vindo do tablet: o agente só entra em canal que ele mesmo listou. Tratar o erro `5003`
  (já está em outro canal; `force` só com aprovação do usuário, então fica de fora). Sair da call já chega na `0.3.0`.

## 0.6.0: plataforma aberta

Para outras pessoas criarem fontes sem mexer no núcleo.

- [ ] **Catálogo de widgets** (número, medidor, anel, gráfico, lista, botão): o plugin declara e o tablet desenha sozinho.
- [ ] **Plugins externos** (processo separado, qualquer linguagem), além dos assemblies .NET dentro do agente. Decidir qual modelo entra primeiro.
- [ ] **Coluna "agora" configurável:** blocos com liga/desliga, ordem e tamanho P/M/G.
- [ ] **Estrutura do repositório:** pastas `plugins/` e `examples/`; guia para escrever plugin; documentação **em português e inglês**.
- [ ] **Jogos:** FFXIV (próximas mecânicas, cooldowns, DPS; via Dalamud ou OverlayPlugin) e LoL (tempo, placar, KDA, CS, ouro, objetivos).
  Jogos extras a definir.
- [ ] Extras: promoções da wishlist da Steam; timers, pomodoro e contagens regressivas.
- [ ] A barra de abas já não comporta uma 7ª aba com ícone em cima: virar rolável ou ter um "Mais".

## 1.0.0: critérios

A `1.0.0` é declarada pelo mantenedor quando:

- o protocolo estiver estável (v1 sem planos de quebra) e documentado;
- o ciclo instalar, onboarding, atualizar e desinstalar tiver sido provado em máquina limpa e com tablet real, nos dois canais de conexão;
- o modelo de plugin estiver fechado e documentado;
- houver documentação em português e inglês.

## Contribuições bem-vindas

- **Outros nobreaks.** O plugin `ups` funciona hoje **só com nobreaks Intelbras, lendo o banco SQLite do software deles** (somente leitura).
  Outras marcas e softwares ainda **não são compatíveis**. Se você tem outro nobreak, um pull request adicionando o suporte é muito bem-vindo:
  abra antes uma issue descrevendo de onde vêm os dados (software, arquivo, porta ou API), mantenha o formato da fonte `ups` do
  `docs/protocolo-v1.md` (campos que a marca não informa vão como `null`) e siga as regras de plugin do `CONTRIBUTING.md`. O plugin atual
  (`Atalaia.Agent.Plugins.Ups`) e o `OutageDebouncer` servem de modelo.

## Ideias sem versão (não escolhidas para o v1)

- Presences de navegador no bloco "agora" (estilo PreMiD): extensão, manifestos declarativos e uma loja em JSON no GitHub. Combina com a
  extensão do Meet; avaliar depois da `0.5.0`.
- Google Tasks, aniversários, Gmail, fotos como protetor de tela, Tailscale, amigos da Steam em jogo, AniList, Jellyfin, Letterboxd,
  resets e timers do FFXIV, modo foco.
- Discord, avaliado e deixado de fora do RPC: `NOTIFICATION_CREATE` (escopo `rpc.notifications.read`; entrega o texto das mensagens, o que o projeto não envia)
  e eventos de mensagem (`MESSAGE_*`, `messages.read` não funciona com `rpc_token` e só vale para canais assinados).
- Anúncio por mDNS (ou IP fixo no roteador).
- Suporte a Firefox na extensão.
- Assinatura de código do Windows.
- Emulador de Android 8.1 no CI.

## Decisões em aberto

1. ~~**O que o tablet mostra quando o PC está desligado**~~ Decidido: **configurável** entre modo noite por horário, relógio sempre e tela apagada.
2. **PC no cabo ou no Wi-Fi:** define se o Wake-on-LAN é viável.
3. **Qual modelo de plugin entra primeiro** (assembly .NET ou processo separado).
4. **Ponte do Meet:** Native Messaging ou endpoint local autenticado.
5. ~~**Lista de endereços que a janela pode abrir** (tutoriais da `0.2.0`)~~ Decidido: mapa fixo no C# (`SourceHelp`), sem mudar o `LinkPolicy`.
6. ~~**Porta do agente (#13)**~~ Decidido: faixa de 10 portas, o app a percorre sozinho na rede local (`docs/protocolo-v1.md`).
7. **Mudança de protocolo** (Wake-on-LAN, pareamento com revogação, campos e ações novas do `discord`): aprovar antes de qualquer código.
8. **Aviso de download ou cópia (`0.3.0`):** o aviso fica na versão (decidido). Em aberto: se a **cópia** entra (exige campo novo de leitura de disco) ou só o download; e se o pareamento entra na `0.3.0` (decidir depois, no ciclo).
9. **Discord por pessoa (`0.4.0`):** publicar o id do Discord do membro ou um identificador opaco, e se o avatar fica só em memória ou também em disco.

## Diferenças em relação ao documento de funcionalidades

O artefato "Painel do PC: funcionalidades decididas" é anterior ao código e está desatualizado em alguns pontos. Para quem for atualizá-lo:

- **Nome:** o projeto se chama **Atalaia** (a decisão "nome público a escolher" está resolvida).
- **Janela do agente:** decidida em **WinForms + WebView2** (o artefato diz "ainda não decidida").
- **O app do tablet já existe** (o artefato diz que não). Tem **6 abas**, não 9 nem 12 telas; a coluna "agora" tem 270 dp.
- **Código de acesso:** 6 dígitos novos a cada início do agente, com limite de tentativas. Não há "pareamento com validade" nem lista de tablets.
- **Sem PIN** na configuração do tablet (previsto no artefato, não feito).
- **Sem alerta de limite, troca de tela por contexto, Wake-on-LAN, agenda, Deck, Servidores, Rede, FFXIV, LoL, Atração e tela inteligente:** todos
  estão nas versões acima.
- **Já feito e não listado no artefato:** onboarding de 6 passos, cofre de segredos (DPAPI), botão "Reportar um bug", arquivos de comunidade,
  instalador e pipeline de release.
- **Repositório:** só existem `agent/`, `app/`, `docs/`, `installer/` e `scripts/`; `protocol/`, `plugins/` e `examples/` ainda não.
