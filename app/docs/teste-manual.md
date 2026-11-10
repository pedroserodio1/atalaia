# Roteiro de teste manual: app do tablet contra o agente real

## Teste rápido (10 minutos, só USB)

Se você só vai fazer um teste, faça este. Prova que o app conecta pelo cabo e se recupera sozinho.

1. **Suba o agente** no PC, com dados falsos e o módulo USB ligado (a partir de `agent/`):
   `$env:ATALAIA_Usb__Enabled="true"; dotnet run --project src/Atalaia.Agent.Host -- --console --simulate`
   O console mostra o **código de acesso** de 6 dígitos (também no menu do ícone da bandeja); guarde-o.
2. **Abra o app no tablet** (cabo ligado). Na primeira vez ele pede o código: digite os 6 dígitos e toque em **Salvar**.
   **Deve aparecer:** **USB** com bolinha verde no canto de cima da coluna da esquerda, e os números mudando.
3. **Puxe o cabo USB do tablet.**
   **Deve aparecer:** "sem conexão" no lugar de USB e uma faixa "PC desligado ou inalcançável".
4. **Recoloque o cabo.**
   **Deve acontecer:** em alguns segundos (até ~8 s) volta sozinho para **USB**, sem você tocar em nada.

Passou nos 4? A parte principal está provada. Se algo diferente aconteceu, anote o que viu e o que esperava.

O resto deste documento é o roteiro completo (Wi-Fi, queda do roteador, ação na troca de canal, etc.), para quando você
quiser testar com mais calma. Ele exige liberar o Firewall do Windows para o Wi-Fi.

---

# Roteiro completo

Confere o que os testes automáticos não alcançam: cabo de verdade, Wi-Fi de verdade e o ciclo de ligar e desligar o PC.
Cada cenário diz o **esperado** e o que **anotar**. Marque `[x]` ao passar e escreva o que viu quando não passar.

## Preparação

- [ ] **PC:** SDK .NET 10 e o `platform-tools` do Android (`adb` no PATH, ou `Usb:AdbPath`).
- [ ] **Tablet:** Android 8.1 ou mais novo, depuração USB ligada e autorizada para este PC (`adb devices` mostra `device`).
- [ ] **App instalado:** `./gradlew :tablet:assembleRelease` em `app/` e `adb install -r tablet/build/outputs/apk/release/tablet-release.apk`.
- [ ] **Agente com dados falsos** (a partir de `agent/`):

  ```bash
  ATALAIA_Usb__Enabled=true dotnet run --project src/Atalaia.Agent.Host -- --console --simulate
  ```

  (No PowerShell: `$env:ATALAIA_Usb__Enabled="true"; dotnet run --project src/Atalaia.Agent.Host -- --console --simulate`.)
  O console mostra o código de 6 dígitos (e o menu da bandeja). Ele **muda a cada início do agente**. Com `--simulate` as 11 fontes ficam ligadas e o nobreak alterna entre rede e bateria sozinho.
- [ ] **Wi-Fi (só para os cenários 4 a 7):** ligue `Agent:ListenOnLan` em `agent/appsettings.Local.json`
  (`{ "Agent": { "ListenOnLan": true }, "Usb": { "Enabled": true } }`), reinicie o agente e **permita** o aviso do Firewall do
  Windows para rede privada. Descubra o IP do PC (`ipconfig`, o IPv4 da placa que o tablet alcança) e confirme que o tablet
  está na mesma rede. Em todo cenário abaixo, `<IP-DO-PC>` é esse endereço.
- [ ] Anote o modelo do tablet, se o Wi-Fi dele é 2,4 ou 5 GHz, e se o PC está no cabo ou no Wi-Fi.

Tempos de referência (do projeto): `ping` a cada 2 s; sem `pong` por 5 s derruba a conexão; backoff entre voltas de 1, 2, 4,
8 e 15 s; o app testa o canal preferido (USB) a cada 5 s enquanto está no Wi-Fi; o agente confere o `adb reverse` a cada 3 s;
dado sem `update` por mais de 15 s aparece como "velho".

## 1. Primeira configuração

