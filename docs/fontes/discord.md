# Discord: Como criar o aplicativo do Discord

Este texto é o mesmo que a janela do Atalaia mostra em **Como conseguir** (Fontes de dados). Se um passo não bate mais com o site, avise numa issue: as telas dos serviços mudam.

Página do serviço: <https://discord.com/developers/applications>

## Passo a passo

1. Abra o Portal de Desenvolvedores do Discord, entre com a sua conta e toque em New Application. Dê o nome que quiser.
2. Em General Information, copie o Application ID e cole em Client ID.
3. Em OAuth2, em Client Secret, toque em Reset Secret, copie o valor (ele aparece uma vez só) e cole em Client Secret. Aqui ele fica criptografado neste PC.
4. Ainda em OAuth2, em Redirects, adicione http://localhost e salve.
5. Deixe o Discord para computador aberto (o navegador não vale). Na primeira vez, ele pede uma autorização: você tem 3 minutos para aceitar, com a mesma conta que criou o aplicativo.

## Se der errado

- Discord fechado: abra o Discord para computador e teste de novo.
- Credenciais recusadas: confira o Client ID e gere o segredo de novo com Reset Secret.
- O teste não abre o pedido de autorização: ele só aparece quando a fonte liga com o Discord aberto.

Na janela, o botão **Testar conexão** confere o que você preencheu na hora, sem ligar a fonte.
