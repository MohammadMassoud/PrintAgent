using SinergiaAI.PrintAgent.Agent;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Configuration.AddJsonFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SinergiaPrint", "appsettings.Local.json"), optional: true).AddEnvironmentVariables("SINERGIAPRINT_");
var options = builder.Configuration.GetSection("Agent").Get<AgentOptions>() ?? new();
if (!Uri.TryCreate(options.ApiBaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))) throw new InvalidOperationException("Set Agent:ApiBaseUrl to an HTTPS URL (HTTP is allowed only on loopback).");
if (options.InstallationId == Guid.Empty || string.IsNullOrWhiteSpace(options.Token)) throw new InvalidOperationException("Set Agent:InstallationId and Agent:Token.");
Directory.CreateDirectory(options.DataPath);
// One worker per installation on this computer; keeps its durable journal consistent.
using var processLock = new FileStream(Path.Combine(options.DataPath, $"agent-{options.InstallationId:N}.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(new AgentJournal(options.DataPath, options.InstallationId));
builder.Services.AddSingleton<WindowsRawReceiptPrinter>();
builder.Services.AddWindowsService(o => o.ServiceName = "SinergiaPrint Agent");
builder.Services.AddHttpClient("api", http => { http.BaseAddress = new Uri(options.ApiBaseUrl.TrimEnd('/') + $"/agent/{options.InstallationId}/"); http.DefaultRequestHeaders.Add("X-Agent-Token", options.Token); http.Timeout = TimeSpan.FromSeconds(45); });
builder.Services.AddHostedService<AgentWorker>();
builder.Services.AddHostedService<LocalAgentBridge>();
await builder.Build().RunAsync();