1. Abra o app. **Esperado:** a tela de configuração abre sozinha, com `127.0.0.1:8765` na lista.
2. Adicione `<IP-DO-PC>` (aparece como Wi-Fi, na posição 2). Digite o código e toque em **Salvar**.
3. **Esperado:** o painel abre e o canto da coluna "agora" mostra **USB**; os cartões enchem em poucos segundos.
4. Recuse de propósito: tente adicionar `8.8.8.8`. **Esperado:** mensagem "Só aceito endereços da rede local…".
5. Feche e abra o app. **Esperado:** mantém a configuração e reconecta sozinho; o código nunca aparece na tela.

## 2. USB normal

1. Com o cabo ligado e o agente rodando, navegue pelas seis abas. **Esperado:** todas desenham dados; nenhuma falha.
2. Mídia: tocar/pausar, próxima, anterior e toque na barra de posição. **Esperado:** o agente reage (o dado muda em ≤ 1 s).
3. Mixer: arraste o volume de um app e toque em "mudo". Discord: mic mudo e volumes. **Esperado:** o valor fica no que você
   soltou e o agente confirma.
4. Log do agente (`%LOCALAPPDATA%\AtalaiaAgent\logs`): **Esperado:** uma linha `USB: reverse … criado`, sem repetir a cada rodada.

## 3. Desplugar o cabo (só USB configurado)

Tire o Wi-Fi da lista (deixe só `127.0.0.1:8765`) para ver o estado sem alternativa.

1. Desconecte o cabo. **Esperado:** em poucos segundos o canal vira "sem conexão" e a faixa mostra "PC desligado ou
   inalcançável. Tentando de novo…". Depois de 15 s sem `update`, os cartões ficam "velho" e o jogo some da coluna.
2. Reconecte o cabo. **Esperado:** volta a **USB** sozinho, sem tocar em nada: em ~3 a 6 s se o cabo ficou fora pouco tempo, e
   em até ~18 s se ficou fora por mais de um minuto e o app estava sem nenhum canal (3 s do agente refazer o reverse mais o backoff, que chega a 15 s).
3. Anote: quantos segundos levou a perda e a volta (com o cabo fora por pouco e por muito tempo).

## 4. Failover USB para Wi-Fi (USB e Wi-Fi configurados)

1. Com os dois na lista (USB primeiro) e o cabo ligado. **Esperado:** canal **USB**.
2. Desconecte o cabo. **Esperado:** troca para **Wi-Fi** quase na hora (sem passar por "inalcançável"); dados continuam.
3. Mídia tocando: **Esperado:** a música e os cartões não ficam "velhos" na troca.
4. Anote o tempo da troca e se houve piscada de "sem conexão".

## 5. Volta ao USB

1. Estando no Wi-Fi do cenário 4, reconecte o cabo. **Esperado:** em até ~8 s (3 s do agente + sonda de 5 s) o canal volta
   para **USB**, e a conexão do Wi-Fi é fechada (o agente mostra "Clientes conectados: 1" na bandeja).
2. Repita desplugando e plugando várias vezes seguidas. **Esperado:** sem tela travada nem loop de trocas; se o cabo
   oscilar, a sonda espera mais (até 2 min).

## 6. Queda silenciosa do Wi-Fi

1. Fique no Wi-Fi (cabo desplugado). No tablet, **desligue o Wi-Fi** nas configurações (ou tire o roteador da tomada).
   **Esperado:** em ~7 s (2 s até o ping + 5 s sem resposta) o app dá a conexão como morta: "sem conexão" e depois
   "PC desligado ou inalcançável".
2. Ligue o Wi-Fi de novo. **Esperado:** reconecta sozinho em alguns segundos (backoff de até 15 s).
3. Anote o tempo da detecção e da volta.

## 7. Ação em andamento na troca (o ponto delicado)

O app nunca reenvia uma ação sozinho. Teste com uma ação que dê para perceber:

1. No Wi-Fi, com mídia tocando, toque em **próxima** e, no mesmo instante, desplugue/replugue o cabo para forçar a troca.
2. **Esperado:** a faixa avança **no máximo uma vez** (nunca duas). Se a conexão caiu antes da resposta, o cartão mostra
   "sem confirmação" por 3 s; olhe o agente para saber se a ação executou.
