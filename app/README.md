# Atalaia (app do tablet)

App Android que mostra o painel do PC. Conecta no agente (`../agent`) pelo protocolo v1
([`../docs/protocolo-v1.md`](../docs/protocolo-v1.md)), por USB (`adb reverse`) ou Wi-Fi, trocando sozinho de um para o outro.
O nome exibido no tablet e o identificador do app (`applicationId`) vêm de `appName` e `appId` em `gradle.properties`.

Alvo: tablet T68, Android 8.1 (API 27), 32 bits (`armeabi-v7a`), tela útil 862 × 505 dp em paisagem.

## O que o app faz

- **Coluna "agora"** (em todas as telas): relógio, canal ativo (USB, Wi-Fi ou sem conexão), jogo ou janela em foco, mídia com
  controles.
- **Navegação em submenus:** a barra de cima mostra os contextos (Início, Sistema, Mídia, Jogo e Comunicação). Tocar num que tem filhos abre a tela principal dele e a barra **troca** por `‹ voltar` e os botões dele
  (hoje a Mídia, com Tocando agora, Mixer e Anime, e a Comunicação, com Pessoas e a Agenda, que tem Dia, Semana e Mês: submenu dentro de submenu; cada fonte nova entra como filho). A profundidade não tem limite fixo: a tela é um caminho de ids (`media/mixer`), definido em `Nav.kt` no `:core`. **Segurar** um botão que tem filhos abre um arco por cima da tela
  (sem empurrar nada); arrastar até um dos botões e soltar vai direto para ele, e soltar fora cancela.
- **Anime (Mídia › Anime):** o perfil público do AniList (a fonte `anilist`, desligada por padrão, **sem login nem chave**: só o nome de usuário na janela do agente). Só mostra: contagens (animes, episódios, tempo assistido, mangás, capítulos), as **últimas atividades** ("Assistiu ep. 5 · há 3 h"), gêneros e estúdios mais vistos. A tradução e as contas de tempo ficam em `AniList.kt` no `:core`.
- **Agenda (Comunicação › Agenda):** eventos de até 5 agendas por link iCal secreto (a fonte `calendar` do agente, desligada por padrão). Três visões, na barra de cima: **Dia** (lista, com os de dia inteiro no alto e o próximo evento em destaque),
  **Semana** (sete colunas, de domingo a sábado) e **Mês** (grade com barrinhas por agenda). Tocar num dia da semana ou do mês abre um **modal** por cima, com setas para o dia anterior e o seguinte e "Abrir o dia".
  Cada agenda tem o nome que o usuário deu (nunca o de dentro do arquivo) e uma cor pela ordem; os chips da legenda ligam e desligam cada uma na tela. **Aviso:** nos últimos 15 min antes de um evento o cartão do relógio
  fica menta com "Em N min · título", e o tablet toca um bipe em 15 min e dois em 5 min (liga e desliga na configuração, "Som do aviso de agenda"; só toca com o app aberto e com dado fresco). O aviso olha todas as agendas, mesmo as desligadas na tela.
- **Telas:** Início, Sistema, Mídia (Tocando agora, com a capa, e Mixer por aplicativo), Jogo (Steam) e Comunicação (o Discord). Na Comunicação a faixa da esquerda tem os botões de microfone e fones (ensurdecer desliga o microfone também) e os volumes em pé; a lista da call fica à direita, com o número de pessoas
  no cabeçalho e os estados por ícone (som para quem fala, microfone cortado para mudo, microfone e fones cortados para surdo).
- **Conexão:** lista de endereços em ordem de prioridade (padrão: USB em `127.0.0.1`, depois o IP do PC no Wi-Fi). Uma conexão
  por vez; quando cai, tenta o próximo na hora; no Wi-Fi, testa o USB a cada 5 s e volta quando ele fica pronto.
- **Segurança:** só aceita endereços de loopback e rede local; o token fica cifrado no Keystore e não é exibido de volta.
  Uma ação que ficou sem resposta por queda de conexão nunca é reenviada sozinha.
- **Alertas:** o cartão do relógio vira coral cheio ("Sem energia da rede · 80%") enquanto o nobreak está na bateria, sem ocupar espaço nem mexer no layout; "velho", "desligado" ou "erro" em vez de número desatualizado.
  **Limites:** GPU quente (85 °C), RAM no fim (90 %) e disco cheio (90 %) acendem a borda do relógio e trocam a linha do jogo por "RAM no fim · 93%" (com "+N" se houver mais de um).
  Só liga depois de 10 s acima do limite e só desliga 5 pontos abaixo dele; os limites (ou "Desligado") se ajustam na configuração.
- **PC desligado:** depois de 1 min sem achar o PC (a troca USB e Wi-Fi não conta), a tela vira, conforme a configuração, **modo noite** (relógio escuro no horário escolhido, padrão 22h às 07h;
  fora dele o painel segue normal), **relógio** sempre ou **tela apagada** (preta, brilho mínimo, tela ainda ligada). Voltou a conectar, volta o painel. Segurar na tela abre a configuração.
- **Aviso de download:** tráfego de rede do PC a partir de 30 Mbps (configurável) por 2 min seguidos conta como download; quando cai abaixo de 2 Mbps por 30 s o relógio mostra "Download terminou"
  por 10 min ou até um toque. Deduzido só de `system.net.rxKbps`, sem mudar o protocolo.
