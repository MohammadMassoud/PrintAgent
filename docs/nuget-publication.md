# Release guide

Version 1.1.0 requires both a new Windows agent and a new NuGet integration. Build on Windows x64 with .NET 10, Python and Inno Setup 6.

## Verify

```shell
dotnet build SinergiaAI.PrintAgent.slnx
python tests/integration.py
python tests/websocket_integration.py
python tests/installer_validation.py
dotnet run --project tests/ErrorHandling
node tests/demo.browser.cjs
node tests/multi_application.browser.cjs
node tests/errors.browser.cjs
```

Browser tests require Playwright and its browser, or `CHROME_PATH` pointing to an installed Chromium executable. They use separate browser processes, isolated data and loopback TCP sinks. No permanent service or physical printer is used. The original integration suite verifies the retained legacy API with explicit worker opt-in.

## Build distributions

```powershell
.\scripts\publish.ps1 -BuildSetup
.\scripts\pack-nuget.ps1
python tests/package_integration.py
```

The Windows distribution includes the self-contained service and desktop approval utility. Its installer registers the `sinergia-printagent` protocol. No API URL, GUID or token is requested. Preserve ProgramData and legacy journals.

The publish script archives previous output, creates the installer, and records an immutable hash URL for the retained legacy bootstrap. The direct facade downloads the public generic installer without application context.

Outputs: `artifacts/SinergiaPrint-Setup.exe`, `artifacts/SinergiaPrint-Agent-win-x64.zip`, and `artifacts/nuget-unified/SinergiaAI.PrintAgent.1.1.0.nupkg`.

Publish the installer before NuGet and consumer updates. Verify the hosted SHA-256. Retain old immutable URLs for older packages. Wait for NuGet validation and indexing before restoring on deployment servers. A local build is not publication.

Visual Studio: select Release and choose **Pack** on SinergiaAI.PrintAgent.Server. This builds only the package. The package retains legacy assemblies and SQLite dependencies for explicit old-API maintenance; `AddPrintAgent` does not initialize them.

Update the landing page, README and consuming test pages together. Follow [migration](migration-1.0.md); never automatically copy or resend old jobs.


## Desktop application (1.1.0)

The installer creates a **SinergiaAI PrintAgent** Start menu shortcut and starts its tray application at Windows sign-in. Double-click its icon to open status and service controls. Opening the Start shortcut brings an existing instance forward and attempts to start a stopped service. Windows administrator consent is required for service start/stop.

Closing the window hides it to the tray. **Exit tray app** removes only the icon. **Stop service and exit** stops printing and closes the tray app, preserving pending work. Interrupted delivery remains Unknown and is not automatically resent. Website permissions can be removed in **Authorized websites**.

Desktop menus and permission windows follow the current Windows user's display language: English for English Windows; Portuguese otherwise. Public SDK identifiers, comments and error contracts remain English. The tray's local named-pipe checks do not poll any application API. StartAsync requests a native decision but never authorizes a website itself.
