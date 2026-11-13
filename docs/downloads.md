# PrintAgent downloads and verification

## Stable components

| Component | Version | Distribution |
| --- | --- | --- |
| Blazor / .NET integration SDK | 2.1.0 | [NuGet.org](https://www.nuget.org/packages/SinergiaAI.PrintAgent/2.1.0) |
| Windows x64 service and desktop agent | 2.0.0 | [Setup.exe](https://github.com/MohammadMassoud/PrintAgent/releases/download/v2.1.0/SinergiaPrint-Setup.exe) |
| Release notes and checksums | SDK 2.1.0 / agent 2.0.0 | [GitHub Release](https://github.com/MohammadMassoud/PrintAgent/releases/tag/v2.1.0) |

SDK 2.1.0 is compatible with agent 2.0.0 on protocol 2. The installer asset is the existing published agent 2.0.0, not a new 2.1.0 Windows build. The `.nupkg` asset is the repository-signed package downloaded from NuGet.org.

For most .NET projects, install the SDK through NuGet:

```shell
dotnet add package SinergiaAI.PrintAgent --version 2.1.0
```

Run the Windows installer on the workstation connected to the printer. Installation needs administrator permission and adds a Windows service, desktop startup entry, Start menu shortcut and local website-authorization protocol. Only approve trusted website origins. Accepted print jobs and local history remain on the workstation; uninstalling preserves local configuration and history.

## Check the installer hash

The current agent 2.0.0 installer SHA-256 is:

```text
61b7e783ae57384bbe22e167cac33dc33f87b5720980ee2867dc78146d7a47cb
```

In PowerShell, calculate the hash of the file you downloaded:

```powershell
Get-FileHash -LiteralPath .\SinergiaPrint-Setup.exe -Algorithm SHA256
```

Compare the result with this value and the release's `SHA256SUMS.txt`. The release checksum file also identifies the official NuGet package. A hash comparison verifies that bytes match the referenced release; it does not establish a publisher identity or replace an Authenticode signature.

The installer is currently unsigned. Windows may display an unknown-publisher or SmartScreen warning. The project does not claim code-signing approval from SignPath Foundation or any other signing provider. Do not disable Windows protection to install a file whose source or integrity you cannot establish.

## Upgrade without duplicate jobs

Read [the migration guide](migration-2.1.md) before changing submission behavior. SDK-only updates do not require replacing the compatible agent installer. Preserve journals, saved request keys and pending-job records. A lost response or `Unknown` result must not trigger automatic submission of another copy.
