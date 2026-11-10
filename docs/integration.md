# Integration and operation

PrintAgent 1.0.0 uses WebSocket protocol 2 between the browser and Windows workstation. The default host integration has no printing API, backend key, server database or claim polling.

## Blazor Server

Install NuGet 1.0.0. Call `builder.Services.AddPrintAgent()` and `app.MapPrintAgent()` with normal host middleware and authentication. Inject `PrintAgent` from `using SinergiaAI.PrintAgent;` in an Interactive Server component. Call `StartAsync` after the first interactive render.

When `Status == PrintAgentStatus.OriginDenied`, show a button whose click calls `AuthorizeAsync()`. The browser opens the installed Windows permission utility. Ask the user to verify the exact website origin, approve it and refresh the connection. Permission is shared by Windows users on the machine. Sites on the same origin share access and job history.

Use `IsConnected`, `Printers` or `PrintersInfo`. Persist each request's printer, exact bytes and unique key before `SendAsync` or `SendBatchAsync`. Never replace a key to bypass a submission conflict. Query known jobs with `GetJobsAsync`.

Subscribe to `OnJobChanged` for pushed status updates. Dispatch UI changes with `InvokeAsync` and unsubscribe on disposal. Notifications are best effort; query persisted results after reconnecting. `OnStatusChanged` reports a closed socket. `OnGetPrinters` reports refreshed lists; call `RefreshPrintersAsync` on demand. DI owns facade disposal. New documents are never automatically resubmitted.

The [README](../README.md) includes setup and error-display examples. The [demo](../samples/PrintAgent.Demo) demonstrates request persistence and event subscriptions.

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

Install Windows agent 1.0.0. Its self-contained service starts with Windows. A separate desktop utility handles explicit website authorization over a local Windows named pipe. The `sinergia-printagent` protocol is registered by the installer. Browser local-network permission and agent website approval are separate.

Printer visibility follows the service account, normally LocalSystem. Install printers visible to that account or configure `Agent:NetworkPrinters` TCP destinations in the protected ProgramData configuration. `Agent:AllowedOrigins` is an optional workstation-administrator allowlist; configured origins are reapplied at startup. Normal users authorize sites through the Windows dialog.

The service binds only to IPv4 loopback, verifies Host and Origin, and checks approval for every operation. Local native processes are part of the Windows trust boundary. Approve trusted sites only.

The FIFO queue is `%ProgramData%\SinergiaPrint\local-queue.db` unless `Agent:DataPath` is overridden. Accepted jobs are durable before acknowledgement. Pending work resumes on restart; interrupted delivery becomes Unknown and never automatically repeats. Preserve the database, WAL, configuration and legacy journals during upgrades. Completed payloads are released; keys, hashes and results are retained without automatic expiry.

Limits: 5,000 documents per batch, 2 MiB each, 4 MiB total RAW data per batch, 256 MiB pending content and 16 concurrent sockets. The browser must be connected to submit new jobs. Already accepted work continues after disconnect.

`UnlinkAsync` closes the current component connection. `RevokeAuthorizationAsync` opens a Windows dialog to remove website-wide access. Neither cancels accepted jobs. Uninstallation preserves local data.

## Migration and errors

Follow [migration](migration-1.0.md) before changing an old installation. Display the curated `LastError` fields; [troubleshooting](troubleshooting.md) covers local connectivity, authorization, upgrade and storage. No extra diagnostics endpoint is needed.
