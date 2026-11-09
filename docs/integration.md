# Integration and operation

## Components

| Component | Responsibility |
| --- | --- |
| Application backend | Authentication, authorization, enrollment, machine discovery and persistent SQLite queue |
| `PrintAgent` facade | Page-facing API, browser discovery, printer lists, submission and status queries |
| Embedded JavaScript | Browser storage, loopback discovery and contextual installer download |
| Windows service | Outbound authenticated job claims, durable journal, RAW delivery and acknowledgements |
| Contextual installer | Redeems a short-lived code and installs the verified Windows agent |

The download website distributes installers only. The printing API runs in the application that owns the queue. The Windows agent contacts that API; the server does not open an inbound connection to a customer's computer.

## Register the integration

Install `SinergiaAI.PrintAgent` version `0.5.0` in the backend and in any component library that uses its public types. Non-Web SDK projects require `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. This integration targets .NET 10 and Blazor Server, including interactive server components in Blazor Web Apps.

```csharp
using SinergiaAI.PrintAgent;

builder.Services.AddPrintAgent(options =>
{
    options.ApiPrefix = "/printing";
    options.DataDirectory = "App_Data/PrintAgent";
    options.PublicBaseUrl = "https://your-app.example/printing/";
});

// Keep the host's authentication, authorization and component setup.
app.MapPrintAgent();
```

The public URL must include the API prefix and any application path base. Use HTTPS in production. HTTP is allowed only for loopback testing. Configure forwarded headers in a reverse proxy host as required by that host's deployment.

The host must supply authentication, authorization and an `AuthenticationStateProvider`. Authenticated users need a `ClaimTypes.NameIdentifier`. Protect printing pages with your application's authorization policy. The machine bridge proves local installation access; it does not replace business permissions or tenant isolation. The built-in facade uses the host's authenticated identity; applications with stricter tenant or printer access rules must enforce those rules before invoking it, or implement a dedicated backend using the advanced APIs.

## Component lifecycle

```razor
@using SinergiaAI.PrintAgent
@inject PrintAgent PrintAgent
```

Call `await PrintAgent.StartAsync(cancellationToken)` after the first interactive render, when browser interop is available. Read `Status`, `IsConnected`, `MachineConfirmed`, `MachineName` and `LastErrorMessage` for connection UI. Use `PrintersInfo` to display names while retaining stable printer IDs; `Printers` exposes just the names.

Subscribe to `OnStatusChanged` or `OnGetPrinters` with handlers that dispatch UI changes through `InvokeAsync(StateHasChanged)`. Unsubscribe and cancel page operations on disposal. DI owns the facade lifetime. The facade starts no automatic timer. Refresh discovery when appropriate, call `RefreshPrintersAsync` to read the latest backend report, and query pending job IDs until final results arrive. The standalone demo shows this lifecycle.

`InstalledVersion`, `AvailableVersion` and `UpdateAvailable` are compared by the integration after discovery. JavaScript transports local service information; it does not independently determine or install updates. A page can offer `DownloadInstallerAsync` when an upgrade is available.

## Submit RAW jobs

```csharp
var document = new PrintDocument(printerId, rawBytes, persistedRequestKey);
var job = await PrintAgent.SendAsync(document.PrinterId, document.Content, document.IdempotencyKey);
var results = await PrintAgent.GetJobsAsync(new[] { job.Id });

