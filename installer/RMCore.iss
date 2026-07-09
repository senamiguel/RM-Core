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
; Binários do build Release (Costura.Fody já embedded as DLLs)
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

[UninstallRun]
; Nada a fazer no uninstall

[UninstallDelete]
; Limpa settings/window pos (opcional - mantém histórico)
; Type: filesandordirs; Name: "{localappdata}\RM_Core"

[Code]
// ============================================================
// Helpers
// ============================================================

function IsDotNet9DesktopInstalled(): Boolean;
var
  Key: String;
  SubKeys: TArrayOfString;
  I: Integer;
  Version: String;
begin
  Result := False;
  // Verifica versões do Microsoft.WindowsDesktop.App >= 9.0
  // Path: HKLM\SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App
  if RegKeyExists(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App') then
  begin
    if RegGetSubkeyNames(HKLM, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', SubKeys) then
    begin
      for I := 0 to GetArrayLength(SubKeys) - 1 do
      begin
        Version := SubKeys[I];
        // Version string format: "9.0.0" — checa se começa com "9."
        if (Length(Version) >= 2) and (Copy(Version, 1, 2) = '9.') then
        begin
          Result := True;
          Exit;
        end;
      end;
    end;
  end;

  // Fallback: checa no user-level também
  if not Result then
  begin
    if RegKeyExists(HKCU, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App') then
    begin
      if RegGetSubkeyNames(HKCU, 'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App', SubKeys) then
      begin
        for I := 0 to GetArrayLength(SubKeys) - 1 do
        begin
          Version := SubKeys[I];
          if (Length(Version) >= 2) and (Copy(Version, 1, 2) = '9.') then
          begin
            Result := True;
            Exit;
          end;
        end;
      end;
    end;
  end;
end;

function InstallDotNet9DesktopRuntime(): Boolean;
var
  TempDir: String;
  ScriptPath: String;
  InstPath: String;
  InstallPage: TDownloadWizardPage;
  ResultCode: Integer;
begin
  Result := False;
  TempDir := ExpandConstant('{tmp}\rmcore_dotnet_install');
  if not ForceDirectories(TempDir) then
  begin
    MsgBox('Não foi possível criar pasta temporária: ' + TempDir, mbError, MB_OK);
    Exit;
  end;

  ScriptPath := TempDir + '\dotnet-install.ps1';
  InstPath := 'C:\Program Files\dotnet';

  // Baixa o script oficial de instalação (URL estável: dot.net/v1/dotnet-install.ps1)
  if not DownloadTemporaryFile(
    'https://dot.net/v1/dotnet-install.ps1',
    ScriptPath, '', nil) then
  begin
    MsgBox('Falha ao baixar dotnet-install.ps1. Verifique sua conexão com a internet.', mbError, MB_OK);
    Exit;
  end;

  // Roda o script PowerShell como admin, instalando Windows Desktop Runtime
  // -Runtime windowsdesktop: instala só o WPF (menor que o ASP.NET Core completo)
  // -Version 9.0.0: fixa na versão 9 (atual LTS-like pra WPF)
  // -InstallPath: C:\Program Files\dotnet (padrão Windows)
  if not Exec('powershell.exe',
    '-NoProfile -ExecutionPolicy Bypass -File "' + ScriptPath +
    '" -Runtime windowsdesktop -Version 9.0.0 -InstallPath "' + InstPath + '"',
    '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('Falha ao executar dotnet-install.ps1. Erro de permissão?', mbError, MB_OK);
    Exit;
  end;

  if ResultCode = 0 then
  begin
    MsgBox('.NET 9 Desktop Runtime instalado com sucesso!', mbInformation, MB_OK);
    Result := True;
  end
  else
  begin
    MsgBox('A instalação do .NET 9 falhou (código ' + IntToStr(ResultCode) + '). O app pode não rodar.', mbWarning, MB_OK);
  end;
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;

  // Welcome page customizada
  // (pode customizar aqui se quiser)

  if not IsDotNet9DesktopInstalled() then
  begin
    if MsgBox(
      'O RM Core precisa do .NET 9 Desktop Runtime para funcionar.' + #13#10 + #13#10 +
      'Não foi detectado na sua máquina.' + #13#10 + #13#10 +
      'Deseja baixar e instalar agora? (~55MB)' + #13#10 + #13#10 +
      '(Se você já tem mas em versão diferente, pode prosseguir e tentar abrir o app)',
      mbConfirmation, MB_YESNO) = IDYES then
    begin
      if not InstallDotNet9DesktopRuntime() then
      begin
        if MsgBox('A instalação do .NET 9 falhou ou foi cancelada. Continuar a instalação do RM Core mesmo assim?',
          mbConfirmation, MB_YESNO) = IDNO then
        begin
          Result := False;
        end;
      end;
    end;
    // Se disse não, continua mesmo assim (app pode não funcionar)
  end;
end;

function NeedRestart(): Boolean;
begin
  Result := False; // Não força reboot; pede pro user reiniciar manualmente se preciso
end;
