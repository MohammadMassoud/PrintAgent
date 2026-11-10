$ErrorActionPreference = 'Stop'
$desktopExe = [IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'SinergiaPrint\approval\SinergiaAI.PrintAgent.Approval.exe'))
Get-CimInstance Win32_Process -Filter "Name='SinergiaAI.PrintAgent.Approval.exe'" | Where-Object {
    $_.ExecutablePath -and [IO.Path]::GetFullPath($_.ExecutablePath) -ieq $desktopExe
} | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop }
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
$runPath = 'HKLM:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Get-ItemProperty -LiteralPath $runPath -Name 'SinergiaAI PrintAgent' -ErrorAction SilentlyContinue) {
    Remove-ItemProperty -LiteralPath $runPath -Name 'SinergiaAI PrintAgent'
}
$shortcutPath = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'SinergiaAI PrintAgent.lnk'
if (Test-Path -LiteralPath $shortcutPath) { Remove-Item -LiteralPath $shortcutPath }
Write-Host 'Service removed. Configuration and history were preserved in ProgramData\SinergiaPrint.'
