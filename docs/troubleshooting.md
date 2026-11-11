# Troubleshooting local printing

The default 1.1.0 integration connects the browser directly to Windows agent 1.1.0 over WebSocket. It does not initialize a server printing directory, database or enrollment API.

## AGENT_UNREACHABLE

1. On the workstation, open Windows Services and verify **SinergiaPrint Agent** is running. Install it if absent.
2. Check the browser's local-network permission. A failed connection may mean a blocked permission or port; it does not prove the service is missing.
3. Verify the service and package use the same port, normally 17831. The service binds only to 127.0.0.1.
4. Refresh the connection. An endpoint on another computer cannot discover this workstation.

## LOCAL_ORIGIN_DENIED

1. Open **SinergiaAI PrintAgent** from the Windows Start menu if its tray icon is absent.
2. Refresh the application connection. StartAsync requests its native permission window automatically.
3. Verify the full origin before approving. Permission applies to all Windows users on the machine.
4. A waiting connection completes automatically. If denied or timed out, refresh when ready; denials have a cooldown of up to two minutes.

Browser local-network access and PrintAgent approval are separate permissions. To remove a site's access, use `RevokeAuthorizationAsync()` and confirm removal in Windows. Existing sockets lose permission on the next operation. Accepted jobs are not cancelled.

## AGENT_UPDATE_REQUIRED / AGENT_PROTOCOL_INCOMPATIBLE

1. Compare the installed service with the package requirement.
2. Upgrade the Windows agent to 1.1.0; protocol 1 cannot accept WebSocket jobs.
3. Preserve all queue and journal files. Reconcile old work using the [migration checklist](migration-1.0.md).

## LOCAL_STORAGE_UNAVAILABLE

This refers to the **Windows workstation**, not the application server.

1. In Services → SinergiaPrint Agent → Properties → Log On, identify the service account. The standard installer uses LocalSystem.
2. Check disk space and read/write access to `%ProgramData%\SinergiaPrint` or the configured `Agent:DataPath`, including existing database and WAL files.
3. Preserve `local-queue.db`, WAL/SHM files, configuration and legacy journals. Do not delete or recreate the queue to bypass an error.
4. Inspect Windows Event Viewer → Windows Logs → Application for service failures. Delivery errors include a job ID. SDK error references identify the corresponding entry in the application's host log.
5. Ask the workstation administrator to repair access or restore the appropriate backup, then restart the service during maintenance and reconnect.

## Installed network printer missing

The standard LocalSystem agent includes shared printer connections from active Windows user sessions, as well as machine-installed queues. A connection such as `\\server\Labels` is not the same as a machine-wide TCP/IP installation. Keep its Windows session active until delivery starts and refresh the application's printer list after installing or reconnecting a queue. Agents running under a custom account only discover printers visible to that account. Discovery does not scan the network or infer IP addresses from port names.

For a read-only local diagnostic, run `dotnet run --project tests/PrinterDiscovery/PrinterDiscovery.csproj` from the repository. It reports the executing Windows identity and installed queues, and verifies delivery-context selection without opening a printer for writing or sending bytes. Running it under the signed-in user verifies that user's queues; it does not validate LocalSystem session-token acquisition. Building the project does not replace an already installed Windows service.

## REQUEST_INVALID

Check that the printer is available, content is non-empty and batch limits are respected. A key identifies exactly one origin, printer and byte sequence. An identical retry returns the old job. Do not generate a fresh key merely to bypass a conflict; that creates another copy.

## OPERATION_TIMED_OUT / lost response

A timeout does not establish that submission failed. Reconnect and query known jobs, or retry the exact request with its saved key. Inspect the printer before requesting another copy if delivery is uncertain. No document is automatically resubmitted.

## Failed or Unknown

Failed can indicate an unavailable destination before transmission. Unknown means delivery may have started, including a service interruption. Neither automatically repeats. Sent confirms transport to the spooler or TCP destination, not physical paper output.

## Old DATA_DIRECTORY_ACCESS_DENIED errors

This code belongs to the former server queue. A direct 1.1.0 integration should not initialize that backend. Verify that all consumer projects use 1.1.0 and deploy the complete output. Keep the old database and keys for reconciliation.

For a deliberately retained legacy backend, grant its hosting identity Modify on its configured printing data directory only. On IIS, identify the Application Pool and use `IIS AppPool\<ApplicationPoolName>` when it uses ApplicationPoolIdentity. Preserve that directory and its permission across deployments. This is not required for direct printing.

## Unknown errors

Display the stable error code and reference, and ask the application administrator to inspect the correlated host log. Do not expose raw exceptions, stack traces or credentials. Unknown errors are not a reason to reinstall or resend automatically.


## Desktop application (1.1.0)

The installer creates a **SinergiaAI PrintAgent** Start menu shortcut and starts its tray application at Windows sign-in. Double-click its icon to open status and service controls. Opening the Start shortcut brings an existing instance forward and attempts to start a stopped service. Windows administrator consent is required for service start/stop.

Closing the window hides it to the tray. **Exit tray app** removes only the icon. **Stop service and exit** stops printing and closes the tray app, preserving pending work. Interrupted delivery remains Unknown and is not automatically resent. Website permissions can be removed in **Authorized websites**.

Desktop menus and permission windows follow the current Windows user's display language: English for English Windows; Portuguese otherwise. Public SDK identifiers, comments and error contracts remain English. The tray's local named-pipe checks do not poll any application API. StartAsync requests a native decision but never authorizes a website itself.


## Windows desktop correction (1.1.0)

The refreshed Windows installer retains version 1.1.0, matching NuGet 1.1.0. It fixes local control response disposal, restores authorized-website queries and displays the actual Windows service state. Start and Stop controls are disabled for inappropriate or changing states. The version appears beside the product name.

Download and run the current setup again to apply this desktop correction. No application deployment or NuGet update is required. Existing local queue and configuration are preserved. Do not republish the existing NuGet 1.1.0 package.

The SDK, Windows service and desktop application share the release version in `build/Version.props`. The publishing script also passes this version to the installer compiler.
