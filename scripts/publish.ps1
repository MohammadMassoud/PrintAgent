param([switch]$BuildSetup)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($BuildSetup -and (Test-Path -LiteralPath (Join-Path $root 'artifacts\agent\appsettings.Local.json'))) {
    throw 'The public installer must not contain appsettings.Local.json. Move private configuration outside the publish directory.'
}
$output = Join-Path $root 'artifacts\agent'
if (Test-Path -LiteralPath $output) {
    $artifactsRoot = [IO.Path]::GetFullPath((Join-Path $root 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    $resolvedOutput = [IO.Path]::GetFullPath($output)
    $archivedOutput = Join-Path $artifactsRoot ('agent-previous-' + [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff'))
    if (-not $resolvedOutput.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase) -or -not $archivedOutput.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid publishing directory.' }
    Move-Item -LiteralPath $resolvedOutput -Destination $archivedOutput
}
& dotnet publish (Join-Path $root 'src\SinergiaAI.PrintAgent.Agent') -c Release -r win-x64 --self-contained true -o $output
if ($LASTEXITCODE -ne 0) { throw 'Publishing failed.' }
& dotnet publish (Join-Path $root 'src\SinergiaAI.PrintAgent.Approval') -c Release -r win-x64 --self-contained true -o (Join-Path $output 'approval')
if ($LASTEXITCODE -ne 0) { throw 'Publishing the Windows authorization utility failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'install.ps1') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'uninstall.ps1') -Destination $output -Force
Set-Content -LiteralPath (Join-Path $output 'install.bat') -Encoding ASCII -Value '@echo off', 'cd /d "%~dp0"', 'powershell -NoProfile -ExecutionPolicy Bypass -File ".\install.ps1"', 'pause'
Compress-Archive -Path (Join-Path $output '*') -DestinationPath (Join-Path $root 'artifacts\SinergiaPrint-Agent-win-x64.zip') -Force
if ($BuildSetup) {
    $compiler = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $compilerPath = if ($compiler) { $compiler.Source } else { Join-Path $root 'artifacts\tools\InnoSetup6\ISCC.exe' }
    if (-not (Test-Path -LiteralPath $compilerPath)) { throw 'ZIP created. To build Setup.exe, install Inno Setup 6 and add ISCC.exe to PATH.' }
    [xml]$releaseVersionFile = Get-Content -LiteralPath (Join-Path $root 'build\Version.props') -Raw
    $releaseVersion = $releaseVersionFile.Project.PropertyGroup.Version
    & $compilerPath ('/DAppVersion=' + $releaseVersion) (Join-Path $root 'installer\SinergiaAI.PrintAgent.iss')
    if ($LASTEXITCODE -ne 0) { throw 'Could not compile Setup.exe.' }
    # The small context installer accepts only the exact public installer built here.
    $installerHash = (Get-FileHash -LiteralPath (Join-Path $root 'artifacts\SinergiaPrint-Setup.exe') -Algorithm SHA256).Hash
    $releasePath = Join-Path $root 'src\SinergiaAI.PrintAgent.Bootstrap\InstallerRelease.cs'
    $releaseSource = Get-Content -LiteralPath $releasePath -Raw
    $releaseSource = $releaseSource -replace 'Sha256 = "[A-Fa-f0-9]{64}"', ('Sha256 = "' + $installerHash + '"')
    $versionedUrl = 'https://www.sinergiaai.com/downloads/sinergiaprint/' + $installerHash.ToLowerInvariant() + '/SinergiaPrint-Setup.exe'
    $releaseSource = $releaseSource -replace 'internal const string Url = "[^"]+"', ('internal const string Url = "' + $versionedUrl + '"')
    Set-Content -LiteralPath $releasePath -Value ($releaseSource.TrimEnd()) -Encoding UTF8
    & dotnet build (Join-Path $root 'src\SinergiaAI.PrintAgent.Server') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Could not embed the installer bootstrap in the server library.' }
}
