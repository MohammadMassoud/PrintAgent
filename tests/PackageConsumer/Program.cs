using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using SinergiaAI.PrintAgent.Client;
using SinergiaAI.PrintAgent;
using SinergiaAI.PrintAgent.Server;

var builder = WebApplication.CreateBuilder(args);
var data = Environment.GetEnvironmentVariable("PRINTAGENT_PACKAGE_TEST_DATA")
    ?? throw new InvalidOperationException("An isolated test directory is required.");
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(data, "keys")));
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddAuthentication().AddCookie();
builder.Services.AddAuthorization();
builder.Services.AddPrintAgent(o => o.DataDirectory = data);
builder.Services.AddSinergiaAIPrintAgentClient(new Uri("http://localhost:5189/"), "package-test-key-not-a-production-secret");
await using var app = builder.Build();
app.MapPrintAgent();
await using (var scope = app.Services.CreateAsyncScope())
{
    var agent = scope.ServiceProvider.GetRequiredService<PrintAgent>();
    if (agent.IsConnected || agent.Status != PrintAgentStatus.NotStarted || agent.Printers.Count != 0 || agent.PrintersInfo.Count != 0)
        throw new Exception("The root-namespace facade must start disconnected with empty read-only printer lists.");
    using var module = typeof(PrintAgent).Assembly.GetManifestResourceStream("PrintAgent.Browser.js")
        ?? throw new Exception("The browser bridge must be embedded in the NuGet assembly.");
    if (!(await new StreamReader(module).ReadToEndAsync()).Contains("export async function discover"))
        throw new Exception("Invalid embedded browser bridge.");
}
var store = app.Services.GetRequiredService<PrintStore>();
var credentials = store.CreateInstallation("Single-package consumer");
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
Console.WriteLine("PASS single NuGet: root PrintAgent facade/models/DI, embedded browser module, Server + Client + Contracts, SQLite, embedded EXE, one-use enrollment and endpoints. No Common dependency.");
