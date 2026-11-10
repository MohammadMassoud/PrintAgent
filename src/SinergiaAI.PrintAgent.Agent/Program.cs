using SinergiaAI.PrintAgent.Agent;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Configuration.AddJsonFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SinergiaPrint", "appsettings.Local.json"), optional: true).AddEnvironmentVariables("SINERGIAPRINT_");
var options = builder.Configuration.GetSection("Agent").Get<AgentOptions>() ?? new();
if (options.EnableLegacyApi && ApplicationConnections.HasCredentials(options)) ApplicationConnections.Validate(options);
Directory.CreateDirectory(options.DataPath);
// One service process owns all machine-wide application profiles and their durable journals.
using var processLock = new FileStream(Path.Combine(options.DataPath, "agent-machine.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
// Also exclude older processes that use the original per-installation lock.
using var legacyLock = options.InstallationId == Guid.Empty ? null : new FileStream(Path.Combine(options.DataPath, $"agent-{options.InstallationId:N}.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton<WindowsRawReceiptPrinter>();
builder.Services.AddWindowsService(o => o.ServiceName = "SinergiaPrint Agent");
builder.Services.AddHttpClient("api", http => http.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddSingleton<LocalPrintQueue>();
builder.Services.AddSingleton<LocalPrinters>();
builder.Services.AddHostedService<LocalQueueWorker>();
builder.Services.AddHostedService<LocalApproval>();
builder.Services.AddHostedService<LocalWebSocketBridge>();
if (options.EnableLegacyApi)
{
builder.Services.AddSingleton<ApplicationConnections>();
builder.Services.AddHostedService(provider => provider.GetRequiredService<ApplicationConnections>());
}
await builder.Build().RunAsync();
