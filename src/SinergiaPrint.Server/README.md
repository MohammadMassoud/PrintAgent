# SinergiaPrint.Server

Embed a raw-printing API in an ASP.NET Core application. Targets .NET 10; initial preview.

Includes a persistent SQLite queue, token-authenticated Windows agent endpoints, and the Windows installer bootstrap as an embedded resource. No local SinergiaPrint source folder or installer compiler is required in the consuming project.

## Register the module

```csharp
using SinergiaPrint.Server;

builder.Services.AddSinergiaPrintServer(options =>
{
    options.DatabasePath = Path.Combine(
        builder.Environment.ContentRootPath,
        "App_Data", "Printing", "printing.db");
});

// After building the application:
app.MapSinergiaPrintAgent("/printing");
```

Use a persistent directory outside the public web root. Configure and preserve ASP.NET Core Data Protection keys and the application identity across deployments.

## Integrate with your application's authorization

The module handles agent authentication. Your backend must authorize users/tenants before creating installations, listing printers, submitting jobs, or providing an installer download. It does not impose an ERP-specific ownership model or add a UI.

From an authorized backend operation, use `PrintStore.CreateInstallation(name)` and retain the returned installation ID and agent token securely. The store keeps a hash of the token; it cannot recover the original credential for your application.

Use `GetInstallation(id)` to inspect the last contact and reported printers, `Submit(request)` to queue raw bytes, and `GetJob(id)` to retrieve status. Preserve the idempotency key when retrying the same submission.

## Automatic installer

After checking the current user's ownership, use `PrintEnrollmentService` to create an installer download:

```csharp
var bytes = enrollment.CreateInstaller(
    credentials,
    new Uri("https://your-application.example/printing/"));

// Return from an authenticated, ownership-checked download endpoint:
return Results.File(bytes, "application/octet-stream",
    "SinergiaPrint-Instalar.exe");
```

Set `Cache-Control: no-store` on that personalized download. The EXE contains the public API address and a random, single-use enrollment code valid for 30 minutes. The permanent configuration remains encrypted on the server. The bootstrap redeems it by POST, downloads an immutable version of the generic installer, checks SHA-256, and asks the user to authorize installation in Windows. URL, GUID, and token do not need to be typed.

The bootstrap requires .NET Framework 4.8 (included with Windows 11 and modern Windows 10). The installed Windows x64 agent includes its .NET 10 runtime. Initial installer binaries are unsigned. Expired/used downloads must be regenerated; a canceled installation can require a fresh download.

## Delivery semantics

The agent sends supplied bytes through Windows RAW printing or TCP. Your application generates printer commands such as ZPL or ESC/POS; the agent does not validate the printer language or convert PDF/images.

`Sent` means data was sent, not confirmation of physical printing. Ambiguous/interrupted jobs are not automatically reprinted. Inspect the printer before explicitly submitting a new copy after `Failed` or `Unknown`.
