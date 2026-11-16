// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 Pedro Serôdio

namespace Atalaia.Agent.Host.Sources;

/// <summary>
/// O tutorial de uma fonte: como conseguir as chaves e o que conferir quando dá errado. O texto é fixo e embutido no agente (nada é baixado
/// de fora para exibi-lo). As páginas de <c>docs/fontes/</c> repetem o mesmo texto e um teste confere que não divergem.
/// Exemplos só com valores falsos. As telas dos sites mudam: se um passo não bate mais, atualize aqui e na página de <c>docs/fontes/</c>.
/// </summary>
/// <param name="PageLabel">Texto do botão que abre a página do serviço, ou <c>null</c> se não há página para abrir.</param>
internal sealed record SourceHelp(string Title, IReadOnlyList<string> Steps, IReadOnlyList<string> IfWrong, string? PageLabel)
{
    /// <summary>
    /// Os endereços que o botão "Abrir página" pode abrir: uma lista fechada, definida aqui, nunca vinda de configuração nem de conteúdo remoto.
    /// A página não manda endereço nenhum: manda só o id da fonte.
    /// </summary>
    private static readonly Dictionary<string, string> Pages = new(StringComparer.Ordinal)
    {
        ["steam"] = "https://steamcommunity.com/dev/apikey",
        ["discord"] = "https://discord.com/developers/applications",
        ["weather"] = "https://www.openstreetmap.org/",
        ["calendar"] = "https://calendar.google.com/calendar/r/settings",
        ["anilist"] = "https://anilist.co/",
    };

