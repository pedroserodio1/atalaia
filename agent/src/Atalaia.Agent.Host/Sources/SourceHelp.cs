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
