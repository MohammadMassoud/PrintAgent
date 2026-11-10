; Machine-wide installer. Application connections are added automatically by the service.
[Setup]
AppId={{1035B4C9-6996-4AC4-9F03-52D3B53DE48F}
AppName=SinergiaPrint Agent
AppVersion=1.1.0
AppPublisher=SinergiaAI
AppSupportURL=https://www.sinergiaai.com/
DefaultDirName={autopf}\SinergiaPrint
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=..\artifacts
OutputBaseFilename=SinergiaPrint-Setup
Compression=lzma2
SolidCompression=yes
CloseApplications=no
SetupLogging=yes
SetupIconFile=..\branding\sinergia.ico
UninstallDisplayIcon={app}\SinergiaAI.PrintAgent.Agent.exe
WizardStyle=modern
LanguageDetectionMethod=uilanguage
ShowLanguageDialog=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
english.LaunchDesktop=Launch SinergiaAI PrintAgent
brazilianportuguese.LaunchDesktop=Abrir SinergiaAI PrintAgent

[Files]
Source: "..\artifacts\agent\*"; DestDir: "{app}"; Excludes: "appsettings.Local*.json,applications.json,*.pdb,*.db,*.db-shm,*.db-wal,*.lock"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\scripts\install.ps1"; DestDir: "{tmp}"; DestName: "prepare-install.ps1"; Flags: dontcopy

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "SinergiaAI PrintAgent"; ValueData: """{app}\approval\SinergiaAI.PrintAgent.Approval.exe"" --background"; Flags: uninsdeletevalue
Root: HKLM; Subkey: "Software\Classes\sinergia-printagent"; ValueType: string; ValueData: "URL:PrintAgent website authorization"; Flags: uninsdeletekey
Root: HKLM; Subkey: "Software\Classes\sinergia-printagent"; ValueType: string; ValueName: "URL Protocol"; ValueData: ""
Root: HKLM; Subkey: "Software\Classes\sinergia-printagent\shell\open\command"; ValueType: string; ValueData: """{app}\approval\SinergiaAI.PrintAgent.Approval.exe"" ""%1"""

[Icons]
Name: "{commonprograms}\SinergiaAI PrintAgent"; Filename: "{app}\approval\SinergiaAI.PrintAgent.Approval.exe"; AppUserModelID: "SinergiaAI.PrintAgent"

[Run]
Filename: "{app}\approval\SinergiaAI.PrintAgent.Approval.exe"; Parameters: "--background"; Description: "{cm:LaunchDesktop}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\uninstall.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveSinergiaPrintService"

[Code]
var
  ExternalConfig: String;

procedure InitializeWizard();
begin
  ExternalConfig := ExpandConstant('{param:CONFIGFILE|}');
  if (ExternalConfig <> '') and not FileExists(ExternalConfig) then
    RaiseException('Automatic configuration was not found. Download the installer again.');
  WizardForm.FinishedLabel.Caption :=
    'PrintAgent was installed as a Windows service. Return to your application and refresh the connection.' + #13#10 + #13#10 +
    'Open SinergiaAI PrintAgent from the Start menu to manage its service and website permissions.';
  if ActiveLanguage = 'brazilianportuguese' then
    WizardForm.FinishedLabel.Caption :=
      'O PrintAgent foi instalado como um serviço Windows. Volte ao aplicativo e atualize a conexão.' + #13#10 + #13#10 +
      'Abra SinergiaAI PrintAgent pelo menu Iniciar para gerenciar o serviço e as permissões dos sites.';
end;

function RunInstallScript(ScriptPath: String; ExtraArguments: String): Boolean;
var
  ExitCode: Integer;
  Arguments: String;
begin
  Arguments := '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptPath + '"';
  if ExternalConfig <> '' then Arguments := Arguments + ' -ConfigurationFile "' + ExternalConfig + '"';
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    Arguments + ExtraArguments, '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Result := Result and (ExitCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  ExtractTemporaryFile('prepare-install.ps1');
  if not RunInstallScript(ExpandConstant('{tmp}\prepare-install.ps1'), ' -ValidateOnly') then
  begin
    Result := 'The existing or supplied configuration is invalid. No service was changed.';
    Exit;
  end;
  if not RunInstallScript(ExpandConstant('{tmp}\prepare-install.ps1'), ' -PrepareOnly') then
    Result := 'Could not stop the previous agent version. Stop the agent before continuing.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    if not RunInstallScript(ExpandConstant('{app}\install.ps1'), '') then
      RaiseException('Could not start the PrintAgent Windows service. Installation did not complete.');
end;
