# Release guide

PrintAgent is distributed as the `SinergiaAI.PrintAgent` NuGet package and a separate Windows installer. The integration package version is **0.6.1**; the Windows agent version is **0.5.1**. Upgrade both to enable automatic multi-application enrollment. Long polling remains supported.

This patch adds SinergiaAI branding and the official [PrintAgent website](https://www.sinergiaai.com/printagent). Agent 0.5.0 already supports multi-application enrollment and long polling; 0.5.1 adds the branded executables and setup.

## Build and verify

Use Windows x64, the .NET 10 SDK and Python. Inno Setup 6 is needed to build the installer.

```powershell
dotnet build SinergiaAI.PrintAgent.slnx
python tests/integration.py
python tests/long_polling.py
python tests/installer_validation.py
```

Checks use isolated data and simulated destinations without permanent service installation or physical printing.

## Windows installer

```powershell
.\scripts\publish.ps1 -BuildSetup
```

The script publishes a self-contained agent, creates `artifacts/SinergiaPrint-Setup.exe`, pins its SHA-256 and immutable URL in the bootstrap source, and rebuilds the server assembly. Existing publish output is archived. Keep private configuration outside public artifacts.

Publish the exact installer at the pinned URL before releasing NuGet. Verify the hosted SHA-256 and retain older immutable installers for existing package versions. Regenerating the installer requires rebuilding and verifying the package so the embedded hash remains correct.

## NuGet package

```powershell
.\scripts\pack-nuget.ps1
python tests/package_integration.py
```

Output: `artifacts/nuget-unified/SinergiaAI.PrintAgent.0.6.1.nupkg`.

The package contains Server, Client and Contracts assemblies, embedded browser discovery and installer bootstrap, English README and MIT license. NuGet restores third-party dependencies. It excludes demo data, credentials and application-specific libraries.

Verification restores the actual package into an isolated consumer and checks metadata, public facade, resources, queue and single-use enrollment.

### Visual Studio

Select **Release**, right-click `SinergiaAI.PrintAgent.Server`, and choose **Pack**. Default output is `src/SinergiaAI.PrintAgent.Server/bin/Release/`. This does not build or publish a Windows installer.

The package version is in `build/NuGetPackage.props`. Agent and installer versions are maintained separately.

## Publish

1. Sign in to [NuGet upload](https://www.nuget.org/packages/manage/upload) with an owner account.
2. Select the new package and review ID, version, license, dependencies, repository and README.
3. Submit and wait for validation and search indexing.

Published versions are immutable. A local build is not publication. A GitHub README edit does not change a published package. Stable `0.6.1` does not require enabling prerelease packages in Visual Studio.

## Upgrade

```shell
dotnet add package SinergiaAI.PrintAgent --version 0.6.1
```

Preserve API prefix, data directory, Data Protection keys and connection identifiers. Older agents remain compatible but retain their polling interval until updated. See the [integration guide](integration.md).
