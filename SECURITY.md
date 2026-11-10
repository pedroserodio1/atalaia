# Política de segurança

## Versões que recebem correção

O Atalaia está em beta (`0.y.z`). Só a **versão mais recente** (estável ou beta) recebe correção de segurança.

## Como relatar uma vulnerabilidade

**Não abra uma issue pública** para uma falha de segurança.

Use o relato privado do GitHub: aba **Security** do repositório, **Report a vulnerability**
(<https://github.com/pedroserodio1/atalaia/security/advisories/new>). Só o mantenedor vê o relato.

Conte o que for possível: versão do agente e do app, passos para reproduzir e o impacto que você enxerga. Não inclua o código de
acesso, tokens nem chaves reais de ninguém: use valores inventados.

O mantenedor responde assim que puder; é um projeto pessoal e de código aberto, sem prazo garantido. Corrigida a falha, ela sai numa
versão nova e o relato é publicado com crédito a quem o enviou (se você quiser).

## O que o projeto já assume

- O agente só **lê** o sistema do usuário; as únicas ações são as declaradas por cada plugin, e nunca existe comando livre.
- A conexão entre agente e tablet **não usa TLS**: é USB (loopback) ou rede local, protegida por um código de 6 dígitos. Por isso o
  app só aceita endereços de loopback e de rede local. Não exponha a porta do agente à internet.
- Segredos digitados na janela (chave da Steam, `clientSecret` do Discord) ficam no cofre do Windows (DPAPI, escopo do usuário), nunca
  no `settings.json`, no log nem de volta na página.
- A atualização automática baixa só de HTTPS, só do repositório configurado, e confere o SHA-256 contra o `SHA256SUMS.txt` da mesma
  release. O instalador do Windows **não é assinado** nesta fase, então o SmartScreen avisa na instalação.
