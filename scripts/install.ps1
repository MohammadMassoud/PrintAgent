param(
    [string]$ApiBaseUrl = '',
    [string]$InstallationId = '',
    [string]$AgentToken = '',
    [string]$ConfigurationFile = '',
    [switch]$ValidateOnly,
    [switch]$PrepareOnly
)
$ErrorActionPreference = 'Stop'
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $ValidateOnly -and -not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Execute instalar.bat como administrador.' }
$serviceName = 'SinergiaPrint Agent'
$installPath = Join-Path $env:ProgramFiles 'SinergiaPrint'
$dataPath = Join-Path $env:ProgramData 'SinergiaPrint'
$configPath = Join-Path $dataPath 'appsettings.Local.json'
$sourceConfig = Join-Path $PSScriptRoot 'appsettings.Local.json'
if (-not ($ValidateOnly -or $PrepareOnly) -and -not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'SinergiaPrint.Agent.exe'))) { throw 'Executavel nao encontrado. Use o pacote publicado.' }
if ($ConfigurationFile) {
    $config = Get-Content -LiteralPath $ConfigurationFile -Raw | ConvertFrom-Json
}
elseif ($InstallationId -or $AgentToken -or $ApiBaseUrl) {
    if (-not $InstallationId -or -not $AgentToken -or -not $ApiBaseUrl) { throw 'Informe URL, InstallationId e AgentToken juntos.' }
    $config = @{Agent=@{ApiBaseUrl=$ApiBaseUrl;InstallationId=$InstallationId;Token=$AgentToken;NetworkPrinters=@()}}
}
elseif (Test-Path -LiteralPath $sourceConfig) { $config = Get-Content -LiteralPath $sourceConfig -Raw | ConvertFrom-Json }
elseif (Test-Path -LiteralPath $configPath) { $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json }
else {
    $ApiBaseUrl = Read-Host 'URL da API de impressao (https://...)'
    $InstallationId = Read-Host 'Identificacao da instalacao'
    $secureToken = Read-Host 'Token do agente' -AsSecureString
    $AgentToken = [Net.NetworkCredential]::new('', $secureToken).Password
    $config = @{Agent=@{ApiBaseUrl=$ApiBaseUrl;InstallationId=$InstallationId;Token=$AgentToken;NetworkPrinters=@()}}
}
$agentConfig = $config.Agent
$uri = [Uri]$agentConfig.ApiBaseUrl
if (-not $uri.IsAbsoluteUri -or ($uri.Scheme -ne 'https' -and -not ($uri.Scheme -eq 'http' -and $uri.IsLoopback))) { throw 'Use HTTPS; HTTP somente para localhost.' }
if ([Guid]$agentConfig.InstallationId -eq [Guid]::Empty -or [string]::IsNullOrWhiteSpace([string]$agentConfig.Token)) { throw 'Credenciais invalidas.' }
if ($ValidateOnly) { return }
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($service -and $service.Status -ne 'Stopped') {
    Stop-Service -Name $serviceName -ErrorAction Stop
    $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped,[TimeSpan]::FromSeconds(30))
}
if ($PrepareOnly) { return }
New-Item -ItemType Directory -Path $installPath -Force | Out-Null
New-Item -ItemType Directory -Path $dataPath -Force | Out-Null
# Credentials and journal are readable only by Administrators and LocalSystem.
& icacls.exe $dataPath /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Falha ao proteger configuracao.' }
if ([IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') -ine [IO.Path]::GetFullPath($installPath).TrimEnd('\')) {
    Get-ChildItem -LiteralPath $PSScriptRoot | Where-Object { $_.Name -ne 'appsettings.Local.json' } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $installPath -Recurse -Force }
}
$config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath -Encoding UTF8
function Invoke-Sc([string[]]$ScArguments) {
    & sc.exe @ScArguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao configurar servico Windows.' }
}
$exePath = Join-Path $installPath 'SinergiaPrint.Agent.exe'
if ($service) { Invoke-Sc -ScArguments @('config',$serviceName,'binPath=',"`"$exePath`"",'start=','delayed-auto') }
else { Invoke-Sc -ScArguments @('create',$serviceName,'binPath=',"`"$exePath`"",'start=','delayed-auto') }
Invoke-Sc -ScArguments @('failure',$serviceName,'reset=','86400','actions=','restart/5000/restart/15000/restart/30000')
Invoke-Sc -ScArguments @('failureflag',$serviceName,'1')
Start-Service -Name $serviceName
(Get-Service -Name $serviceName).WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running,[TimeSpan]::FromSeconds(30))
Write-Host 'SinergiaPrint instalado. Confira a comunicacao com a API antes de imprimir.'
