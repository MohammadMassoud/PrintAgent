using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using SinergiaAI.PrintAgent.Client;
using SinergiaAI.PrintAgent.Contracts;
using SinergiaAI.PrintAgent.Server;

var builder = WebApplication.CreateBuilder(args);
var data = Environment.GetEnvironmentVariable("PRINTAGENT_PACKAGE_TEST_DATA")
    ?? throw new InvalidOperationException("An isolated test directory is required.");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(data, "keys")));
builder.Services.AddSinergiaAIPrintAgentServer(o => o.DatabasePath = Path.Combine(data, "printing.db"));
builder.Services.AddSinergiaAIPrintAgentClient(new Uri("http://localhost:5189/"), "package-test-key-not-a-production-secret");
await using var app = builder.Build();
app.MapSinergiaAIPrintAgent("/printing");
var store = app.Services.GetRequiredService<PrintStore>();
InstallationCredentials credentials = store.CreateInstallation("Single-package consumer");
var enrollment = app.Services.GetRequiredService<PrintEnrollmentService>();
var installer = enrollment.CreateInstaller(credentials, new Uri("http://localhost:5189/printing/"));
if (installer[0] != 'M' || installer[1] != 'Z')
    throw new Exception("Embedded Windows bootstrap is missing.");
var marker = Encoding.ASCII.GetBytes("SINERGIAPRINT-CONTEXT-V1");
if (!installer.AsSpan(installer.Length - marker.Length).SequenceEqual(marker))
    throw new Exception("Installer context marker is missing.");
var count = BitConverter.ToInt32(installer, installer.Length - marker.Length - 4);
using var context = JsonDocument.Parse(installer.AsMemory(installer.Length - marker.Length - 4 - count, count));
var code = context.RootElement.GetProperty("code").GetString()!;
var configuration = enrollment.Redeem(code) ?? throw new Exception("Enrollment failed.");
if (configuration.InstallationId != credentials.InstallationId || enrollment.Redeem(code) is not null)
    throw new Exception("Enrollment must match the installation and be single-use.");
_ = app.Services.GetRequiredService<PrintingClient>();
Console.WriteLine("PASS single NuGet: Server + Client + Contracts, SQLite, embedded EXE, one-use enrollment, endpoints and client DI.");
