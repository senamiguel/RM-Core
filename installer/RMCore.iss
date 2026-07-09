; ============================================================
;  RM Core — Inno Setup Script
;  Gera "RM-Core-Setup.exe" (~1.5MB wrapper)
;  - Instala o app em Program Files\RM Core\
;  - Detecta .NET 9 Desktop Runtime; baixa e instala se faltar
;  - Cria atalhos (Menu Iniciar, Área de trabalho opcional)
;  - Registra uninstaller
; ============================================================

#define MyAppName "RM Core"
#define MyAppPublisher "Miguel Sena"
#define MyAppURL "https://github.com/senamiguel/RM-Core"
#define MyAppExeName "RM Core.exe"
#define MyAppVersion "1.0.0"

[Setup]
AppId={{B6E2A8C1-5D7F-4E3A-9B1C-7F2D8E4A6B5C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DisableProgramGroupPage=yes
LicenseFile=
InfoBeforeFile=
InfoAfterFile=
OutputDir=..\installer\dist
OutputBaseFilename=RM-Core-Setup-{#MyAppVersion}
SetupIconFile=..\RM Core\RM_CORE.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppVersion}
MinVersion=10.0

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na &Área de Trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked

[Files]
Source: "..\RM Core\bin\Release\net9.0-windows10.0.18362.0\RM Core.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\RM Core\bin\Release\net9.0-windows10.0.18362.0\RM Core.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\RM Core\bin\Release\net9.0-windows10.0.18362.0\RM Core.deps.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\RM Core\bin\Release\net9.0-windows10.0.18362.0\RM Core.runtimeconfig.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\RM Core\RM_CORE.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

[Code]

// ============================================================
//  Detecta .NET 9 Desktop Runtime via registry
// ============================================================

function IsDotNet9DesktopInstalled(): Boolean;
var
  SubKeys: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetSubkeyNames(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', SubKeys) then
  begin
    for I := 0 to GetArrayLength(SubKeys) - 1 do
    begin
      if (Length(SubKeys[I]) >= 2) and (Copy(SubKeys[I], 1, 2) = '9.') then
      begin
        Result := True;
        Exit;
      end;
    end;
  end;
end;

// ============================================================
//  Baixa e instala .NET 9 via PowerShell (nativo do Windows)
//  Usa Invoke-WebRequest + dotnet-install.ps1 oficial
// ============================================================

function InstallDotNet9DesktopRuntime(): Boolean;
var
  TempDir: String;
  ScriptPath: String;
  InstPath: String;
  PSCommand: String;
  ResultCode: Integer;
begin
  Result := False;

  // Cria pasta temporária
  TempDir := ExpandConstant('{tmp}\rmcore_dotnet');
  if not CreateDir(TempDir) then
  begin
    MsgBox('Nao foi possivel criar pasta temporaria.', mbError, MB_OK);
    Exit;
  end;

  ScriptPath := TempDir + '\dotnet-install.ps1';
  InstPath := 'C:\Program Files\dotnet';

  // PowerShell: baixa o script oficial (usando aspas simples no comando
  // pra nao conflitar com as aspas duplas da shell externa)
  PSCommand :=
    '[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12; ' +
    'Invoke-WebRequest -Uri ''https://dot.net/v1/dotnet-install.ps1'' -OutFile ''' + ScriptPath + '''';

  if not Exec('powershell.exe',
    '-NoProfile -ExecutionPolicy Bypass -Command "' + PSCommand + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('Falha ao baixar dotnet-install.ps1. Verifique sua conexao.', mbError, MB_OK);
    Exit;
  end;

  if ResultCode <> 0 then
  begin
    MsgBox('Falha no download. Codigo: ' + IntToStr(ResultCode), mbError, MB_OK);
    Exit;
  end;

  // Executa o script para instalar o Windows Desktop Runtime 9.0
  if not Exec('powershell.exe',
    '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath +
    '" -Runtime windowsdesktop -Version 9.0.0 -InstallPath "' + InstPath + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('Falha ao executar dotnet-install.ps1. Erro de permissao?', mbError, MB_OK);
    Exit;
  end;

  if ResultCode = 0 then
  begin
    MsgBox('.NET 9 Desktop Runtime instalado com sucesso!', mbInformation, MB_OK);
    Result := True;
  end
  else
  begin
    MsgBox('A instalacao do .NET 9 falhou (codigo ' + IntToStr(ResultCode) + ').', mbError, MB_OK);
  end;
end;

// ============================================================
//  Inicialização — pergunta antes de começar
// ============================================================

function InitializeSetup(): Boolean;
begin
  Result := True;

  if not IsDotNet9DesktopInstalled() then
  begin
    if MsgBox(
      'O RM Core precisa do .NET 9 Desktop Runtime para funcionar.' + #13#10 + #13#10 +
      'Nao foi detectado na sua maquina.' + #13#10 + #13#10 +
      'Deseja baixar e instalar agora? (~55 MB)' + #13#10 + #13#10 +
      '(Se voce ja tem o .NET 9 instalado em local diferente, prossiga.)',
      mbConfirmation, MB_YESNO) = IDYES then
    begin
      if not InstallDotNet9DesktopRuntime() then
      begin
        if MsgBox(
          'A instalacao do .NET 9 falhou ou foi cancelada.' + #13#10 +
          'Continuar a instalacao do RM Core mesmo assim (o app pode nao funcionar)?',
          mbConfirmation, MB_YESNO) = IDNO then
        begin
          Result := False;
        end;
      end;
    end;
  end;
end;

function NeedRestart(): Boolean;
begin
  Result := False;
end;
