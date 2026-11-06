# PrintAgent by SinergiaAI

Connect .NET and Blazor applications to printers accessible from a Windows computer. Your application generates raw commands, the user selects a printer, and the Windows agent sends the bytes through the spooler or TCP.

**One package:** `SinergiaAI.PrintAgent`. Includes `SinergiaAI.PrintAgent.Server.dll`, `SinergiaAI.PrintAgent.Client.dll` and `SinergiaAI.PrintAgent.Contracts.dll`. Third-party dependencies are restored automatically. License: MIT.

Supports raw commands such as ZPL and ESC/POS. The agent does not detect printer language or convert PDF/images. An incompatible printer may accept bytes without producing the intended output or reporting an error.

## Install

```powershell
dotnet add package SinergiaAI.PrintAgent --version 0.3.0-preview.1
```

In Visual Studio, select **nuget.org** and enable **Include prerelease** to find the preview version.

Requires .NET 10 and the ASP.NET Core shared framework. Non-Web .NET projects need `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. Install in the backend / Blazor Server, not in a browser/WebAssembly project. Add the same package to each project that uses its types.

Installing the NuGet package does not install a Windows service. The computer connected to the printers uses the EXE installer described below.

See the repository's [technical integration guide](https://github.com/MohammadMassoud/PrintAgent/blob/main/docs/funcionamento-e-integracao.md) (Portuguese) for installer internals, per-machine discovery, version checks and the Blazor adapter used by the Comercial pilot. The pilot's `IAgenteImpressaoService` and `IAgenteImpressaoConexao` belong to its Common library; they are not public APIs included in this package.

The 0.3 Windows service exposes discovery only on `http://127.0.0.1:17831`. Browser JavaScript calls `/info` and `/connect`; `PrintDiscoveryService` issues a short-lived challenge, validates the service's confirmation and provides a browser-specific proof for the same machine installation. The permanent agent token never reaches the browser. Backend endpoints that issue/redeem challenges must require application authentication. Sending jobs still uses the backend queue, not this local bridge. Browser profiles may require local-network permission.

Use the numeric version returned by `/info` to compare with the Windows installer actually distributed by your backend (currently `0.3.0`), not with the NuGet version `0.3.0-preview.1`. A failed local request cannot distinguish an absent/stopped agent from browser restrictions and does not prove an update is available. Updating the service requires executing the installer; automatic updates are not implemented.

## 1. Register the embedded API

Add to your existing ASP.NET Core application's `Program.cs`, keeping the host's authentication and authorization configuration:

```csharp
using Microsoft.AspNetCore.DataProtection;
using SinergiaAI.PrintAgent.Server;

// Before builder.Build():
var printingData = Path.Combine(
    builder.Environment.ContentRootPath, "App_Data", "PrintAgent");

builder.Services.AddSinergiaAIPrintAgentServer(options =>
{
    options.DatabasePath = Path.Combine(printingData, "printing.db");
});

// Keep the host's existing persistent Data Protection configuration if present.
// Otherwise, configure persistent keys:
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(printingData, "keys")));

// After building app and configuring the host's middleware:
app.MapSinergiaAIPrintAgent("/printing");
```

The agent's API address is now your application's public address plus `/printing/`, for example `https://your-application.example/printing/`. Include any application path base. HTTPS is required in production; HTTP is allowed only for loopback during development.

Preserve the database, Data Protection keys and application identity across deployments. Keep data outside the public web root, use persistent storage and configure key protection at rest for your environment.

The module authenticates the Windows agent. **Your backend must authorize users and check ownership** before creating installations, downloading personalized installers, listing printers, submitting jobs or reading results. The module does not provide user accounts, tenant isolation or a ready-made UI.

## 2. Create an installation and download its installer

Inject `PrintStore` and `PrintEnrollmentService` into your backend service. After authorizing the operation, create an installation for the computer:

```csharp
var credentials = store.CreateInstallation("Shipping workstation");
```

