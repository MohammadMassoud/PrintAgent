# PrintAgent

![SinergiaAI](https://raw.githubusercontent.com/MohammadMassoud/PrintAgent/main/branding/sinergia.png)

[Product website](https://www.sinergiaai.com/printagent) · [GitHub](https://github.com/MohammadMassoud/PrintAgent)

RAW printing for .NET 10 and Blazor Server. PrintAgent connects an authenticated application to Windows printers through a persistent queue and a background Windows service. Applications provide the exact ZPL, ESC/POS or other RAW bytes to send.

Developed by SinergiaAI. MIT licensed. [Source and documentation](https://github.com/MohammadMassoud/PrintAgent).

## Installation

```shell
dotnet add package SinergiaAI.PrintAgent --version 0.6.1
```

The package contains the Server, Client and Contracts assemblies, embedded browser discovery and a contextual installer bootstrap. Dependencies are restored by NuGet.

## Configure the host

In an authenticated Blazor Server application with interactive server components and authorization configured:

```csharp
using SinergiaAI.PrintAgent;

builder.Services.AddPrintAgent(options =>
{
    options.DataDirectory = "App_Data/PrintAgent";
    options.PublicBaseUrl = "https://your-app.example/printing/";
});

// After building the app and configuring authentication and authorization:
app.MapPrintAgent();
```

Persist SQLite and Data Protection keys between deployments. Grant the host write access to its data directory and use HTTPS in production. Install in the backend, not WebAssembly. Non-Web SDK consumers need a `Microsoft.AspNetCore.App` framework reference.

## Use in a component

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

// Persist key, destination and bytes before submitting; reuse them on retry.
var job = await PrintAgent.SendAsync(printerId, rawBytes, persistedRequestKey);
var results = await PrintAgent.GetJobsAsync(new[] { job.Id });
```

Additional operations include `SendBatchAsync`, `RefreshPrintersAsync`, `DownloadInstallerAsync` and `UnlinkAsync`. Connection properties include `Status`, `MachineName`, `LastErrorMessage`, `InstalledVersion`, `AvailableVersion` and `UpdateAvailable`.

The facade does not start a polling timer. The consuming page controls connection refresh and pending job queries. Subscribe to `OnStatusChanged` and `OnGetPrinters` using UI dispatch, unsubscribe on disposal, and cancel page operations. DI owns service disposal.

## Windows agent

Install the service once per Windows computer. Multiple integrated applications, application users and browser profiles can use it. The browser discovers the service through `127.0.0.1:17831`; new applications enroll automatically using a single-use code from their authenticated backend. Each application retains separate credentials, queue and journal without replacing existing connections. The permanent service token does not enter the browser. Local-network permission may be required per origin and browser profile.

Automatic multi-application enrollment requires package **0.6.1** and Windows agent **0.5.1**. Agents 0.3/0.4 need one upgrade. Generic Windows setup no longer asks for an API URL, GUID or token. Remote enrollment targets the application's own HTTPS origin; HTTP loopback is supported for local development.

The contextual download contains a single-use enrollment code. Running it retrieves configuration, verifies the pinned installer and requests administrative approval. Downloading this NuGet package does not install a Windows service. Windows x64 agent binaries include their runtime; the bootstrap requires .NET Framework 4.8. Executables are currently unsigned.

Package **0.6.1** and agent **0.5.1** enable asynchronous long polling with a 20-second idle wait, reducing idle claim requests from approximately 30 to 3 per minute. Enqueue wakes a waiting claim without holding SQLite connections during the wait. Communication failures use exponential backoff with jitter. Older agents remain compatible but need upgrading to benefit. Notification hints are local to one API process.

## Delivery semantics

The agent passes RAW bytes through without validating command language or rendering documents. Printer visibility depends on the Windows service account. `Sent` confirms delivery of bytes to the configured destination, not physical paper output. Preserve idempotency keys across retries and inspect the printer after an uncertain failure before requesting another copy. The durable journal prevents automatic resubmission of a job that may already have printed.

## Demo and documentation

Clone and run the standalone demo with a temporary agent and simulated printer:

```shell
git clone https://github.com/MohammadMassoud/PrintAgent.git
cd PrintAgent
dotnet run --project samples/PrintAgent.Demo -- --simulate
```

Open **http://localhost:5199**. Windows x64 and the .NET 10 SDK are required. Simulation does not install a service or print physically.

- [Demo guide](https://github.com/MohammadMassoud/PrintAgent/tree/main/samples/PrintAgent.Demo)
- [Integration and operation](https://github.com/MohammadMassoud/PrintAgent/blob/main/docs/integration.md)
- [Release guide](https://github.com/MohammadMassoud/PrintAgent/blob/main/docs/nuget-publication.md)
