# Assinatura do APK do tablet

O APK de **release** que a esteira publica (`Atalaia-Tablet-<versão>.apk`) é assinado com uma chave própria do projeto. O Android só
atualiza um app por cima de outro se os dois forem assinados com a **mesma chave**. Por isso:

- **Guarde o keystore e as senhas com cuidado.** Perder a chave significa que ninguém mais consegue atualizar o app já instalado por
  cima: todo mundo teria que desinstalar e reinstalar (e perderia a configuração do app).
- A chave **nunca** vai para o repositório, para uma issue, um PR ou um log. Só vive no seu computador (cópia de segurança fora dele) e
  nos *secrets* do GitHub.

## 1. Gerar o keystore (uma vez, no seu PC)

Precisa do `keytool` (vem com o JDK 17 ou mais novo). Escolha **você** as senhas e **anote num gerenciador de senhas**:

```bash
keytool -genkeypair -v -keystore atalaia-release.jks -alias atalaia -keyalg RSA -keysize 4096 -validity 10000
```

Ele pergunta as senhas (do keystore e da chave) e os dados do certificado. Use dados que possam ser públicos (o certificado vai dentro
do APK). Guarde o `atalaia-release.jks` **fora** da pasta do projeto e faça uma cópia de segurança em outro lugar (outro disco ou um
cofre de senhas).

## 2. Cadastrar os secrets no GitHub

No repositório: **Settings → Environments → New environment → `release`**. Dentro dele, em *Environment secrets*, crie quatro:

| Secret | Valor |
|---|---|
| `ATALAIA_KEYSTORE_BASE64` | o `.jks` em base64 (comando abaixo) |
| `ATALAIA_KEYSTORE_PASSWORD` | a senha do keystore |
| `ATALAIA_KEY_ALIAS` | o alias (`atalaia`, se usou o comando acima) |
| `ATALAIA_KEY_PASSWORD` | a senha da chave |

Para gerar o valor do primeiro (PowerShell; o resultado vai para a área de transferência, e **não** é exibido na tela):

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\caminho\fora\do\projeto\atalaia-release.jks")) | Set-Clipboard
```

Em *Deployment branches and tags* do environment, limite a `development`, `beta/*` e às tags `v*`: só esses ramos e tags poderão usar os secrets.

## 3. Como a esteira usa

- `release.yml` (job `app`) grava o keystore num arquivo temporário do runner e passa `ATALAIA_KEYSTORE_PATH`,
  `ATALAIA_KEYSTORE_PASSWORD`, `ATALAIA_KEY_ALIAS` e `ATALAIA_KEY_PASSWORD` só para o Gradle. O `signingConfig` de `release` em
  `app/tablet/build.gradle.kts` lê essas variáveis.
- **Release publicada sem keystore é recusada.** O ensaio (`dry_run`) sem o secret ainda constrói, mas o APK sai com a chave de debug e a
  esteira avisa; esse APK não serve para publicar.
- Depois do build a esteira roda `apksigner verify` e **recusa** um APK de release assinado com a chave de debug.
- Build local (`./gradlew :tablet:assembleRelease` sem as variáveis) continua saindo com a chave de debug, só para instalar e medir no tablet.

## 4. Efeito no APK que já está no tablet

O APK instalado hoje (versão 0.0.1) foi assinado com a **chave de debug**. O primeiro APK assinado com a chave de release tem uma
assinatura diferente, e o Android recusa a atualização por cima (`INSTALL_FAILED_UPDATE_INCOMPATIBLE`). Isso acontece **uma vez**:

1. Desinstale o app do tablet (**isso apaga a configuração do app**: endereços e código de acesso; é preciso configurar de novo).
2. Instale o APK da release.
3. A partir daí, todas as atualizações (`adb install -r`, pelo agente ou à mão) funcionam por cima e mantêm a configuração.

O passo "App no tablet" do agente detecta esse erro, explica com estas palavras e só desinstala depois da sua confirmação.

## 5. Se precisar trocar a chave um dia

Não existe troca transparente: o Android não aceita outra chave por cima. Seria uma versão nova do app que exige desinstalar a antiga
(como na seção 4). Por isso o cuidado com o keystore.
