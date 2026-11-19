# Rede: Como funcionam os alvos da Rede

Este texto é o mesmo que a janela do Atalaia mostra em **Como conseguir** (Fontes de dados). Se um passo não bate mais com o site, avise numa issue: as telas dos serviços mudam.

## Passo a passo

1. A Rede mede o tempo de resposta (ping) até alguns endereços e mostra, no tablet, o tempo atual, a média, o jitter (a variação entre respostas) e a perda de pacotes dos últimos 60 segundos.
2. Em Configurar, cada linha é um alvo: um nome (só seu) e o endereço. O endereço é a palavra gateway (o roteador desta máquina) ou um IPv4 com os quatro números, como 1.1.1.1. Nome de site não vale. Até 4 alvos.
3. Para saber se o problema é o Wi-Fi ou o provedor, use um alvo no roteador (gateway) e um na internet (por exemplo, 1.1.1.1 ou 8.8.8.8): se só o da internet piora, é o provedor.
4. Aviso no relógio do tablet: com um alvo gateway e pelo menos um alvo da internet, se todos os da internet ficam sem resposta por uns 6 segundos enquanto o roteador responde, o relógio mostra Internet fora; se nenhum alvo responde, mostra Sem rede. Sai sozinho quando a resposta volta.
5. Privacidade: só o nome do alvo vai para o tablet, nunca o endereço. O agente só envia pacotes de ping, sem administrador, e só para os alvos que você escolheu.

## Se der errado

- Perda de 100% no roteador: alguns roteadores ignoram ping. Troque o alvo por outro endereço da sua rede ou deixe só os da internet.
- Endereço recusado: ele precisa ser gateway ou quatro números separados por ponto, sem espaços. O nome tem no máximo 24 caracteres.
- Gateway sem resposta: com VPN ou várias placas de rede, o gateway escolhido pode não ser o do roteador. Use o endereço do roteador no lugar de gateway.
