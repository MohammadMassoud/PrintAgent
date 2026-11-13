# PrintAgent by SinergiaAI — RAW printing bridge for Blazor and .NET

[![NuGet version](https://img.shields.io/nuget/v/SinergiaAI.PrintAgent.svg)](https://www.nuget.org/packages/SinergiaAI.PrintAgent)
[![NuGet downloads](https://img.shields.io/nuget/dt/SinergiaAI.PrintAgent.svg)](https://www.nuget.org/packages/SinergiaAI.PrintAgent)
[![Latest release](https://img.shields.io/github/v/release/MohammadMassoud/PrintAgent.svg)](https://github.com/MohammadMassoud/PrintAgent/releases/latest)
[![Windows checks](https://github.com/MohammadMassoud/PrintAgent/actions/workflows/ci.yml/badge.svg?branch=main)](https://github.com/MohammadMassoud/PrintAgent/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

![SinergiaAI](https://raw.githubusercontent.com/MohammadMassoud/PrintAgent/main/branding/sinergia.png)

Connect Blazor Server and .NET 10 applications to local Windows queues, shared network printers and TCP destinations. PrintAgent is RAW printing middleware for compatible label, receipt and ordinary-paper printers. PrintAgent connects the browser directly to a Windows service over WebSocket, with a durable local print queue, delivery status and optional application-supplied request keys. Developed by SinergiaAI. Free and open source under the MIT license.

[Product website](https://www.sinergiaai.com/printagent) · [Integration](https://github.com/MohammadMassoud/PrintAgent/blob/main/docs/integration.md) · [Troubleshooting](https://github.com/MohammadMassoud/PrintAgent/blob/main/docs/troubleshooting.md)

[Leia em português: impressão de etiquetas no Blazor](README.pt-BR.md).

## Downloads and stable versions

| Component | Stable version | Get it |
| --- | --- | --- |
| Blazor / .NET SDK | **2.1.0** | [NuGet.org](https://www.nuget.org/packages/SinergiaAI.PrintAgent/2.1.0) |
| Windows x64 agent | **2.0.0** | [Download Setup.exe](https://github.com/MohammadMassoud/PrintAgent/releases/download/v2.1.0/SinergiaPrint-Setup.exe) |
| Release notes and SHA-256 checksums | SDK 2.1.0 + agent 2.0.0 | [GitHub Release](https://github.com/MohammadMassoud/PrintAgent/releases/tag/v2.1.0) |

SDK 2.1.0 is a compatible SDK-only update; the Windows agent stays at 2.0.0. Install the agent on the Windows workstation connected to the printer and the NuGet package in the Blazor Server application. Downloads contain the existing published binaries. The installer is currently unsigned, so Windows may display a SmartScreen warning; see the [download verification guide](docs/downloads.md).

[Documentation index](docs/README.md) · [What changed](CHANGELOG.md) · [2.1 migration](docs/migration-2.1.md) · [Report an issue](https://github.com/MohammadMassoud/PrintAgent/issues/new/choose) · [Contributing](CONTRIBUTING.md)

## Connect web applications to compatible printers

- **Ordinary-paper printers:** send application-generated commands in a language the device accepts, such as PCL or PostScript on supported models. Paper type does not determine language compatibility.
- **ZPL label printing:** send application-generated label and barcode commands to a Zebra or other compatible ZPL printer. [Blazor ZPL tutorial](https://www.sinergiaai.com/printagent/blazor-zpl-label-printing.html).
- **ESC/POS receipt printing:** send exact receipt command bytes to a compatible thermal printer. [Blazor ESC/POS tutorial](https://www.sinergiaai.com/printagent/blazor-escpos-receipt-printing.html).
- **Local printers from a hosted web app:** the agent runs on the Windows workstation connected to the printer, so the application server does not need direct access to its USB printers.
- **Safe integration testing:** run the supplied demo with a simulated TCP printer, without installing a permanent service or using paper.

The current integration requires Blazor Server / Interactive Server and .NET 10. It does not provide a standalone Blazor WebAssembly SDK. Your application generates printer-ready bytes; PrintAgent does not convert HTML, PDF, Office documents or images into printer commands. Printer language support depends on the device.

**Português:** o PrintAgent é um agente gratuito para imprimir etiquetas ZPL e recibos ESC/POS no Blazor Server com C#. Use impressoras Windows ou TCP, com fila local persistente e acompanhamento do envio. [Guia: como imprimir etiquetas no Blazor](https://www.sinergiaai.com/printagent/imprimir-etiquetas-blazor.html).

## Architecture

```text
Blazor component → packaged JavaScript → local WebSocket → durable local queue → printer
```

The default integration has no printing API, server database, API key, remote enrollment or claim polling. The browser module is embedded in the NuGet package. Applications provide exact ZPL, ESC/POS or other RAW bytes; the agent does not validate printer language or render PDF, Office documents or images.

## Integration

Requires .NET 10, Blazor Server / Interactive Server and Windows x64. Package **2.1.0** uses protocol 2 and works with Windows agent **2.0.0**. Both components must be upgraded from 0.x.

```shell
dotnet add package SinergiaAI.PrintAgent --version 2.1.0
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

// Simple sends: each call creates a new request when no key is supplied.
var textJob = await PrintAgent.SendAsync(printerId, commands);
var byteJob = await PrintAgent.SendAsync(printerId, rawBytes);
var listJobs = await PrintAgent.SendAsync(printerId, commandList);
var results = await PrintAgent.GetJobsAsync(new[] { textJob.Id, byteJob.Id });
```

`SendAsync` accepts a string, a byte array, a list of strings, a list of byte arrays or prepared `PrintDocument` instances. C# chooses the overload from the argument type. Text uses the configured encoding (strict UTF-8 by default); bytes are forwarded unchanged. No BOM, line endings or printer-language conversion is added.

Request keys are optional. Without an explicit key, each single or plain-list call creates new jobs, including repeated identical content. For recoverable submission, prepare documents and persist their destination, bytes and generated or explicit keys before sending:

```csharp
var documents = new[]
{
    PrintAgent.CreateTextDocument(printerId, commands), // Uses configured encoding.
    new PrintDocument(printerId, rawBytes, savedBinaryKey),
    new PrintDocument(otherPrinterId, otherCommands)   // Strict UTF-8 text.
};
// Persist documents before submitting if recovery is required.
var jobs = await PrintAgent.SendAsync(documents);

// Explicit stable key for an individual text request:
await PrintAgent.SendAsync(printerId, commands, idempotencyKey: savedTextKey);
```

A prepared document generates its key once and keeps it when reused. A key is not a password or a cancellation token. Never automatically repeat a keyless call after an uncertain response; it could create another copy. Cancelling a wait cannot retract accepted work.

Configure `AddPrintAgent(o => o.TextEncoding = Encoding.ASCII)` (import `System.Text`) or use an explicit encoding: `SendAsync(printerId, commands, null, Encoding.UTF8)`. Unsupported characters are rejected before submission. Register `CodePagesEncodingProvider.Instance` before selecting legacy code pages. The printer must support the supplied commands and encoding.

Lists accept 1–5,000 documents, at most 2 MiB each and 4 MiB total after encoding. Acceptance is atomic. The durable pending queue is limited to 256 MiB; completed payloads are released while identities and results are retained.

SDK **2.1.0** works with Windows agent **2.0.0**, protocol 2. `SendByteAsync` and `SendBatchAsync` remain compatible aliases, hidden from IntelliSense. See the [2.1 guide](https://github.com/MohammadMassoud/PrintAgent/blob/main/docs/migration-2.1.md). English XML API documentation is bundled for IntelliSense and F12 metadata navigation.

## Workstation setup

1. Install [PrintAgent for Windows](https://www.sinergiaai.com/downloads/SinergiaPrint-Setup.exe). The self-contained service starts with Windows.
2. Keep the SinergiaAI PrintAgent tray application running. Calling `StartAsync()` requests native website authorization automatically; no page-specific authorization button is required.
3. Verify the exact website origin in the native window and approve it. The waiting `StartAsync()` connects automatically. If the request timed out, refresh the connection.
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


## Desktop application

The installer creates a **SinergiaAI PrintAgent** Start menu shortcut and starts its tray application at Windows sign-in. Double-click its icon to open status and service controls. Opening the Start shortcut brings an existing instance forward and attempts to start a stopped service. Windows administrator consent is required for service start/stop.

Closing the window hides it to the tray. **Exit tray app** removes only the icon. **Stop service and exit** stops printing and closes the tray app, preserving pending work. Interrupted delivery remains Unknown and is not automatically resent. Website permissions can be removed in **Authorized websites**.

Desktop menus and permission windows follow the current Windows user's display language: English for English Windows; Portuguese otherwise. Public SDK identifiers, comments and error contracts remain English. The tray's local named-pipe checks do not poll any application API. StartAsync requests a native decision but never authorizes a website itself.
