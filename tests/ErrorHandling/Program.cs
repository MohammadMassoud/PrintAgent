using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.JSInterop;
using SinergiaAI.PrintAgent;
using SinergiaAI.PrintAgent.Server;

var root = Path.Combine(Path.GetTempPath(), "printagent-errors-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
var cases = new (Exception Error, string Code)[]
{
    (new UnauthorizedAccessException("private storage location"), PrintAgentErrorCodes.StorageAccessDenied),
    (new FileNotFoundException("private loader detail", "Microsoft.Data.Sqlite"), PrintAgentErrorCodes.SqliteDependencyMissing),
    (new DllNotFoundException("e_sqlite3"), PrintAgentErrorCodes.SqliteDependencyMissing),
    (new FileNotFoundException("private loader detail", "Other.Dependency"), PrintAgentErrorCodes.DependencyMissing),
    (new BadImageFormatException("private architecture detail"), PrintAgentErrorCodes.ArchitectureMismatch),
    (new TargetInvocationException(new TypeInitializationException("Provider", new SqliteException("private busy detail", 5))), PrintAgentErrorCodes.DatabaseBusy),
    (new SqliteException("private read-only detail", 8), PrintAgentErrorCodes.DatabaseReadOnly),
    (new AggregateException(new SqliteException("private disk detail", 13)), PrintAgentErrorCodes.StorageFull),
    (new SqliteException("private path detail", 14), PrintAgentErrorCodes.DatabaseUnavailable),
    (new SqliteException("private corruption detail", 11), PrintAgentErrorCodes.DatabaseDamaged),
    (new InvalidOperationException("Call SQLitePCL.raw.SetProvider() first"), PrintAgentErrorCodes.SqliteProviderUnavailable),
    (new InvalidOperationException("token=secret-value; private host information"), PrintAgentErrorCodes.BackendInitializationFailed)
};
foreach (var test in cases)
{
    await using var fixture = new Fixture(root, test.Error);
    try { await fixture.Agent.StartAsync(); throw new Exception("Expected initialization failure"); }
    catch (Exception ex) when (ex.Message.StartsWith("The printing backend could not be initialized", StringComparison.Ordinal)) { }
    Require(fixture.Agent.Status == PrintAgentStatus.ServerUnavailable, "Backend status");
    Verify(fixture, test.Code);
    Require(fixture.Log.Errors.Any(e => e.ToString().Contains(test.Error.Message, StringComparison.Ordinal)), "Original exception must remain in the host log");
}
Console.WriteLine("PASS nested backend failures: storage, SQLite, architecture, busy/read-only/full/damaged databases and unknown failures");

await using (var fixture = new Fixture(root))
{
    fixture.State.User = new();
    try { await fixture.Agent.StartAsync(); throw new Exception("Expected sign-in failure"); }
    catch (UnauthorizedAccessException) { }
    Verify(fixture, PrintAgentErrorCodes.AuthenticationRequired);
    Require(fixture.Agent.Status != PrintAgentStatus.ServerUnavailable, "Sign-in is not a backend initialization failure");
    fixture.State.User = new(new ClaimsIdentity([], "Test"));
    try { await fixture.Agent.StartAsync(); throw new Exception("Expected missing identifier"); }
    catch (UnauthorizedAccessException) { }
    Verify(fixture, PrintAgentErrorCodes.UserIdentifierMissing);
    fixture.State.User = Fixture.User();
    fixture.Browser.Result = new { status = "unavailable", error = "private browser detail" };
    await fixture.Agent.StartAsync();
    Verify(fixture, PrintAgentErrorCodes.AgentUnreachable);
    Require(fixture.Agent.LastErrorSteps.Any(s => s.Contains("does not prove", StringComparison.Ordinal)), "A browser failure must not assert that the service is missing");
    var reference = fixture.Agent.LastErrorReferenceId;
    await fixture.Agent.StartAsync();
    Require(reference == fixture.Agent.LastErrorReferenceId, "Repeated state keeps its reference");
    using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
    try { await fixture.Agent.StartAsync(cancellation.Token); throw new Exception("Expected cancellation"); }
    catch (OperationCanceledException) { }
    Require(reference == fixture.Agent.LastErrorReferenceId, "Cancellation must not overwrite an actionable error");
    fixture.Browser.Failure = new TaskCanceledException("private timeout detail");
    try { await fixture.Agent.StartAsync(); throw new Exception("Expected timeout"); }
    catch (TaskCanceledException) { }
    Verify(fixture, PrintAgentErrorCodes.OperationTimedOut);
    fixture.Browser.Failure = null;
    fixture.Browser.Result = new { status = "error", errorCode = "AUTHENTICATION_REQUIRED", error = "token=secret-value" };
    await fixture.Agent.StartAsync();
    Verify(fixture, PrintAgentErrorCodes.AuthenticationRequired);
    fixture.Browser.Result = new { status = "error", errorCode = "untrusted-secret-code", error = "private browser detail" };
    await fixture.Agent.StartAsync();
    Verify(fixture, PrintAgentErrorCodes.UnexpectedError);

    var store = fixture.App.Services.GetRequiredService<PrintStore>();
    var credentials = store.CreateInstallation("Error recovery fixture");
    var discovery = fixture.App.Services.GetRequiredService<PrintDiscoveryService>();
    var proof = discovery.RegisterMachine(credentials, "Simulated workstation");
    store.Report(credentials.InstallationId, []);
    fixture.Browser.Result = new { status = "connected", comprovante = proof.Proof, version = "0.5.1" };
    await fixture.Agent.StartAsync();
    Require(fixture.Agent.IsConnected && fixture.Agent.LastError is null && fixture.Agent.LastErrorHelp is null && fixture.Agent.LastErrorSteps.Count == 0, "Recovery clears all stale error fields");
    await fixture.Agent.UnlinkAsync();
    Require(fixture.Agent.LastError is null, "Unlink clears errors");
}
Console.WriteLine("PASS real facade authentication, browser failures, safe codes, cancellation, reference stability, recovery and unlink");

var blocked = Path.Combine(root, "a-file-not-a-directory");
File.WriteAllText(blocked, "preserve this file");
await using (var fixture = new Fixture(root, dataOverride: blocked))
{
    try { await fixture.Agent.StartAsync(); throw new Exception("Expected invalid storage"); }
    catch (Exception ex) when (ex.Message.StartsWith("The printing backend could not be initialized", StringComparison.Ordinal)) { }
    Verify(fixture, PrintAgentErrorCodes.DataDirectoryInvalid);
    Require(File.ReadAllText(blocked) == "preserve this file", "Storage failure must preserve files");
}
Console.WriteLine("PASS actual invalid storage path; no jobs submitted or files deleted");
Console.WriteLine("All actionable-error checks passed. Isolated data: " + root);

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Verify(Fixture fixture, string code)
{
    var agent = fixture.Agent;
    Require(agent.LastErrorCode == code, $"Expected {code}, received {agent.LastErrorCode}");
    Require(agent.LastErrorMessage is not null && agent.LastErrorSteps.Count >= 2 && agent.LastErrorHelp?.StartsWith("1. ", StringComparison.Ordinal) == true, "Numbered resolution guidance");
    Require(agent.LastErrorReferenceId is { Length: 12 } && fixture.Log.Messages.Any(m => m.Contains(agent.LastErrorReferenceId, StringComparison.Ordinal) && m.Contains(code, StringComparison.Ordinal)), "Log reference correlates with public error");
    var visible = JsonSerializer.Serialize(agent.LastError);
    Require(!visible.Contains("private", StringComparison.OrdinalIgnoreCase) && !visible.Contains("secret-value", StringComparison.Ordinal), "No raw exception/browser information in public fields");
    Require(agent.LastErrorSteps is System.Collections.ObjectModel.ReadOnlyCollection<string>, "Resolution steps are read-only");
}

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
    public object Result { get; set; } = new { status = "unavailable" };
    public Exception? Failure { get; set; }
    private readonly Dictionary<string, string> _storage = [];
    public ValueTask<T> InvokeAsync<T>(string identifier, object?[]? args) => InvokeAsync<T>(identifier, default, args);
    public ValueTask<T> InvokeAsync<T>(string identifier, CancellationToken ct, object?[]? args)
    {
        ct.ThrowIfCancellationRequested();
        if (Failure is not null) throw Failure;
        if (identifier == "import") return ValueTask.FromResult((T)(object)this);
        if (identifier == "discover") return ValueTask.FromResult(JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(Result), new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
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
