# Changelog

Mudanças notáveis do Atalaia, por versão estável. As notas de cada beta ficam nas releases do GitHub.
O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/) e as versões seguem o [SemVer](https://semver.org/lang/pt-BR/).

## [0.4.0] - 2026-11-19

### Novidades

- **app:** Navegação em submenus com arco ao segurar uma aba (#88)
- Agenda por link iCal (agente e app), com modal do dia, aviso e som (#91)
- Lista de agendas na janela e Agenda como aba da raiz (#93)
- **app:** Agenda vira filha da Comunicação e o aviso do relógio abre a Agenda (#95)
- Fonte AniList (agente e tela Mídia › Anime) (#96)
- **agent:** Fonte Rede (ping, jitter e perda até o roteador e a internet) (#100)
- **app:** Tela Rede em Sistema com latência, jitter e perda por alvo (#101)
- Uso por núcleo da CPU (system.cpu.cores e cartão na tela Sistema) (#105)
- Alerta "internet caiu" no cartão do relógio (gateway no alvo da Rede) (#109)
- **app:** Gráfico de tráfego (descida e subida) no alto da tela Rede (#110)
- Foto dos membros da call do Discord (avatarHash) (#111)
- Volume e mudo por pessoa na call do Discord (#115)

### Correções

- **app:** Arco que travava aberto ao soltar o dedo e aba Tocando agora cortada (#90)
- **app:** Estatísticas da Rede sem corte e ruim só com perda, oscilação ou ping muito alto (#104)
- **app:** Cor das barras da Rede segue a mesma regra da qualidade (#108)
- **agent:** Atualização usa o feed e os arquivos do github.com quando a API estoura o limite (#118)

### Outros

- Anime sem a lista "assistindo agora" (tela, agente e contrato) (#99)
## [0.3.0] - 2026-11-15

### Novidades

- **app:** Alertas de limite de GPU, RAM e disco (#57)
- **app:** Tela de PC desligado configurável (modo noite, relógio ou tela apagada) (#60)
- **app:** Troca de tela por contexto (jogo, call e volta ao Início) (#62)
- **app:** Aba Discord com faixa de controles, volumes em pé e estados por ícone (#64)
- **app:** Aviso de download terminando no PC (#69)
- **app:** PIN opcional para abrir a configuração do tablet (#70)
- **app:** Tela Atração com relógio, estado do PC e jogado hoje (#71)
- **discord:** Ping da call e sair da call pelo tablet (#74)
- Wake-on-LAN pelo tablet com o MAC da placa de cabo (#77)

### Correções

- **agent:** A janela não diz mais que o app não está instalado durante a atualização por USB (#66) (#68)
- **ci:** Notas da release estável sem grupos repetidos (#23) (#76)


## [0.2.0] - 2026-11-13

### Novidades

- **agent:** Manter o mesmo código de acesso entre reinícios e gerar novo (#36)
- **agent:** Abrir o app no tablet após instalar por USB e avisar quando o código mudar (#39)
- Faixa de portas para o agente não cair com a porta ocupada (#13) (#42)
- **agent:** Tutoriais das fontes, Abrir página e Testar conexão (#44)
- **app:** Abrir o painel sozinho quando o tablet liga (#46)

### Outros

- Abre o ciclo 0.2.0
- Marca o roadmap (0.1.2 fechada, 0.2.0 em beta) e cria a regra de manter o roadmap em dia (#38)
- Roadmap com a v0.2.0-beta.2 publicada
- Roadmap da 0.2.0 todo feito (beta.5) e notas do app abrindo no boot (#48)

## [0.1.2] - 2026-11-12

### Correções

- Mantém o "jogando agora" da Steam enquanto o jogo segue aberto (#12) (#24)
- Nobreak deixa de ficar "na bateria" quando a rede volta com o PC desligado (#17) (#26)
- Tablet mostra o bloqueio por tentativas erradas e reconecta sozinho (#16) (#31)

### Outros

- Abre o ciclo 0.1.2
- Ajusta o ROADMAP da 0.1.2 (#13 adiada para a 0.2.0, núcleos da CPU na 0.4.0) (#25)
- ROADMAP da 0.2.0 ganha abrir o app depois da instalação por USB (#28)
- Desativa a fonte Claude (agente e app) (#29)

## [0.1.1] - 2026-11-11

### Novidades

- **agent:** Agente do PC (marco 1)
- **app:** App do tablet com conexão USB e Wi-Fi e telas Início, Sistema, Mídia e Discord
- **app:** Abas Jogo (Steam) e Claude, com tempo até o reinício
- **app:** Tela de configuração com endereços em ordem de prioridade e token
- **agent:** Módulo USB opcional que mantém o adb reverse para o tablet
- **app:** Alerta de queda de energia no cartão do relógio, sem faixa
- **agent:** Código de acesso de 6 dígitos novo a cada início, com limite de tentativas
- **app:** Textos do código de acesso de 6 dígitos e docs atualizadas
- **agent:** Janela do agente com WebView2, onboarding e settings.json (#2)
- **agent:** Onboarding com os seis passos reais (rede, USB, app, código, fontes, resumo) (#3)
- **agent:** Telas de configuração (Conexão, Fontes, Jogos e ações, Atualizações) (#4)
- **agent:** Atualização automática (checagem, instalador conferido e app do tablet por USB) (#5)
- **agent:** Configurar clima, Steam e Discord pela janela, com segredos no cofre do Windows
- **agent:** Configurar clima, Steam e Discord pela janela, com segredos no cofre do Windows (#9)
- **agent:** Botão "Reportar um bug" na janela, que abre o formulário de nova issue

### Correções

- **agent:** Leitura do adb sem esperar o fim da saída e log quando o adb fica sem aparelho
- **ci:** Confere o versionCode do APK sem exigir as senhas do keystore
- **ci:** Confere o versionCode do APK sem exigir as senhas do keystore (#8)
- **ci:** Notas da release usam o SHA da tag como fim da faixa
- **ci:** No ensaio a tag simulada não existe, então o fim da faixa continua sendo o GITHUB_SHA
- **ci:** Notas da release usam o SHA da tag como fim da faixa (#10)
- **agent:** Card "App do tablet" parava de piscar na aba Atualizações

### Outros

- Gitignore da raiz e instruções do projeto
- Protocolo v1, especificação do marco 1 e plano do app
- **probe:** Sondagem antiga, somente leitura
- README geral do projeto
- Licença GPL-3.0
- **agent:** Número de série fictício no teste do adb
- Renomeia o projeto para Atalaia
- Licença GPL-3.0 ou posterior, NOTICE, avisos de terceiros e cabeçalhos SPDX
- CONTRIBUTING, regras e skills do Claude Code para contribuir com plugins
- Marca a versão 0.0.1 (beta)
- Abre o ciclo 0.1.1 com versionamento, esteira de release e marca (#1)
- Integra o ciclo 0.1.1 em development (#7)
- Arquivos de comunidade (conduta, segurança, formulários de issue e modelo de pull request)
- ROADMAP.md e README com ramos, versões e compatibilidade de nobreak