3. Anote se a ação executou, não executou, ou executou duas vezes (esse último é um defeito).

## 8. PC e agente fora do ar (rotina de desligar à noite)

1. Feche o agente (bandeja, Sair). **Esperado:** "PC desligado ou inalcançável"; em 15 s os cartões ficam "velho".
2. Com o app aberto, suba o agente de novo. **Esperado:** o app volta sozinho, sem toque, pelo canal preferido disponível.
3. Desligue o PC e ligue de novo. **Esperado:** o tablet fica no estado de espera sem erro e **reconecta sozinho** depois que
   o agente subir (lembre: o agente ainda não inicia com o Windows).
4. Anote: o tablet ficou aceso a noite toda? A bateria se manteve (carregador ligado)?

## 9. Código de acesso e configuração

1. Em **Configuração**, salve um token errado. **Esperado:** em poucos segundos a faixa vira "Código inválido…" e o app para de
   insistir; tocar na faixa abre a configuração.
2. Salve o código certo. **Esperado:** conecta na hora, sem reiniciar o app.
3. Toque longo no relógio abre a configuração. Reordene os endereços com ▲ ▼. **Esperado:** a prioridade muda e vale na
   próxima conexão.
4. **Reiniciar o agente:** feche e suba o agente de novo. **Esperado:** o código muda; o app mostra "Código inválido" e, com o código novo na configuração, volta a conectar. (Pelo USB isso vale igual: o código é sempre conferido.)
5. **Limite de tentativas (só pelo Wi-Fi):** erre o código 5 vezes seguidas pelo Wi-Fi. **Esperado:** a partir daí o agente recusa tudo desse endereço por 1 min, até com o código certo; o USB continua funcionando.
6. **Colar:** copie o código no PC, envie por algum canal ao tablet, copie lá e use **Colar**. **Esperado:** o campo é preenchido.

## 10. Alerta de queda de energia

Com `--simulate` o nobreak alterna entre rede e bateria.

1. **Esperado:** quando passa para a bateria (e dura mais que o anti-ruído), o **cartão do relógio** (coluna da esquerda) fica **coral** e mostra
   "Sem energia da rede · NN%", em qualquer aba, sem nada mudar de lugar; quando volta, o cartão volta ao normal.
2. Com o nobreak real (sem `--simulate`, `Plugins:ups:enabled=true`): puxe o nobreak da tomada por mais de 5 s.
   **Esperado:** o alerta liga; ao religar, desliga. Reconectar o app no meio da queda também mostra o alerta.

## 11. Dados reais (sem `--simulate`)

1. Suba o agente real e confira cada cartão contra o Windows: CPU/RAM/disco (Gerenciador de Tarefas), GPU (nvidia-smi),
   volume e mixer (ícone de volume), faixa e capa (o que está tocando), janela em foco, nobreak.
2. **Esperado:** valores batendo; plugin sem configuração (ex.: Steam sem chave) aparece "erro" ou "sem dados", sem derrubar o resto.
3. Notificações: **cuidado**, "Limpar tudo" descarta as notificações do Windows e não dá para desfazer (o app pede confirmação).

## 12. Desempenho no tablet

Com o app de release aberto por alguns minutos, a partir de `app/`:

```powershell
powershell -ExecutionPolicy Bypass -File tools\measure-perf.ps1 -Minutes 30
```

**Esperado** (referência medida em 10/11 com o agente simulado): memória (PSS) abaixo de ~60 MB e estável, CPU abaixo de ~3%,
partida a frio abaixo de ~1 s. Anote qualquer crescimento contínuo de memória: o teste longo (8 h) ainda não foi feito.

## Resultado

| Cenário | Passou? | Observação |
|---|---|---|
| 1 Primeira configuração | | |
| 2 USB normal | | |
| 3 Desplugar o cabo | | |
| 4 Failover USB para Wi-Fi | | |
| 5 Volta ao USB | | |
| 6 Queda silenciosa do Wi-Fi | | |
| 7 Ação na troca | | |
| 8 PC e agente fora do ar | | |
| 9 Código e configuração | | |
| 10 Queda de energia | | |
| 11 Dados reais | | |
| 12 Desempenho | | |