Treat `credentials.InstallationId` as the machine installation identity and enforce user/company access in your application. Do not bind discovery exclusively to the creating user's login. Store `credentials.AgentToken` encrypted on the server; token authentication only retains its hash. `PrintDiscoveryService.RegisterMachine` can protect the recoverable credentials for discovery/enrollment. Reopening the page or going offline must not automatically create another installation.

In your authenticated download endpoint, verify ownership, retrieve the protected credentials and return a contextual installer:

```csharp
// credentials belong to the current authorized user's installation.
var installer = enrollment.CreateInstaller(
    credentials,
    new Uri("https://your-application.example/printing/"));

httpContext.Response.Headers.CacheControl = "no-store";
httpContext.Response.Headers.Pragma = "no-cache";

return Results.File(installer, "application/octet-stream",
    "SinergiaAI.PrintAgent-Install.exe");
```

These snippets require your application's secure credential storage and ownership checks. Do not expose public installation-creation or personalized-download endpoints.

The user clicks **Install agent**, executes the downloaded file, authorizes Windows installation and returns to the page. The EXE contains the public API address and a random, single-use enrollment code valid for 30 minutes. The bootstrap redeems it, downloads the pinned generic installer, checks SHA-256 and passes the configuration to setup. URL, GUID and token do not need to be typed.

Expired/used downloads must be regenerated; a canceled installation can require a fresh download. The bootstrap requires .NET Framework 4.8. The Windows x64 agent includes its .NET runtime. Current installer binaries are unsigned.

The public generic installer alone does not contain application context. Generate the personalized download above for automatic configuration.

## 3. Show connection and available printers

After checking that the current user can access the installation:

```csharp
using SinergiaAI.PrintAgent.Contracts;

var installation = store.GetInstallation(installationId);
var connected = installation?.LastSeenAt is { } lastSeen
    && DateTimeOffset.UtcNow - lastSeen < TimeSpan.FromSeconds(60);
var printers = installation?.Printers ?? Array.Empty<PrinterInfo>();
```

The 60-second window is an example UI criterion. Last contact indicates communication, not direct detection of installed software. No contact may mean the agent is absent, stopped or unable to reach the API.

Bind the selector's label to `PrinterInfo.Name` and value to `PrinterInfo.Id`. The first printer report occurs on agent startup and refreshes approximately every 30 seconds. Your page can poll the authorized backend every few seconds.

Only printers visible to the Windows service are reported. Per-user printer installations may not be visible to LocalSystem; verify installation context or configure TCP printers in the agent options.

## 4. Queue a ZPL label and read the result

After validating authorization, the installation, selected printer ID and your application's payload limits:

```csharp
using System.Text;
using SinergiaAI.PrintAgent.Contracts;

var zpl = "^XA^FO30,30^A0N,30,30^FDPrintAgent test^FS^XZ";
var bytes = Encoding.ASCII.GetBytes(zpl);

// Generate once for this request; preserve it across submission retries.
var requestKey = Guid.NewGuid().ToString("N");
var job = store.Submit(new SubmitPrintRequest(
    installationId,
    selectedPrinterId,
    Convert.ToBase64String(bytes),
    requestKey));

// Keep job.Id and poll this operation through your authorized backend:
var result = store.GetJob(job.Id);
```

The snippets assume injected services and installation/printer values supplied by your application. The package does not include a printer-selection screen.

`Submit` returns when queued. Poll `GetJob` until a final status and show `Error` when present. An immediate read normally still returns `Pending` or `Claimed`. Content and encoding are the application's responsibility; ASCII is only an example.

| Status | Meaning |
|---|---|
| `Pending` | Waiting for the agent. |
| `Claimed` | Reserved for a delivery attempt. |
| `Sent` | Bytes sent to spooler/TCP, not proof of physical printing. |
| `Failed` | Delivery returned an error; partial delivery may have occurred. |
| `Unknown` | Delivery was interrupted or unconfirmed. |

Reuse the same idempotency key when retrying the same submission. Inspect the printer before explicitly requesting another copy after `Failed` or `Unknown`. The durable agent journal prevents automatic resend of jobs that may already have reached the printer.

