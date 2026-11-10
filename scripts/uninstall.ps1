$ErrorActionPreference = 'Stop'
$service = Get-Service -Name 'SinergiaPrint Agent' -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $service.Name
        $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped,[TimeSpan]::FromSeconds(30))
    }
    & sc.exe delete 'SinergiaPrint Agent'
    if ($LASTEXITCODE -ne 0) { throw 'Could not remove the Windows service.' }
}
if (Test-Path -LiteralPath 'HKLM:\Software\Classes\sinergia-printagent') {
    Remove-Item -LiteralPath 'HKLM:\Software\Classes\sinergia-printagent' -Recurse -Force
}
Write-Host 'Service removed. Configuration and history were preserved in ProgramData\SinergiaPrint.'
