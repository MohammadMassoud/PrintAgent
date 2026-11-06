$ErrorActionPreference = 'Stop'
$service = Get-Service -Name 'SinergiaPrint Agent' -ErrorAction SilentlyContinue
if ($service) {
    if ($service.Status -ne 'Stopped') {
        Stop-Service -Name $service.Name
        $service.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Stopped,[TimeSpan]::FromSeconds(30))
    }
    & sc.exe delete 'SinergiaPrint Agent'
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao remover servico.' }
}
Write-Host 'Servico removido. Configuracoes e historico preservados em ProgramData\SinergiaPrint.'