- **Ligar PC (Wake-on-LAN):** com o PC desligado e um MAC de cabo guardado (o agente publica `system.net.mac`; Wi-Fi não vale), a tela de PC desligado mostra o botão redondo **Ligar PC**, que manda o pacote mágico (UDP, broadcast, portas 9 e 7) pela rede do tablet. Na tela apagada, um toque mostra o botão por 15 s. Cada pedido, com o desfecho, vai para `wol.log` na pasta do app (`Android/data/org.atalaia.app/files/`, as últimas ~100 linhas, MAC mascarado, sem IP) e para o `logcat` (tag `AtalaiaWol`). O registro só existe a partir da 0.3.0-beta.7, só no tablet, e reinstalar o app o apaga.
- **Tela Atração:** com o PC conectado e 10 min sem toque, o painel vira um relógio com a data, o estado do PC e "Jogado hoje" (tempo com `steam.game` ativo visto pelo tablet, só em memória); um toque sai.
  Não entra por cima de queda de energia, alerta de limite ou call. Desliga na configuração.
- **Troca de tela por contexto:** jogo da Steam que abre leva à tela Jogo; call no Discord (sem jogo aberto) leva à Comunicação; depois de 5 min sem toque volta ao Início, com a barra na raiz (menos se a tela
  é a do contexto ainda ativo; já estar dentro do contexto, mesmo num submenu, não puxa a tela). Só reage a um começo (reconectar com jogo ou call em andamento não puxa a tela, nem quem está mexendo). Ligada por padrão; desliga na configuração.
- **PIN da configuração (opcional):** 4 a 6 dígitos pedidos ao abrir a configuração (menos na primeira); guarda só sal e hash, cinco erros seguidos trancam por 30 s. Esqueceu: reinstalar o app.
- **Configuração:** abre sozinha na primeira vez, por toque longo no relógio ou tocando na faixa de erro de conexão.

## Estrutura

```
core/       Kotlin/JVM puro: protocolo, conexão, failover, estado, regras da configuração (testável sem emulador)
tablet/     Android + Compose: telas, configuração, tela cheia
tools/      measure-perf.ps1 (memória, CPU, quadros e partida a frio no tablet, por adb)
docs/       teste-manual.md (roteiro com cabo e Wi-Fi reais)
third-party/   licenças das fontes (OFL)
```

## Compilar e testar

Requer JDK 17+ (testado com 21) e o Android SDK com a plataforma 37 e build-tools 36. Informe o SDK por
`ANDROID_HOME` ou por um `local.properties` com `sdk.dir=...` (esse arquivo não vai para o git).
O Gradle é baixado pelo wrapper (versão e checksum fixos).

```bash
./gradlew :core:test                                        # testes (JVM, sem emulador)
./gradlew :core:build :tablet:assembleDebug :tablet:assembleRelease
```

APKs em `tablet/build/outputs/apk/`. O release sai assinado com a chave de debug (só para instalar e medir; APK
assinado de verdade está fora do escopo por enquanto).

```bash
adb install -r tablet/build/outputs/apk/release/tablet-release.apk
```

Versões de todas as dependências ficam em `gradle/libs.versions.toml`, sem faixas. O `:core` compila o código de produção
com `-Xjdk-release=8`: o Android 8.1 só tem a biblioteca do Java 8, e uma API mais nova vira erro de compilação em vez de
derrubar o app no tablet.

## Usar

1. Suba o agente (`../agent`; `--simulate` para dados falsos). Por USB, ligue `Usb:Enabled` no agente para ele manter o
   `adb reverse` sozinho (ou faça `adb reverse tcp:8765 tcp:8765` à mão). Por Wi-Fi, ligue `Agent:ListenOnLan` e libere o
   Firewall do Windows.
2. Abra o app: a configuração abre sozinha. (Por Wi-Fi, se a porta 8765 do PC estiver ocupada, o agente usa a próxima da faixa e o app a procura sozinho, de 8765 a 8774.) Informe os endereços e o código de 6 dígitos do agente (no PC: menu do ícone da bandeja; ele muda toda vez que o agente reinicia).
3. Em build de debug, dá para configurar por `adb`:
   `adb shell am start -n org.atalaia.app/.MainActivity --es hosts "127.0.0.1:8765,192.168.0.12" --es token "..."`
   (ignorado em release).
4. **Abre sozinho ao ligar o tablet:** o app declara `RECEIVE_BOOT_COMPLETED` (permissão normal, sem aviso) e um `BootReceiver` que só abre a tela
   e termina, sem serviço em segundo plano. Funciona no Android 8.1 (o alvo). **Do Android 10 em diante o sistema impede abrir uma tela a partir do segundo
   plano**, e o app simplesmente não abre sozinho (nada quebra: é só tocar no ícone). Para esses aparelhos seria preciso "Exibir sobre outros apps"
   (`SYSTEM_ALERT_WINDOW`) ou ser o app inicial do aparelho, o que não está feito.
   **Demora:** o painel abre uns 25 a 30 s depois de o Android terminar de ligar, porque o aviso de boot é entregue aos apps um por vez (medido no T68).
   **Tire a tela de bloqueio** (Configurações, Segurança, Bloqueio de tela, Nenhum): com ela ligada, o painel abre atrás dela e é preciso deslizar para ver.

## Testes e verificação

- **Automáticos:** `:core:test` (307 testes: protocolo, conexão com servidor falso e com OkHttp real, failover, estado,
  configuração) e `dotnet test` em `../agent` (114, inclusive o módulo USB com `adb` falso).
- **Manual, com cabo e Wi-Fi reais:** [`docs/teste-manual.md`](docs/teste-manual.md).
- **Desempenho no tablet:** `tools/measure-perf.ps1`.
- O que já foi e o que ainda não foi verificado está consolidado em [`../docs/plano-app.md`](../docs/plano-app.md), seção 11.
