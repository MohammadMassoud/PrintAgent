param([switch]$BuildSetup)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($BuildSetup -and (Test-Path -LiteralPath (Join-Path $root 'artifacts\agent\appsettings.Local.json'))) {
    throw 'O instalador publico nao pode conter appsettings.Local.json. Mova a configuracao personalizada para fora da pasta de publicacao.'
}
$output = Join-Path $root 'artifacts\agent'
& dotnet publish (Join-Path $root 'src\SinergiaPrint.Agent') -c Release -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { throw 'Falha no publish.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $output -Force
Set-Content -LiteralPath (Join-Path $output 'instalar.bat') -Encoding ASCII -Value '@echo off', 'cd /d "%~dp0"', 'powershell -NoProfile -ExecutionPolicy Bypass -File ".\install.ps1"', 'pause'
Compress-Archive -Path (Join-Path $output '*') -DestinationPath (Join-Path $root 'artifacts\SinergiaPrint-Agent-win-x64.zip') -Force
if ($BuildSetup) {
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $compilerPath = if ($compiler) { $compiler.Source } else { Join-Path $root 'artifacts\tools\InnoSetup6\ISCC.exe' }
    if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'ZIP gerado. Para gerar Setup.exe, instale Inno Setup 6 e adicione ISCC.exe ao PATH.' }
    & $compilerPath (Join-Path $root 'installer\SinergiaPrint.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao compilar Setup.exe.' }
    # The small context installer accepts only the exact public installer built here.
    $installerHash = (Get-FileHash -LiteralPath (Join-Path $root 'artifacts\SinergiaPrint-Setup.exe') -Algorithm SHA256).Hash
    $releasePath = Join-Path $root 'src\SinergiaPrint.Bootstrap\InstallerRelease.cs'
    $releaseSource = Get-Content -LiteralPath $releasePath -Raw
    $releaseSource = $releaseSource -replace 'Sha256 = "[A-Fa-f0-9]{64}"', ('Sha256 = "' + $installerHash + '"')
    $versionedUrl = 'https://www.sinergiaai.com/downloads/sinergiaprint/' + $installerHash.ToLowerInvariant() + '/SinergiaPrint-Setup.exe'
    $releaseSource = $releaseSource -replace 'internal const string Url = "[^"]+"', ('internal const string Url = "' + $versionedUrl + '"')
    Set-Content -LiteralPath $releasePath -Value ($releaseSource.TrimEnd()) -Encoding UTF8
    & dotnet build (Join-Path $root 'src\SinergiaPrint.Server') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Falha ao incorporar inicializador no modulo do servidor.' }
}
