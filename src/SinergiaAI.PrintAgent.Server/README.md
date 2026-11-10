# PrintAgent by SinergiaAI — ZPL label and ESC/POS printing for Blazor

![SinergiaAI](https://raw.githubusercontent.com/MohammadMassoud/PrintAgent/main/branding/sinergia.png)

Print ZPL labels and ESC/POS receipts from Blazor Server and .NET 10 to local Windows and network TCP printers. PrintAgent connects the browser directly to a Windows service over WebSocket, with a durable local print queue, delivery status and stable request keys. Developed by SinergiaAI. Free and open source under the MIT license.

[Product website](https://www.sinergiaai.com/printagent) · [Integration](https://github.com/MohammadMassoud/PrintAgent/blob/main/docs/integration.md) · [Troubleshooting](https://github.com/MohammadMassoud/PrintAgent/blob/main/docs/troubleshooting.md)

## Print labels and thermal receipts from Blazor

Use PrintAgent for ZPL labels and barcodes on Zebra or other compatible printers, ESC/POS receipts on compatible thermal printers, and application-generated RAW commands. The agent runs on the Windows workstation connected to the printer, allowing hosted Blazor applications to reach local printers through the browser.

[ZPL label printing tutorial](https://www.sinergiaai.com/printagent/blazor-zpl-label-printing.html) · [ESC/POS receipt printing tutorial](https://www.sinergiaai.com/printagent/blazor-escpos-receipt-printing.html) · [Documentação em português](https://www.sinergiaai.com/printagent/pt-br.html).

**Português:** impressão de etiquetas ZPL e recibos ESC/POS no Blazor Server com C#, impressoras Windows ou TCP e fila local persistente. [Como imprimir etiquetas no Blazor](https://www.sinergiaai.com/printagent/imprimir-etiquetas-blazor.html).

Requires Blazor Server / Interactive Server and .NET 10. There is no standalone WebAssembly SDK. PrintAgent transports printer-ready bytes; it does not convert HTML, PDF, Office documents or images into printer commands. Check the target printer's supported language.

## Architecture

```text
Blazor component → packaged JavaScript → local WebSocket → durable local queue → printer
```

The default integration has no printing API, server database, API key, remote enrollment or claim polling. The browser module is embedded in the NuGet package. Applications provide exact ZPL, ESC/POS or other RAW bytes; the agent does not validate printer language or render PDF, Office documents or images.

## Integration

Requires .NET 10, Blazor Server / Interactive Server and Windows x64. Package **1.0.0** uses protocol 2 and requires Windows agent **1.0.0**. Both components must be upgraded from 0.x.

```shell
dotnet add package SinergiaAI.PrintAgent --version 1.0.0
```

```csharp
using SinergiaAI.PrintAgent;

builder.Services.AddPrintAgent();

// After building the app and configuring normal middleware:
app.MapPrintAgent();
```

`MapPrintAgent` only serves the browser module. Keep the application's normal authentication and authorization. In an interactive component:

```razor
@using SinergiaAI.PrintAgent
@inject PrintAgent PrintAgent
```

```csharp
// After the first interactive render:
await PrintAgent.StartAsync();
var connected = PrintAgent.IsConnected;
var names = PrintAgent.Printers;
var printers = PrintAgent.PrintersInfo; // Id, Name, IsDefault

// Persist the key, destination and bytes before submitting.
var job = await PrintAgent.SendAsync(printerId, rawBytes, persistedRequestKey);
var results = await PrintAgent.GetJobsAsync(new[] { job.Id });
```

`SendBatchAsync` accepts 1–5,000 documents, at most 2 MiB each and 4 MiB of RAW data per batch. Accepted batches are committed atomically. The pending local queue is limited to 256 MiB. Completed jobs retain keys, hashes and results for deduplication while releasing payload storage.

## Workstation setup

1. Install [PrintAgent for Windows](https://www.sinergiaai.com/downloads/SinergiaPrint-Setup.exe). The self-contained service starts with Windows.
2. For a new website, show an **Authorize this website** button calling `await PrintAgent.AuthorizeAsync()` from its click handler.
3. Verify the exact website origin in the Windows permission dialog and approve it. Return to the application and refresh the connection.
4. Select a compatible printer and send a test.

Installation and website approval are shared across Windows users and browser profiles. Browser local-network permission may still be requested separately. Approval applies to the entire origin (scheme, hostname and port), not an application login or a page. Applications sharing an origin share its printing access and job history. Approve trusted websites only.

The service binds to `127.0.0.1:17831`, validates loopback Host and browser Origin, and checks approval on every operation. A separate desktop utility asks for permission and communicates over a local Windows named pipe; websites cannot grant themselves access through WebSocket. Local native processes are part of the workstation trust boundary.

`RevokeAuthorizationAsync()` opens the Windows dialog for removing website access. `UnlinkAsync()` disconnects the current component only. Neither cancels accepted jobs or uninstalls the service. Revocation blocks subsequent operations, including on existing sockets.

## Connection and job updates

Subscribe to `OnStatusChanged`, `OnGetPrinters` and `OnJobChanged`. Dispatch UI changes through `InvokeAsync` and unsubscribe on disposal. Delivery updates are pushed through WebSocket. Query `GetJobsAsync` after reconnection to reconcile missed notifications; use `RefreshPrintersAsync` on demand. DI owns facade disposal.

The browser must be connected to submit new jobs. Accepted jobs continue processing after it closes. Reconnection is explicit; the SDK never automatically resubmits documents.

## Delivery guarantees

- Each stable key belongs to one origin, printer and byte sequence. An identical retry returns the original job; changed content or destination is rejected.
- Delivery intent is durable before bytes leave the process. Interrupted delivery becomes `Unknown` after restart and is never automatically replayed.
- `Sent` confirms delivery to the Windows spooler or TCP destination, not physical paper output.
- Inspect the printer before creating another copy after `Failed`, `Unknown` or a lost submission response. Retain the original key, printer and bytes when retrying the same request.

## Actionable errors

Convenience properties include `LastErrorMessage`, `LastErrorCode`, `LastErrorHelp`, `LastErrorSteps` and `LastErrorReferenceId`. The structured result is `LastError`. Private SDK exceptions remain in the host log; delivery failures are recorded in the Windows service log with the job ID.

```razor
@if (PrintAgent.LastError is { } error)
{
    <p role="alert">@error.Message</p>
    <details>
        <summary>How to resolve · @error.Code</summary>
        <ol>
            @foreach (var step in error.ResolutionSteps)
            {
                <li>@step</li>
            }
        </ol>
        <small>Host log reference: @error.ReferenceId</small>
    </details>
}
```

A printing-folder permission on the application server is not required. `LOCAL_STORAGE_UNAVAILABLE` concerns the workstation's queue. See [troubleshooting](https://github.com/MohammadMassoud/PrintAgent/blob/main/docs/troubleshooting.md).

## Demo

```shell
git clone https://github.com/MohammadMassoud/PrintAgent.git
cd PrintAgent
dotnet run --project samples/PrintAgent.Demo -- --simulate
```

Open `http://localhost:5199`. Simulation starts an isolated temporary agent and a loopback TCP receiver; physical printers are excluded and no service is installed. Only the sample origin is preapproved in the temporary configuration. Omit `--simulate` to use an installed agent and the normal Windows authorization dialog. [Demo guide](https://github.com/MohammadMassoud/PrintAgent/tree/main/samples/PrintAgent.Demo).

## Upgrade from 0.x

Upgrade both the package and Windows service. Remove printing-specific server data-folder and API URL settings from the integration configuration. Preserve the old data files for reconciliation. Existing option properties remain for source compatibility but are unused by the direct facade. The package retains legacy assemblies and their dependencies for explicit maintenance; default DI does not initialize them.

Old queues, credentials and journals are preserved, not imported or automatically resent. Reconcile old work before switching. Use a new browser-storage namespace for direct-mode request records so pages cannot restore old remote batches into the new local queue. Legacy polling requires explicit `Agent:EnableLegacyApi=true` and is disabled by default.

[Migration checklist](https://github.com/MohammadMassoud/PrintAgent/blob/main/docs/migration-1.0.md) · [Release guide](https://github.com/MohammadMassoud/PrintAgent/blob/main/docs/nuget-publication.md)
