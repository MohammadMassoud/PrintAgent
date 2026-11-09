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
Write-Host 'Service removed. Configuration and history were preserved in ProgramData\SinergiaPrint.'