    private static readonly Dictionary<string, SourceHelp> All = new(StringComparer.Ordinal)
    {
        ["steam"] = new(
            "Como conseguir a chave e o SteamID64",
            [
                "Abra a página da chave da Steam e entre com a sua conta.",
                "Em Domain Name escreva qualquer coisa (por exemplo, localhost), marque que concorda com os termos e toque em Register.",
                "Copie a chave (32 letras e números) e cole em Chave da Web API. Ela é secreta: não mostre a ninguém. Aqui ela fica criptografada neste PC.",
                "Para o SteamID64, abra o seu perfil na Steam. Se o endereço tem /profiles/ e 17 números, esses números são o SteamID64. Se tem /id/ e um nome, procure o seu SteamID64 por esse nome em um site de busca de SteamID.",
                "Em Editar perfil, Privacidade, deixe Detalhes do jogo como Público. Sem isso, biblioteca, jogos recentes e amigos aparecem como \"oculto no perfil\".",
            ],
            [
                "Chave recusada: confira se copiou inteira e sem espaços. A Steam não deixa gerar chave em conta limitada (conta nova que nunca gastou nada na loja).",
                "SteamID64 errado: ele tem exatamente 17 dígitos, só números.",
                "Perfil oculto: deixe Detalhes do jogo como Público e teste de novo.",
            ],
            "Abrir a página da chave"),

        ["discord"] = new(
            "Como criar o aplicativo do Discord",
            [
                "Abra o Portal de Desenvolvedores do Discord, entre com a sua conta e toque em New Application. Dê o nome que quiser.",
                "Em General Information, copie o Application ID e cole em Client ID.",
                "Em OAuth2, em Client Secret, toque em Reset Secret, copie o valor (ele aparece uma vez só) e cole em Client Secret. Aqui ele fica criptografado neste PC.",
                "Ainda em OAuth2, em Redirects, adicione http://localhost e salve.",
                "Deixe o Discord para computador aberto (o navegador não vale). Na primeira vez, ele pede uma autorização: você tem 3 minutos para aceitar, com a mesma conta que criou o aplicativo.",
            ],
            [
                "Discord fechado: abra o Discord para computador e teste de novo.",
                "Credenciais recusadas: confira o Client ID e gere o segredo de novo com Reset Secret.",
                "O teste não abre o pedido de autorização: ele só aparece quando a fonte liga com o Discord aberto.",
            ],
            "Abrir o Portal do Discord"),

        ["weather"] = new(
            "Como achar a latitude e a longitude",
            [
                "O clima não usa chave nem conta (é o Open-Meteo, gratuito).",
                "Abra o mapa, procure o seu bairro e dê zoom até ver o lugar. O endereço da página passa a ter algo como #map=15/10.5/-20.25: o primeiro número depois do zoom é a latitude e o segundo é a longitude.",
                "Digite com ponto, não com vírgula (-23.55, e não -23,55). Sul e oeste são números negativos.",
                "Privacidade: só essas duas coordenadas saem deste PC, e só para o Open-Meteo.",
            ],
            [
                "Valor recusado: a latitude fica entre -90 e 90, a longitude entre -180 e 180, sempre com ponto.",
                "Sem resposta: pode ser a internet ou o Open-Meteo fora do ar. Tente de novo em instantes.",
            ],
            "Abrir o mapa"),

        ["calendar"] = new(
            "Como pegar o link iCal secreto da agenda",
            [
                "Abra as configurações do Google Agenda no computador e, na lateral, escolha a agenda em Configurações das minhas agendas.",
                "Em Integrar agenda, copie o Endereço secreto no formato iCal (um link que começa com https:// e termina em basic.ics).",
                "Toque em Configurar, escreva um nome para a agenda (por exemplo, Trabalho) e cole o link. O link é o segredo: quem o tem lê a agenda. Aqui ele fica criptografado neste PC. Para mais de uma, toque em Adicionar agenda (até 5): a cor de cada uma no tablet segue a ordem da lista. O nome é só seu; o do arquivo, que pode ser o seu e-mail, nunca é usado.",
                "Privacidade: só o título, o horário e o nome da agenda de cada evento vão para o tablet, pela rede local. Local, descrição e participantes não saem deste PC.",
                "Para invalidar um link que vazou, use Redefinir o endereço secreto na mesma tela e cole o novo.",
            ],
            [
                "Link recusado: ele precisa começar com https:// e estar inteiro, sem espaços no meio. O nome tem no máximo 24 caracteres.",
                "Agenda vazia ou sem resposta: confira a internet e se o link não foi redefinido.",
                "Agenda da empresa: o administrador pode bloquear o endereço secreto, e aí ele não aparece em Integrar agenda.",
            ],
            "Abrir o Google Agenda"),

        ["anilist"] = new(
            "Como achar o seu usuário do AniList",
            [
                "Entre em anilist.co e abra o seu perfil: o endereço é anilist.co/user/NOME. O NOME é o que vai em Configurar.",
                "Cole só o nome (sem o endereço e sem @). Não precisa de senha, chave nem login: a fonte lê só o que o seu perfil mostra para qualquer pessoa.",
                "O perfil precisa ser público. Se você marcou o perfil como privado nas configurações do AniList, a fonte não enxerga nada.",
                "Privacidade: do perfil saem para o tablet só as contagens, os gêneros e estúdios mais vistos e os títulos das últimas atividades. Texto, mensagens e notificações do AniList não são lidos.",
            ],
            [
                "Usuário não encontrado: confira a grafia do nome no endereço do seu perfil.",
                "Sem dados: confira a internet. Se o perfil foi criado agora ou ainda não tem atividades, os números e a lista ficam vazios.",
                "Limite de pedidos: o AniList limita quantos pedidos por minuto aceita; a fonte pede uma vez a cada 10 minutos e tenta de novo sozinha.",
            ],
            "Abrir o AniList"),

        ["network"] = new(
            "Como funcionam os alvos da Rede",
            [
                "A Rede mede o tempo de resposta (ping) até alguns endereços e mostra, no tablet, o tempo atual, a média, o jitter (a variação entre respostas) e a perda de pacotes dos últimos 60 segundos.",
                "Em Configurar, cada linha é um alvo: um nome (só seu) e o endereço. O endereço é a palavra gateway (o roteador desta máquina) ou um IPv4 com os quatro números, como 1.1.1.1. Nome de site não vale. Até 4 alvos.",
                "Para saber se o problema é o Wi-Fi ou o provedor, use um alvo no roteador (gateway) e um na internet (por exemplo, 1.1.1.1 ou 8.8.8.8): se só o da internet piora, é o provedor.",
                "Aviso no relógio do tablet: com um alvo gateway e pelo menos um alvo da internet, se todos os da internet ficam sem resposta por uns 6 segundos enquanto o roteador responde, o relógio mostra Internet fora; se nenhum alvo responde, mostra Sem rede. Sai sozinho quando a resposta volta.",
                "Privacidade: só o nome do alvo vai para o tablet, nunca o endereço. O agente só envia pacotes de ping, sem administrador, e só para os alvos que você escolheu.",
            ],
            [
                "Perda de 100% no roteador: alguns roteadores ignoram ping. Troque o alvo por outro endereço da sua rede ou deixe só os da internet.",
                "Endereço recusado: ele precisa ser gateway ou quatro números separados por ponto, sem espaços. O nome tem no máximo 24 caracteres.",
                "Gateway sem resposta: com VPN ou várias placas de rede, o gateway escolhido pode não ser o do roteador. Use o endereço do roteador no lugar de gateway.",
            ],
            null),

        ["ups"] = new(
            "Quando o nobreak funciona",
            [
                "A fonte lê o banco do software de gerenciamento de nobreak da Intelbras. Ele precisa estar instalado e já ter sido aberto uma vez neste PC.",
                "O Atalaia só lê esse banco. Não conecta no nobreak nem mexe em nada.",
                "Se você tem esse nobreak, ligue a chave ao lado.",
            ],
            [
                "Banco não encontrado: o software não está instalado ou guarda o banco em outra pasta. Informe o caminho em Plugins:ups:dbPath, no arquivo de configuração.",
            ],
            null),
    };

    /// <summary>O tutorial da fonte, ou <c>null</c> se ela não tem.</summary>
    public static SourceHelp? For(string sourceId) => All.GetValueOrDefault(sourceId);

    /// <summary>O endereço da página do serviço da fonte, ou <c>null</c>. Só da lista fechada acima.</summary>
    public static string? PageUrl(string sourceId) => Pages.GetValueOrDefault(sourceId);
}
