; Generic public installer: never includes installation-specific credentials.
[Setup]
AppId={{1035B4C9-6996-4AC4-9F03-52D3B53DE48F}
AppName=SinergiaPrint Agent
AppVersion=0.4.0
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
WizardStyle=modern

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
Source: "..\artifacts\agent\*"; DestDir: "{app}"; Excludes: "appsettings.Local*.json,*.pdb,*.db,*.db-shm,*.db-wal,*.lock"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\scripts\install.ps1"; DestDir: "{tmp}"; DestName: "prepare-install.ps1"; Flags: dontcopy

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\uninstall.ps1"""; Flags: runhidden waituntilterminated; RunOnceId: "RemoveSinergiaPrintService"

[Code]
var
  ConnectionPage: TInputQueryWizardPage;
  ExistingPage: TInputOptionWizardPage;
  ExistingConfig: Boolean;
  TemporaryConfig: String;
  ExternalConfig: String;

function KeepExisting(): Boolean;
begin
  Result := (ExternalConfig = '') and ExistingConfig and ExistingPage.Values[0];
end;

function JsonEscape(Value: String): String;
begin
  StringChangeEx(Value, '\', '\\', True);
  StringChangeEx(Value, '"', '\"', True);
  StringChangeEx(Value, #13, '\r', True);
  StringChangeEx(Value, #10, '\n', True);
  StringChangeEx(Value, #9, '\t', True);
  Result := Value;
end;

procedure InitializeWizard();
begin
  ExternalConfig := ExpandConstant('{param:CONFIGFILE|}');
  if (ExternalConfig <> '') and not FileExists(ExternalConfig) then
    RaiseException('Automatic configuration was not found. Download the installer again from the application.');
  ExistingConfig := FileExists(ExpandConstant('{commonappdata}\SinergiaPrint\appsettings.Local.json'));
  ExistingPage := CreateInputOptionPage(wpWelcome, 'Agent connection',
    'An existing configuration was found.',
    'Keep the current connection, or clear this option to connect to another application.', False, False);
  ExistingPage.Add('Keep the current configuration');
  ExistingPage.Values[0] := True;
  ConnectionPage := CreateInputQueryPage(ExistingPage.ID, 'Connect to your application',
    'Enter the connection details provided by the application that will use these printers.',
    'Obtain the API URL and credentials from your application, not the download website.');
  ConnectionPage.Add('Printing API URL (https://...):', False);
  ConnectionPage.Add('Installation ID (GUID):', False);
  ConnectionPage.Add('Agent token:', True);
  WizardForm.FinishedLabel.Caption :=
    'PrintAgent was installed as a Windows service. Return to the application and refresh the printer list.' + #13#10 + #13#10 +
    'The agent starts automatically with Windows. Your application API must be available for it to connect.';
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if PageID = ExistingPage.ID then Result := not ExistingConfig or (ExternalConfig <> '');
  if PageID = ConnectionPage.ID then Result := KeepExisting() or (ExternalConfig <> '');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Identifier: String;
  Position: Integer;
begin
  Result := True;
  if (CurPageID = ConnectionPage.ID) and not KeepExisting() then
  begin
    Identifier := Trim(ConnectionPage.Values[1]);
    Result := (Trim(ConnectionPage.Values[0]) <> '') and
      (Trim(ConnectionPage.Values[2]) <> '') and (Length(Identifier) = 36);
    if Result then
      for Position := 1 to 36 do
        if (Position = 9) or (Position = 14) or (Position = 19) or (Position = 24) then
        begin
          if Identifier[Position] <> '-' then Result := False;
        end
        else if Pos(Lowercase(Identifier[Position]), '0123456789abcdef') = 0 then
          Result := False;
    if not Result then MsgBox('Enter the API URL, a valid installation ID and the agent token.', mbError, MB_OK);
  end;
end;

function ConfigArguments(): String;
begin
  Result := '';
  if TemporaryConfig <> '' then Result := ' -ConfigurationFile "' + TemporaryConfig + '"';
end;

function RunInstallScript(ScriptPath: String; ExtraArguments: String): Boolean;
var
  ExitCode: Integer;
begin
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "' + ScriptPath + '"' +
    ConfigArguments() + ExtraArguments, '', SW_HIDE, ewWaitUntilTerminated, ExitCode);
  Result := Result and (ExitCode = 0);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Json: String;
  Lines: TArrayOfString;
begin
  Result := '';
  TemporaryConfig := ExternalConfig;
  if (TemporaryConfig = '') and not KeepExisting() then
  begin
    TemporaryConfig := ExpandConstant('{tmp}\sinergiaprint-configuration.json');
    Json := '{"Agent":{"ApiBaseUrl":"' + JsonEscape(Trim(ConnectionPage.Values[0])) +
      '","InstallationId":"' + JsonEscape(Trim(ConnectionPage.Values[1])) +
      '","Token":"' + JsonEscape(Trim(ConnectionPage.Values[2])) + '","NetworkPrinters":[]}}';
    SetArrayLength(Lines, 1);
    Lines[0] := Json;
    if not SaveStringsToUTF8File(TemporaryConfig, Lines, False) then
    begin
      Result := 'Could not prepare the configuration.';
      Exit;
    end;
  end;
  ExtractTemporaryFile('prepare-install.ps1');
  if not RunInstallScript(ExpandConstant('{tmp}\prepare-install.ps1'), ' -ValidateOnly') then
  begin
    Result := 'Invalid configuration. Use HTTPS (HTTP only on loopback), a valid GUID and a token. No service was changed.';
    Exit;
  end;
  if not RunInstallScript(ExpandConstant('{tmp}\prepare-install.ps1'), ' -PrepareOnly') then
    Result := 'Could not stop the previous agent version. Stop the agent before continuing.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    if not RunInstallScript(ExpandConstant('{app}\install.ps1'), '') then
      RaiseException('Could not configure or start the PrintAgent Windows service. Installation did not complete.');
end;
