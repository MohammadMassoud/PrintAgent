using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.JSInterop;
using SinergiaAI.PrintAgent;
using SinergiaAI.PrintAgent.Server;
var root = Path.Combine(Path.GetTempPath(),"printagent-direct-errors-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
await using var fixture = new Fixture(root);
Require(fixture.App.Services.GetService<PrintStore>() is null,"No backend storage registration");
foreach (var code in new[] { PrintAgentErrorCodes.AgentUnreachable, PrintAgentErrorCodes.OriginDenied, PrintAgentErrorCodes.AgentUpdateRequired, PrintAgentErrorCodes.LocalStorageUnavailable })
{
    fixture.Browser.Result = new { error = code, version = "1.0.0" };
    await fixture.Agent.StartAsync();
    Require(fixture.Agent.LastErrorCode == code && fixture.Agent.LastErrorSteps.Count > 0,"Actionable direct error " + code);
    Require(fixture.Agent.LastErrorReferenceId?.Length == 12,"Host log reference");
}
fixture.Browser.Result = new { error = "private-secret-code" };
await fixture.Agent.StartAsync();
Require(fixture.Agent.LastErrorCode == PrintAgentErrorCodes.UnexpectedError,"Unknown code sanitized");
Require(!fixture.Agent.LastErrorHelp!.Contains("private-secret"),"No raw error exposure");
fixture.Browser.Result = new { protocol = 2, version = "1.0.0", machineId = Guid.NewGuid(), machineName = "Fixture", printers = new[] { new PrintPrinter("tcp:test","Test",false) } };
await fixture.Agent.StartAsync();
Require(fixture.Agent.IsConnected && fixture.Agent.LastError is null && fixture.Agent.Printers.Count == 1,"Healthy recovery clears errors");
fixture.Agent.BridgeClosed();
Require(!fixture.Agent.IsConnected && fixture.Agent.Printers.Count == 0,"Socket close clears connection and printers");
await fixture.Agent.UnlinkAsync();
Require(fixture.Agent.LastError is null && fixture.Agent.Status == PrintAgentStatus.Unlinked,"Disconnect clears errors");
Console.WriteLine("PASS direct facade: no backend, curated errors, safe codes, recovery, socket closure and unlink.");
static void Require(bool condition,string message) { if (!condition) throw new Exception(message); }
sealed class Fixture : IAsyncDisposable
{
    public WebApplication App { get; }
    private readonly AsyncServiceScope _scope;
    public PrintAgent Agent { get; }
    public State State { get; } = new();
    public Browser Browser { get; } = new();
    public CaptureLog Log { get; } = new();
    public Fixture(string root, Exception? failure = null, string? dataOverride = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = root });
        builder.Logging.ClearProviders();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();
        builder.Services.AddAuthentication().AddCookie();
        builder.Services.AddAuthorization();
        builder.Services.AddSingleton<AuthenticationStateProvider>(State);
        builder.Services.AddScoped<IJSRuntime>(_ => Browser);
        builder.Services.AddScoped<NavigationManager, Navigation>();
        builder.Services.AddSingleton<ILogger<PrintAgent>>(Log);
        builder.Services.AddPrintAgent(o => { o.DataDirectory = dataOverride ?? Path.Combine(root, Guid.NewGuid().ToString("N")); o.PublicBaseUrl = "http://localhost:5199/printing/"; });
        if (failure is not null) builder.Services.AddScoped(typeof(PrintAgent).Assembly.GetType("SinergiaAI.PrintAgent.PrintAgentBackend", true)!, _ => throw failure);
        App = builder.Build();
        _scope = App.Services.CreateAsyncScope();
        Agent = _scope.ServiceProvider.GetRequiredService<PrintAgent>();
    }
    public static ClaimsPrincipal User() => new(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "error-test-user")], "Test"));
    public async ValueTask DisposeAsync() { await _scope.DisposeAsync(); await App.DisposeAsync(); }
}
sealed class State : AuthenticationStateProvider
{
    public ClaimsPrincipal User { get; set; } = Fixture.User();
    public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(User));
}
sealed class Navigation : NavigationManager { public Navigation() => Initialize("http://localhost:5199/", "http://localhost:5199/"); }
sealed class Browser : IJSRuntime, IJSObjectReference
{
    public object Result { get; set; } = new { error = "AGENT_UNREACHABLE" };
    public Exception? Failure { get; set; }
    private readonly Dictionary<string, string> _storage = [];
    public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
    public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args)
    {
        ct.ThrowIfCancellationRequested();
        if (Failure is not null) throw Failure;
        if (identifier == "import") return ValueTask.FromResult((T)(object)this);
        if (identifier == "connect") return ValueTask.FromResult(JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(Result), new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
        if (identifier == "localStorage.getItem") return ValueTask.FromResult((T)(object?)_storage.GetValueOrDefault((string)args![0]!)!);
        if (identifier == "localStorage.setItem") _storage[(string)args![0]!] = (string)args[1]!;
        if (identifier == "localStorage.removeItem") _storage.Remove((string)args![0]!);
        return ValueTask.FromResult(default(T)!);
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
sealed class CaptureLog : ILogger<PrintAgent>
{
    public List<string> Messages { get; } = [];
    public List<Exception> Errors { get; } = [];
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel level) => true;
    public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
    { Messages.Add(formatter(state, error)); if (error is not null) Errors.Add(error); }
}
