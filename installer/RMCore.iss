; ============================================================
;  RM Core - Inno Setup Script
;  Gera "RM-Core-Setup-Alpha-0.6.7.exe"
;  - Instala o app em Program Files\RM_CORE\
;  - Detecta .NET 9 Desktop Runtime; baixa e instala se faltar
;  - Cria atalhos (Menu Iniciar, Area de trabalho opcional)
;  - Registra uninstaller
; ============================================================

#define MyAppName "RM_CORE"
#define MyAppPublisher "Miguel Sena"
#define MyAppURL "https://github.com/senamiguel/RM-Core"
#define MyAppExeName "RM_CORE.exe"
#define MyAppVersion "Alpha-0.6.11"
#define MyAppNumericVersion "0.6.11.0"

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
OutputDir=dist
OutputBaseFilename=RM-Core-Setup-{#MyAppVersion}
SetupIconFile=..\RM_CORE\RM_CORE.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
VersionInfoVersion={#MyAppNumericVersion}
VersionInfoTextVersion={#MyAppVersion}
MinVersion=10.0
CloseApplications=force
CloseApplicationsFilter=*RM_CORE.exe*

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na &Área de Trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked

[Files]
Source: "stage\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\tools\*"; DestDir: "{app}\tools"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall runasoriginaluser skipifsilent; WorkingDir: "{app}"

[Code]

// ============================================================
//  Detecta .NET 9 Desktop Runtime via registry
// ============================================================

function HasDotNet9Subkey(const RegKey: String): Boolean;
var
  SubKeys: TArrayOfString;
  I: Integer;
begin
  Result := False;
  if RegGetSubkeyNames(HKLM, RegKey, SubKeys) then
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

function IsDotNet9DesktopInstalled(): Boolean;
var
  SearchPath: String;
  FindRec: TFindRec;
begin
  // 1) Registro (instalacao oficial registra aqui)
  if HasDotNet9Subkey('SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App') or
     HasDotNet9Subkey('SOFTWARE\WOW6432Node\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App') then
  begin
    Result := True;
    Exit;
  end;

  // 2) Fallback: checa a pasta de runtime (instalado por dotnet-install.ps1 ou custom path)
  SearchPath := 'C:\Program Files\dotnet\shared\Microsoft.WindowsDesktop.App';
  if DirExists(SearchPath) and FindFirst(SearchPath + '\9.*', FindRec) then
  begin
    FindClose(FindRec);
    Result := True;
    Exit;
  end;

  Result := False;
end;

// ============================================================
//  Baixa e instala .NET 9 via PowerShell (nativo do Windows)
//  Escreve o script em disco e chama com -File (sem problemas de escape)
// ============================================================

function SaveStringToFile(const FileName, Contents: String): Boolean;
var
  Lines: TArrayOfString;
begin
  SetArrayLength(Lines, 1);
  Lines[0] := Contents;
  Result := SaveStringsToFile(FileName, Lines, False);
end;

function InstallDotNet9DesktopRuntime(): Boolean;
var
  TempDir: String;
  ScriptPath: String;
  PS1: String;
  InstPath: String;
  LogPath: String;
  ResultCode: Integer;
begin
  Result := False;

  TempDir := ExpandConstant('{tmp}\rmcore_dotnet');
  if not CreateDir(TempDir) then
  begin
    MsgBox('Nao foi possivel criar pasta temporaria.', mbError, MB_OK);
    Exit;
  end;

  ScriptPath := TempDir + '\dotnet-install.ps1';
  LogPath    := TempDir + '\install.log';

  // Script PowerShell escrito em disco (evita problemas de aspas no Exec)
  PS1 :=
    '[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12' + #13#10 +
    '$ProgressPreference = ''SilentlyContinue''' + #13#10 +
    'try {' + #13#10 +
    '  Invoke-WebRequest -Uri ''https://dot.net/v1/dotnet-install.ps1'' -OutFile ''__SCRIPT__'' -UseBasicParsing -MaximumRedirection 5' + #13#10 +
    '  if (-not (Test-Path ''__SCRIPT__'')) { throw ''download falhou (arquivo nao criado)'' }' + #13#10 +
    '  & __SCRIPT__ -Runtime windowsdesktop -Version 9.0.0 -InstallPath ''__INSTPATH__'' 2>&1 | Tee-Object -FilePath ''__LOGPATH__'' | Out-Null' + #13#10 +
    '  exit $LASTEXITCODE' + #13#10 +
    '} catch {' + #13#10 +
    '  Add-Content -Path ''__LOGPATH__'' -Value (''ERRO: '' + $_.Exception.Message)' + #13#10 +
    '  exit 1' + #13#10 +
    '}';

  InstPath := 'C:\Program Files\dotnet';
  StringChangeEx(PS1, '__SCRIPT__',  ScriptPath, True);
  StringChangeEx(PS1, '__INSTPATH__', InstPath,    True);
  StringChangeEx(PS1, '__LOGPATH__', LogPath,     True);

  if not SaveStringToFile(TempDir + '\runner.ps1', PS1) then
  begin
    MsgBox('Nao foi possivel escrever o script PowerShell.', mbError, MB_OK);
    Exit;
  end;

  if not Exec('powershell.exe',
    '-NoProfile -ExecutionPolicy Bypass -File "' + TempDir + '\runner.ps1"',
    '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('Falha ao executar PowerShell. Veja o log em:' + #13#10 + LogPath, mbError, MB_OK);
    Exit;
  end;

  if ResultCode = 0 then
  begin
    MsgBox('.NET 9 Desktop Runtime instalado com sucesso!', mbInformation, MB_OK);
    Result := True;
  end
  else
  begin
    MsgBox('A instalacao do .NET 9 falhou (codigo ' + IntToStr(ResultCode) + ').' + #13#10 +
           'Veja o log completo em:' + #13#10 + LogPath, mbError, MB_OK);
  end;
end;

// ============================================================
//  Inicializacao - pergunta antes de comecar
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