## Alternative: a separately hosted API

The same package includes the typed HTTP client for the standalone `SinergiaAI.PrintAgent.Api` host:

```csharp
using SinergiaAI.PrintAgent.Client;

builder.Services.AddSinergiaAIPrintAgentClient(
    new Uri(builder.Configuration["Printing:Url"]!),
    builder.Configuration["Printing:ApiKey"]!);
```

Inject `PrintingClient` and call `ListPrintersAsync`, `PrintAsync` and `GetJobAsync`. Preserve the idempotency key passed to `PrintAsync`.

The client uses the standalone host's `/api/...` management endpoints. Mapping only `MapSinergiaAIPrintAgent` does not expose those endpoints; for embedded integration, use `PrintStore` as above. Keep the backend API key on the server, never in browser code or agent configuration. Blazor WebAssembly must call your authenticated backend.

## Migration

Remove old `SinergiaPrint.Server`, `SinergiaPrint.Client` and `SinergiaPrint.Contracts` package or source references before installing the unified package. Change imports to `SinergiaAI.PrintAgent.Server`, `.Client` and `.Contracts`.

Update `AddSinergiaPrintServer` to `AddSinergiaAIPrintAgentServer`, `MapSinergiaPrintAgent` to `MapSinergiaAIPrintAgent`, and `AddSinergiaPrint` to `AddSinergiaAIPrintAgentClient`. Do not mix old and new libraries.

Existing Windows service names, data directories, enrollment format and public installer URLs remain for compatibility. They are operational identifiers, separate from .NET namespaces and assemblies. Preserve the configured API route for installed agents or plan its migration.

Source and complete Portuguese guide: [MohammadMassoud/PrintAgent](https://github.com/MohammadMassoud/PrintAgent).

License: MIT. Copyright (c) 2026 MohammadMassoud.

## Machine discovery (0.3 preview)

The Windows service now exposes a **loopback-only** bridge on `http://127.0.0.1:17831`. Discovery identifies the installed agent, independently of the application login, browser profile or interactive Windows user. It does not identify a workstation by IP or user ID. Upgrade an existing 0.2 agent once to enable this feature; its service name, configuration and durable journal are preserved. Installing a new NuGet package alone does not update the Windows service.

The bridge accepts requests only from the configured application's origin (loopback development origins may use different HTTP/HTTPS ports). It exposes no permanent token, accepts no print jobs, validates the exact configured API address and does not listen on the LAN. Browsers may require local-network permission the first time each profile connects; use `targetAddressSpace: 'loopback'` in local fetch options where supported. Permission cannot be bypassed by the application.

`AddSinergiaAIPrintAgentServer` now registers `PrintDiscoveryService`; `MapSinergiaAIPrintAgent` also maps the agent-authenticated `/machine/confirm` callback. To integrate discovery with your application's authorization:

1. An authenticated backend endpoint calls `CreateChallenge()` and returns the short-lived code with `Cache-Control: no-store`.
2. Browser code sends `{ apiBaseUrl, challenge }` to the local bridge's `/connect`. The service confirms the challenge directly with its configured backend using its permanent token.
3. An authenticated backend endpoint calls `RedeemChallenge(challenge, existingProof)` and returns the machine proof. Redemption is atomic and single-use; the permanent token remains protected on the server.
4. Keep the opaque proof separately from user-specific browser storage and call `GetMachine(proof)` after authenticating a user. A new browser obtains its own proof from the same local service and recognizes the same installation. Never grant access merely because a caller knows an installation GUID.

`GetCredentials(proof)` is a backend-only operation for creating a contextual installer. `ForgetProof(proof)` revokes that browser's recognition, preserving other browser proofs and the print queue. The active Windows service can be discovered again. Proofs expire; create a fresh local challenge to renew them.

The ERP Common adapter implements the authenticated browser endpoints and JavaScript module. Other consumers should implement equivalent authenticated endpoints and user permissions appropriate to their application. The generic module does not assume an ERP, tenant or user ownership model.
