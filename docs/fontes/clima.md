# Clima: Como achar a latitude e a longitude

Este texto é o mesmo que a janela do Atalaia mostra em **Como conseguir** (Fontes de dados). Se um passo não bate mais com o site, avise numa issue: as telas dos serviços mudam.

Página do serviço: <https://www.openstreetmap.org/>

## Passo a passo

1. O clima não usa chave nem conta (é o Open-Meteo, gratuito).
2. Abra o mapa, procure o seu bairro e dê zoom até ver o lugar. O endereço da página passa a ter algo como #map=15/10.5/-20.25: o primeiro número depois do zoom é a latitude e o segundo é a longitude.
3. Digite com ponto, não com vírgula (-23.55, e não -23,55). Sul e oeste são números negativos.
4. Privacidade: só essas duas coordenadas saem deste PC, e só para o Open-Meteo.

## Se der errado

- Valor recusado: a latitude fica entre -90 e 90, a longitude entre -180 e 180, sempre com ponto.
- Sem resposta: pode ser a internet ou o Open-Meteo fora do ar. Tente de novo em instantes.

Na janela, o botão **Testar conexão** confere o que você preencheu na hora, sem ligar a fonte.
