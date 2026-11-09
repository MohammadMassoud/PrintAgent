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
if (-not $ValidateOnly -and -not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run install.bat as an administrator.' }
$serviceName = 'SinergiaPrint Agent'
$installPath = Join-Path $env:ProgramFiles 'SinergiaPrint'
$dataPath = Join-Path $env:ProgramData 'SinergiaPrint'
$configPath = Join-Path $dataPath 'appsettings.Local.json'
$sourceConfig = Join-Path $PSScriptRoot 'appsettings.Local.json'
if (-not ($ValidateOnly -or $PrepareOnly) -and -not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'SinergiaAI.PrintAgent.Agent.exe'))) { throw 'Executable not found. Use a published agent distribution.' }
if ($ConfigurationFile) {
    $config = Get-Content -LiteralPath $ConfigurationFile -Raw | ConvertFrom-Json
}
elseif ($InstallationId -or $AgentToken -or $ApiBaseUrl) {
    if (-not $InstallationId -or -not $AgentToken -or -not $ApiBaseUrl) { throw 'Provide ApiBaseUrl, InstallationId and AgentToken together.' }
    $config = @{Agent=@{ApiBaseUrl=$ApiBaseUrl;InstallationId=$InstallationId;Token=$AgentToken;NetworkPrinters=@()}}
}
elseif (Test-Path -LiteralPath $sourceConfig) { $config = Get-Content -LiteralPath $sourceConfig -Raw | ConvertFrom-Json }
elseif (Test-Path -LiteralPath $configPath) { $config = Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json }
else {
    $ApiBaseUrl = Read-Host 'Printing API URL (https://...)'
    $InstallationId = Read-Host 'Installation ID'
    $secureToken = Read-Host 'Agent token' -AsSecureString
    $AgentToken = [Net.NetworkCredential]::new('', $secureToken).Password
    $config = @{Agent=@{ApiBaseUrl=$ApiBaseUrl;InstallationId=$InstallationId;Token=$AgentToken;NetworkPrinters=@()}}
}
$agentConfig = $config.Agent
$uri = [Uri]$agentConfig.ApiBaseUrl
if (-not $uri.IsAbsoluteUri -or ($uri.Scheme -ne 'https' -and -not ($uri.Scheme -eq 'http' -and $uri.IsLoopback))) { throw 'Use HTTPS; HTTP is allowed only on loopback.' }
if ([Guid]$agentConfig.InstallationId -eq [Guid]::Empty -or [string]::IsNullOrWhiteSpace([string]$agentConfig.Token)) { throw 'Invalid credentials.' }
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
if ($LASTEXITCODE -ne 0) { throw 'Could not secure the configuration directory.' }
if ([IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\') -ine [IO.Path]::GetFullPath($installPath).TrimEnd('\')) {
    Get-ChildItem -LiteralPath $PSScriptRoot | Where-Object { $_.Name -ne 'appsettings.Local.json' } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $installPath -Recurse -Force }
}
$config | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $configPath -Encoding UTF8
function Invoke-Sc([string[]]$ScArguments) {
    & sc.exe @ScArguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not configure the Windows service.' }
}
$exePath = Join-Path $installPath 'SinergiaAI.PrintAgent.Agent.exe'
if ($service) { Invoke-Sc -ScArguments @('config',$serviceName,'binPath=',"`"$exePath`"",'start=','delayed-auto') }
else { Invoke-Sc -ScArguments @('create',$serviceName,'binPath=',"`"$exePath`"",'start=','delayed-auto') }
Invoke-Sc -ScArguments @('failure',$serviceName,'reset=','86400','actions=','restart/5000/restart/15000/restart/30000')
Invoke-Sc -ScArguments @('failureflag',$serviceName,'1')
Start-Service -Name $serviceName
(Get-Service -Name $serviceName).WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running,[TimeSpan]::FromSeconds(30))
Write-Host 'PrintAgent installed. Verify API connectivity before printing.'
