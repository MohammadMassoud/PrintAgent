; Generic public installer: never includes installation-specific credentials.
[Setup]
AppId={{1035B4C9-6996-4AC4-9F03-52D3B53DE48F}
AppName=SinergiaPrint Agent
AppVersion=0.2.0
AppPublisher=Sinergia
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
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

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
    RaiseException('A configuracao automatica nao foi encontrada. Baixe novamente o instalador pelo aplicativo.');
  ExistingConfig := FileExists(ExpandConstant('{commonappdata}\SinergiaPrint\appsettings.Local.json'));
  ExistingPage := CreateInputOptionPage(wpWelcome, 'Conexao do agente',
    'Foi encontrada uma configuracao existente.',
    'Mantenha a conexao atual ou desmarque para conectar a outro projeto.', False, False);
  ExistingPage.Add('Manter a configuracao atual');
  ExistingPage.Values[0] := True;
  ConnectionPage := CreateInputQueryPage(ExistingPage.ID, 'Conectar ao seu aplicativo',
    'Informe os dados fornecidos pelo aplicativo que usara as impressoras.',
    'O instalador e generico. A URL e as credenciais sao fornecidas pelo seu projeto, nao pelo site de download.');
  ConnectionPage.Add('URL da API de impressao (https://...):', False);
  ConnectionPage.Add('Identificacao da instalacao (GUID):', False);
  ConnectionPage.Add('Token do agente:', True);
  WizardForm.FinishedLabel.Caption :=
    'O SinergiaPrint foi instalado como servico do Windows. Volte ao aplicativo e atualize a lista de impressoras.' + #13#10 + #13#10 +
    'O agente iniciara automaticamente com o Windows. A conexao depende da API do seu projeto estar disponivel.';
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
    if not Result then MsgBox('Preencha URL, identificacao valida e token do agente.', mbError, MB_OK);
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
      Result := 'Nao foi possivel preparar a configuracao.';
      Exit;
    end;
  end;
  ExtractTemporaryFile('prepare-install.ps1');
  if not RunInstallScript(ExpandConstant('{tmp}\prepare-install.ps1'), ' -ValidateOnly') then
  begin
    Result := 'Configuracao invalida. Use URL HTTPS (HTTP somente para localhost), GUID valido e token. Nenhum servico foi alterado.';
    Exit;
  end;
  if not RunInstallScript(ExpandConstant('{tmp}\prepare-install.ps1'), ' -PrepareOnly') then
    Result := 'Nao foi possivel parar a versao anterior do agente. Feche o agente antes de continuar.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    if not RunInstallScript(ExpandConstant('{app}\install.ps1'), '') then
      RaiseException('Nao foi possivel configurar ou iniciar o servico SinergiaPrint. A instalacao nao foi concluida.');
end;
