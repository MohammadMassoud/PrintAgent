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
    if (agent.LastError is not null || agent.LastErrorMessage is not null || agent.LastErrorHelp is not null
        || agent.LastErrorCode is not null || agent.LastErrorReferenceId is not null || agent.LastErrorSteps.Count != 0)
        throw new Exception("Actionable error fields must be clear before the first operation.");
    _ = typeof(PrintAgentError);
    var textDocument = agent.CreateTextDocument("test", "^XA^FDHello^FS^XZ", "saved-key");
    if (!textDocument.Content.SequenceEqual(Encoding.UTF8.GetBytes("^XA^FDHello^FS^XZ")))
        throw new Exception("The packaged facade must prepare UTF-8 text without a BOM.");
    Func<string, string, string, CancellationToken, Task<PrintJob>> textSend = agent.SendAsync;
    Func<string, byte[], string, CancellationToken, Task<PrintJob>> byteSend = agent.SendByteAsync;
    Func<string, byte[], string?, CancellationToken, Task<PrintJob>> unifiedByteSend = agent.SendAsync;
    Func<string, IReadOnlyList<string>, CancellationToken, Task<IReadOnlyList<PrintJob>>> listSend = agent.SendAsync;
    Func<IReadOnlyList<PrintDocument>, CancellationToken, Task<IReadOnlyList<PrintJob>>> documentSend = agent.SendAsync;
    if (new PrintDocument("test", "commands").IdempotencyKey.Length == 0)
        throw new Exception("Documents must retain generated identities when the caller omits a key.");
    _ = unifiedByteSend; _ = listSend; _ = documentSend;
    _ = textSend; _ = byteSend;
    _ = PrintAgentErrorCodes.DataDirectoryAccessDenied;
    using var module = typeof(PrintAgent).Assembly.GetManifestResourceStream("PrintAgent.Browser.js")
        ?? throw new Exception("The browser bridge must be embedded in the NuGet assembly.");
    if (!(await new StreamReader(module).ReadToEndAsync()).Contains("new WebSocket"))
        throw new Exception("Invalid embedded browser bridge.");
}
if (app.Services.GetService<PrintStore>() is not null || app.Services.GetService<PrintEnrollmentService>() is not null)
    throw new Exception("Direct integration must not register or initialize the legacy backend.");
if (File.Exists(Path.Combine(data, "printing.db"))) throw new Exception("Direct integration created a server database.");
_ = app.Services.GetRequiredService<PrintingClient>();
Console.WriteLine("PASS single NuGet: root PrintAgent facade/models/DI, embedded browser module, Server + Client + Contracts, no backend database or enrollment, WebSocket browser module. No Common dependency.");
