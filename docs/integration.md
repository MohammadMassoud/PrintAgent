# Integration and operation

PrintAgent 2.0.0 uses WebSocket protocol 2 between the browser and Windows workstation. The default host integration has no printing API, backend key, server database or claim polling.

## Blazor Server

Install NuGet 2.0.0. Call `builder.Services.AddPrintAgent()` and `app.MapPrintAgent()` with normal host middleware and authentication. Inject `PrintAgent` from `using SinergiaAI.PrintAgent;` in an Interactive Server component. Call `StartAsync` after the first interactive render.

StartAsync requests approval through the running Windows tray application. The user verifies the exact origin in its native window and allows or denies it. The waiting connection completes automatically after approval; no custom page button or custom-protocol browser launch is needed. If the tray app is closed, open SinergiaAI PrintAgent from Start and refresh. Declined requests have a cooldown of up to two minutes. Permission is shared by Windows users on the machine. Sites on the same origin share access and job history.

Use `IsConnected`, `Printers` or `PrintersInfo`. Persist each request's printer, exact bytes and unique key before `SendAsync`, `SendByteAsync` or `SendBatchAsync`. Never replace a key to bypass a submission conflict. Query known jobs with `GetJobsAsync`.

Subscribe to `OnJobChanged` for pushed status updates. Dispatch UI changes with `InvokeAsync` and unsubscribe on disposal. Notifications are best effort; query persisted results after reconnecting. `OnStatusChanged` reports a closed socket. `OnGetPrinters` reports refreshed lists; call `RefreshPrintersAsync` on demand. DI owns facade disposal. New documents are never automatically resubmitted.

The [README](../README.md) includes setup and error-display examples. The [demo](../samples/PrintAgent.Demo) demonstrates request persistence and event subscriptions.

## Text and binary printing

`SendAsync(printerId, content, idempotencyKey, ct)` accepts text and uses `PrintAgentOptions.TextEncoding` (strict UTF-8 by default). An overload accepts an explicit `Encoding` before `ct`. `SendByteAsync(printerId, content, idempotencyKey, ct)` sends an exact byte array. Text conversion rejects unrepresentable characters, adds no BOM and does not interpret literal hexadecimal escape strings. The device must accept the supplied language and encoding.

Use `CreateTextDocument(printerId, content, idempotencyKey, encoding)` to prepare bytes without contacting the agent. Save its content, printer ID and key before calling `SendByteAsync` or combining it with binary `PrintDocument` instances in `SendBatchAsync`. Limits apply after encoding. `PrintDocument.FromText` is also available with an explicit encoding for callers that do not use the facade's configured default.

This bridge supports compatible ordinary-paper printers as well as label and receipt printers. PCL, PostScript, ZPL and ESC/POS are examples of device languages, not languages validated or converted by the agent. PDF, HTML, Office and image rendering are outside the transport.

See [migration to 2.0](migration-2.0.md) for the renamed binary method and stable-key rules.

## Optional settings

```csharp
builder.Services.AddPrintAgent(options =>
{
    options.LocalBridgePort = 17831;
    options.ApiPrefix = "/printing"; // Embedded module path, not a printing API.
});
```

Defaults are sufficient for most hosts. `DataDirectory`, `PublicBaseUrl` and `LegacyProtectionPurpose` are unused by the direct facade and retained for source compatibility. The current .NET integration supports Blazor Server / Interactive Server; a standalone WebAssembly integration package is not included.

## Workstation

Install Windows agent 2.0.0. Its self-contained service starts with Windows. A separate desktop utility handles explicit website authorization over a local Windows named pipe. The `sinergia-printagent` protocol is registered by the installer. Browser local-network permission and agent website approval are separate.

The LocalSystem service discovers machine-installed printers and printer connections belonging to active Windows sessions, including shared queues such as `\\server\Labels`. User-only queues are opened under an active user's Windows identity for RAW delivery; credentials are never sent to the browser or stored by the agent. These connections require the user to remain signed in with an active session until delivery starts. Custom-account or foreground agents discover queues visible to their own account. Configured `Agent:NetworkPrinters` TCP destinations remain available independently of user sessions. The approved website's printer list is machine-wide, like website authorization; it is not isolated by Windows user. `Agent:AllowedOrigins` is an optional workstation-administrator allowlist; configured origins are reapplied at startup. Normal users authorize sites through the Windows dialog.

The service binds only to IPv4 loopback, verifies Host and Origin, and checks approval for every operation. Local native processes are part of the Windows trust boundary. Approve trusted sites only.

The FIFO queue is `%ProgramData%\SinergiaPrint\local-queue.db` unless `Agent:DataPath` is overridden. Accepted jobs are durable before acknowledgement. Pending work resumes on restart; interrupted delivery becomes Unknown and never automatically repeats. Preserve the database, WAL, configuration and legacy journals during upgrades. Completed payloads are released; keys, hashes and results are retained without automatic expiry.

Limits: 5,000 documents per batch, 2 MiB each, 4 MiB total RAW data per batch, 256 MiB pending content and 16 concurrent sockets. The browser must be connected to submit new jobs. Already accepted work continues after disconnect.

`UnlinkAsync` closes the current component connection. `RevokeAuthorizationAsync` opens a Windows dialog to remove website-wide access. Neither cancels accepted jobs. Uninstallation preserves local data.

## Migration and errors

Follow [migration](migration-1.0.md) before changing an old installation. Display the curated `LastError` fields; [troubleshooting](troubleshooting.md) covers local connectivity, authorization, upgrade and storage. No extra diagnostics endpoint is needed.


## Desktop application (2.0.0)

The installer creates a **SinergiaAI PrintAgent** Start menu shortcut and starts its tray application at Windows sign-in. Double-click its icon to open status and service controls. Opening the Start shortcut brings an existing instance forward and attempts to start a stopped service. Windows administrator consent is required for service start/stop.

Closing the window hides it to the tray. **Exit tray app** removes only the icon. **Stop service and exit** stops printing and closes the tray app, preserving pending work. Interrupted delivery remains Unknown and is not automatically resent. Website permissions can be removed in **Authorized websites**.

Desktop menus and permission windows follow the current Windows user's display language: English for English Windows; Portuguese otherwise. Public SDK identifiers, comments and error contracts remain English. The tray's local named-pipe checks do not poll any application API. StartAsync requests a native decision but never authorizes a website itself.