var jobs = await PrintAgent.SendBatchAsync(documents);
```

Persist a request's idempotency key, destination and bytes before sending it. Reuse all three on retry. A new key means a new request, potentially producing another physical copy. Batches submit in input order and may partially succeed; retry with the original keys instead of generating replacements.

| Status | Meaning |
| --- | --- |
| `Pending` | Waiting in the backend queue |
| `Claimed` | Reserved for agent processing |
| `Sent` | Bytes delivered to the configured destination |
| `Failed` | Processing reported a failure |
| `Unknown` | Outcome cannot be determined safely |

Neither `Sent` nor a successful TCP write proves paper output. After a partial or uncertain error, inspect the printer before requesting another copy. The agent journal is durable and prevents automatic physical resubmission of a job that may already have reached the printer.

RAW content is not converted or validated. Choose the correct printer and command language. The agent does not render PDFs or offer scanning.

## Machine discovery

The Windows service listens on loopback at `127.0.0.1:17831` by default. It is not exposed on the LAN. The port is configurable, but `PrintAgentOptions.LocalBridgePort` and the service's `Agent:LocalBridgePort` must agree.

The browser obtains an authenticated backend challenge, contacts the bridge, then redeems the result at the backend. The backend validates that the service belongs to its API and issues an opaque machine proof. The permanent service credential never enters browser storage.

Browser JavaScript is necessary in Blazor Server because server-side C# executes on the application server. Its `127.0.0.1` refers to that server, while browser JavaScript can reach the workstation's loopback service. The module is embedded in the package and served by `MapPrintAgent`; consumers do not copy it.

Recognition is associated with the installation, not the signed-in application user. A new browser profile can recognize the same installation through the challenge flow. Browser local-network permission may still be required per profile. A blocked permission or a stopped service is a different condition from a missing installation; use `Status` and the message to guide the user.

## Install and update Windows

`DownloadInstallerAsync` creates a contextual EXE with a single-use enrollment code valid for 30 minutes. It does not expose a permanent token in a download query string. When run, the bootstrap redeems configuration, downloads the exact installer pinned by the package, verifies SHA-256, and requests administrator approval. The installer writes the machine configuration and starts the Windows service.

The service runs in the background independently of the application login and browser. It resumes when Windows starts. Its files and journal use the existing `ProgramData\SinergiaPrint` location. Printer visibility depends on the service account; printers installed only for a user's profile may not be available to LocalSystem. Install required printers for the service context or configure an appropriate service account.

Upgrade the NuGet package to **0.5.0** and the service to **0.4.0** for long polling. Older agents remain compatible and continue their previous claim interval. Service upgrades preserve installation data and the journal. Keep backend API URLs and data stable.

`UnlinkAsync` invalidates the current browser proof; it does not uninstall the Windows service or disable machine discovery. A later discovery can recognize the installed service again. To remove it, use Windows **Settings > Apps > Installed apps**, locate **SinergiaPrint Agent** and uninstall it. The administrative `scripts/uninstall.ps1` removes the service and retains machine data; it is a service-removal helper rather than the full application uninstaller.

## Long polling

An authenticated claim accepts `waitSeconds` from 0 to 20. Omission retains immediate legacy behavior. The API advertises support with `X-PrintAgent-Long-Poll: 20`. It subscribes before checking the queue, avoiding a lost wake-up between checking and waiting. Enqueue and completion signals are emitted only after commits. Claims remain atomically reserved by SQLite.

Waiting uses an asynchronous task without holding an open database connection or transaction. Cancellation releases the subscription. At timeout the API returns 204; the agent immediately starts the next wait. Unsupported older APIs use the configured legacy delay. Communication failures use bounded exponential backoff with jitter.

Idle claims fall from roughly 30 to 3 requests per minute with the default 20-second wait. Printer reports and other requests remain separate. Configure proxy timeouts above the wait interval; the agent's HTTP timeout is 45 seconds.

Notifications are process-local hints, while SQLite remains authoritative. Another API process sees new work on its next claim, within the wait interval. A distributed notification layer is required for immediate wake-up across instances. Do not assume a local SQLite file is a complete multi-server deployment strategy.

## Deployment and persistence

- Grant the host write access to the data directory and retain `printing.db` across deployments.
- Persist and protect ASP.NET Core Data Protection keys so existing protected proofs and enrollment data remain usable.
- Retain `ApiPrefix`, `PublicBaseUrl`, `BrowserStoragePrefix` and legacy protection settings when upgrading existing installations.
- Preserve the agent's local journal; deleting it can remove evidence needed to avoid duplicate physical output.
- Keep old immutable installer URLs available for previously released packages.

The advanced `SinergiaAI.PrintAgent.Server`, `.Client` and `.Contracts` APIs remain available. Standard page integration uses the root namespace. A few legacy serialized identifiers are retained for compatibility.
