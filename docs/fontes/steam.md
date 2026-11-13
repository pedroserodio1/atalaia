# Steam: Como conseguir a chave e o SteamID64

Este texto é o mesmo que a janela do Atalaia mostra em **Como conseguir** (Fontes de dados). Se um passo não bate mais com o site, avise numa issue: as telas dos serviços mudam.

Página do serviço: <https://steamcommunity.com/dev/apikey>

## Passo a passo

1. Abra a página da chave da Steam e entre com a sua conta.
2. Em Domain Name escreva qualquer coisa (por exemplo, localhost), marque que concorda com os termos e toque em Register.
3. Copie a chave (32 letras e números) e cole em Chave da Web API. Ela é secreta: não mostre a ninguém. Aqui ela fica criptografada neste PC.
4. Para o SteamID64, abra o seu perfil na Steam. Se o endereço tem /profiles/ e 17 números, esses números são o SteamID64. Se tem /id/ e um nome, procure o seu SteamID64 por esse nome em um site de busca de SteamID.
5. Em Editar perfil, Privacidade, deixe Detalhes do jogo como Público. Sem isso, biblioteca, jogos recentes e amigos aparecem como "oculto no perfil".

## Se der errado

- Chave recusada: confira se copiou inteira e sem espaços. A Steam não deixa gerar chave em conta limitada (conta nova que nunca gastou nada na loja).
- SteamID64 errado: ele tem exatamente 17 dígitos, só números.
- Perfil oculto: deixe Detalhes do jogo como Público e teste de novo.

Na janela, o botão **Testar conexão** confere o que você preencheu na hora, sem ligar a fonte.
