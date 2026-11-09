# PrintAgent

[![SinergiaAI](branding/sinergia.png)](https://www.sinergiaai.com/printagent)

[Product website](https://www.sinergiaai.com/printagent) · [Documentation](docs/integration.md) · [NuGet](https://www.nuget.org/packages/SinergiaAI.PrintAgent)

PrintAgent connects .NET and Blazor Server applications to printers on a Windows computer. Applications submit RAW commands, such as ZPL or ESC/POS, to a persistent queue. A Windows agent receives jobs and sends their bytes through the Windows spooler or a configured TCP connection.

Developed by SinergiaAI. Licensed under [MIT](LICENSE).

## Try the demo

The repository includes a standalone Blazor application with a temporary agent and simulated TCP printer. Explore discovery, printer selection, job submission and results without installing a Windows service or using a physical printer.

Requirements: Windows x64, the .NET 10 SDK and Git.

```shell
git clone https://github.com/MohammadMassoud/PrintAgent.git
cd PrintAgent
dotnet run --project samples/PrintAgent.Demo -- --simulate
```

Open **http://localhost:5199**, wait for **Agent connected**, select **Simulated TCP printer**, and submit a sample. The page displays job status and captured RAW bytes. Ctrl+C stops the application and its temporary agent. Data is stored in `samples/PrintAgent.Demo/.demo-data/` and excluded from Git.

To test an installed agent and real printer:

```shell
dotnet run --project samples/PrintAgent.Demo
```

Open the same address and use **Download installer** if the service is unavailable. Run the downloaded installer, return to the page and refresh the connection. Choose a printer that supports the selected command language. Printing starts only when you submit a request.

The demo accepts only local clients and uses a demonstration login. Its authentication setup is not a production template. See the [demo guide](samples/PrintAgent.Demo/README.md) for configuration and troubleshooting.

## Add to an application

```shell
dotnet add package SinergiaAI.PrintAgent --version 0.6.1
```

Register the integration in an authenticated Blazor Server host that already configures interactive server components and authorization:

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

In a component:

```razor
@using SinergiaAI.PrintAgent
@inject PrintAgent PrintAgent
```

Call `StartAsync` after the first interactive render:

```csharp
await PrintAgent.StartAsync();

var connected = PrintAgent.IsConnected;
var names = PrintAgent.Printers;
var printers = PrintAgent.PrintersInfo; // Id, Name, IsDefault

// Persist the key, destination and bytes before submitting a request.
var job = await PrintAgent.SendAsync(printerId, rawBytes, persistedRequestKey);
var results = await PrintAgent.GetJobsAsync(new[] { job.Id });
```

The package includes the Server, Client and Contracts assemblies, browser discovery module and contextual installer bootstrap. Standard integration uses the `SinergiaAI.PrintAgent` namespace; pages do not need copied services, interfaces or JavaScript files.

Additional operations include `SendBatchAsync`, `RefreshPrintersAsync`, `DownloadInstallerAsync` and `UnlinkAsync`. The `OnStatusChanged` and `OnGetPrinters` events support UI updates. The facade does not create a polling timer: the application controls connection refresh and pending job queries. See the [integration guide](docs/integration.md) for lifecycle and deployment details.

## How it works

1. The browser contacts the local service at `127.0.0.1:17831` to obtain a short-lived machine challenge.
2. The authenticated backend validates the challenge and recognizes the installation. The service token stays outside the browser.
3. The application queues jobs in SQLite. The Windows agent maintains an authenticated outbound request to the backend.
4. A new job wakes the waiting agent. The agent records the operation in its local journal before sending RAW bytes, then reports the result.

Install the service once per computer. Multiple applications, application users and browser profiles can use that installation. Each integrated application enrolls automatically through its authenticated backend and retains its own credentials, queue and durable journal. Adding an application does not replace other connections. Browser permission to access the local service may be required per origin and profile.

Multi-application support requires package **0.6.1** in each application and Windows agent **0.5.1**. Older agents need one upgrade. A generic installation no longer asks for an API URL, GUID or token.

Installation downloads a contextual EXE containing a single-use enrollment code. The bootstrap redeems configuration, downloads the pinned installer, verifies SHA-256 and requests elevation. Windows x64 agent binaries include their runtime; the bootstrap requires .NET Framework 4.8. Executables are currently unsigned.

## Queue behavior

Library **0.6.1** and Windows agent **0.5.1** support long polling. An empty claim waits asynchronously for up to 20 seconds, without holding a SQLite connection or transaction. Enqueue and acknowledgement commits notify waiting claims. The agent renews an idle wait immediately and uses exponential backoff with jitter after communication failures.

This reduces idle claim requests from approximately 30 to 3 per minute per agent. Older agents remain compatible but retain their previous polling interval; upgrade both backend and service to enable long polling. Notifications are local to an API process. With several processes sharing storage, another process sees work on its next claim, within the wait interval; immediate cross-process notification requires shared signaling.

`Sent` means bytes reached the configured destination, not that paper output was verified. Preserve idempotency keys across retries. After partial or uncertain failure, inspect the printer before intentionally creating another copy. The durable journal prevents automatic physical resubmission of a job that may already have printed.

The agent does not validate printer command languages or render PDF, Office documents or images. Printers installed only for a Windows user may be unavailable to the service account.

## Build and verification

```shell
dotnet build SinergiaAI.PrintAgent.slnx
python tests/integration.py
python tests/long_polling.py
python tests/installer_validation.py
```

Checks use temporary agents and simulated destinations. They do not install a permanent service or print physically. See the [release guide](docs/nuget-publication.md) for package generation and Visual Studio instructions.

## Documentation

- [Demo application](samples/PrintAgent.Demo/README.md)
- [Integration and operation](docs/integration.md)
- [Release guide](docs/nuget-publication.md)
- [License](LICENSE)
