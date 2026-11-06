[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$packageOutput = Join-Path $projectRoot 'artifacts\nuget-unified'

# This builds only the NuGet integration package, not the Windows installer.
& dotnet pack (Join-Path $projectRoot 'src\SinergiaAI.PrintAgent.Server\SinergiaAI.PrintAgent.Server.csproj') `
    --configuration Release --output $packageOutput --nologo
if ($LASTEXITCODE -ne 0) { throw 'NuGet packaging failed.' }

[xml]$packageProperties = Get-Content (Join-Path $projectRoot 'build\NuGetPackage.props')
$packageVersion = $packageProperties.Project.PropertyGroup.Version
$packageFile = Join-Path $packageOutput "SinergiaAI.PrintAgent.$packageVersion.nupkg"
if (-not (Test-Path -LiteralPath $packageFile)) { throw "Package not found: $packageFile" }
Write-Host "Ready for manual upload: $packageFile"
