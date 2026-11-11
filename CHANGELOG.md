# Changelog

Mudanças notáveis do Atalaia, por versão estável. As notas de cada beta ficam nas releases do GitHub.
O formato segue o [Keep a Changelog](https://keepachangelog.com/pt-BR/) e as versões seguem o [SemVer](https://semver.org/lang/pt-BR/).

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

