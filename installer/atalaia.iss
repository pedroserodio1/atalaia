; SPDX-License-Identifier: GPL-3.0-or-later
; Copyright (C) 2026 Pedro Serôdio
;
; Instalador do agente do Atalaia (Inno Setup 6). Por usuário (sem UAC), .NET autocontido, sem assinatura de código.
; Não rode direto: use scripts/build-installer.ps1, que publica o agente e passa os parâmetros abaixo.
;   /DAppVersion=X.Y.Z[-beta.N]   (obrigatório)
;   /DPublishDir=<pasta do dotnet publish>   /O<pasta de saída>

#ifndef AppVersion
  #error Informe /DAppVersion=X.Y.Z ou X.Y.Z-beta.N (use scripts/build-installer.ps1).
#endif
#ifndef PublishDir
  #define PublishDir "..\dist\publish"
#endif

#define IconeAgente "..\agent\src\Atalaia.Agent.Host\Assets\atalaia.ico"
#define ImagemGrande "..\assets\brand\instalador-grande.bmp"
#define ImagemPequena "..\assets\brand\instalador-pequena.bmp"

[Setup]
; AppId fixo para sempre: é o que faz uma versão nova instalar por cima da anterior.
AppId={{571D6283-DD82-487E-B841-20B27023BEFA}
AppName=Atalaia
AppVersion={#AppVersion}
AppVerName=Atalaia {#AppVersion}
AppPublisher=Atalaia (código aberto)
AppPublisherURL=https://github.com/pedroserodio1/atalaia
DefaultDirName={autopf}\Atalaia
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
OutputBaseFilename=Atalaia-Agent-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
; Fecha o agente em execução antes de trocar os arquivos (Restart Manager) e não reinicia o Windows.
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\AtalaiaAgent.exe
#ifexist IconeAgente
SetupIconFile={#IconeAgente}
#endif
#ifexist ImagemGrande
WizardImageFile={#ImagemGrande}
#endif
#ifexist ImagemPequena
WizardSmallImageFile={#ImagemPequena}
#endif

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{autoprograms}\Atalaia"; Filename: "{app}\AtalaiaAgent.exe"

[Registry]
; "Iniciar com o Windows" é uma chave do usuário criada pelo próprio agente; aqui só garantimos que a desinstalação a remova.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Atalaia"; ValueType: none; Flags: uninsdeletevalue

[Run]
; Instalação normal: oferece abrir. Atualização feita pelo agente (silenciosa, com /REABRIR=1): reabre sozinho.
Filename: "{app}\AtalaiaAgent.exe"; Description: "Abrir o Atalaia"; Flags: nowait postinstall skipifsilent
Filename: "{app}\AtalaiaAgent.exe"; Flags: nowait; Check: ReabrirAposAtualizar

[Code]
function ReabrirAposAtualizar: Boolean;
begin
  Result := WizardSilent and (ExpandConstant('{param:REABRIR|0}') = '1');
end;

// A pasta %LOCALAPPDATA%\AtalaiaAgent (configurações, logs) NÃO é apagada ao desinstalar.
