using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SinergiaAI.PrintAgent.Contracts;
using SinergiaAI.PrintAgent.Server;

namespace SinergiaAI.PrintAgent;

internal sealed class PrintAgentBackend(
    PrintStore store,
    AuthenticationStateProvider authentication,
    IDataProtectionProvider protection,
    IOptions<PrintAgentOptions> settings,
    IWebHostEnvironment environment,
    IServer server,
    PrintEnrollmentService enrollment,
    PrintDiscoveryService discovery)
{
    private string? _machineProof;
    private PrintAgentOptions Options => settings.Value;
    private string DataDirectory => Path.GetFullPath(Options.DataDirectory, environment.ContentRootPath);
    private string ApiPrefix => Options.ApiPrefix.Trim('/');

    private Uri GetApiUrl(Uri application)
    {
        if (!string.IsNullOrWhiteSpace(Options.PublicBaseUrl))
        {
            if (!Uri.TryCreate(Options.PublicBaseUrl, UriKind.Absolute, out var endpoint)
                || endpoint.UserInfo.Length != 0 || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0
                || (endpoint.Scheme != "https" && !(endpoint.Scheme == "http" && endpoint.IsLoopback)))
                throw new InvalidOperationException("Configure the public agent API URL using HTTPS (HTTP is allowed only on loopback).");
            return new Uri(endpoint.AbsoluteUri.TrimEnd('/') + "/");
        }
        if (environment.IsDevelopment() && application.IsLoopback)
        {
            var address = server.Features.Get<IServerAddressesFeature>()?.Addresses
                .Select(x => Uri.TryCreate(x, UriKind.Absolute, out var uri) ? uri : null)
                .FirstOrDefault(x => x is { Scheme: "http", IsLoopback: true });
            if (address is not null)
                application = new UriBuilder(application) { Scheme = "http", Port = address.Port }.Uri;
        }
        return new Uri(application, ApiPrefix + "/");
    }

    public string GetInstallerUrl(Uri application, Guid installationId) =>
        new Uri(application, ApiPrefix + "/installers/" + installationId).AbsoluteUri;

    private async Task<string> GetUserIdAsync()
    {
        var user = (await authentication.GetAuthenticationStateAsync()).User;
        return user.Identity?.IsAuthenticated == true
            ? user.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new UnauthorizedAccessException("The authenticated user has no identifier.")
            : throw new UnauthorizedAccessException("Sign in to use printing.");
    }

    public async Task<string> GetBrowserStorageKeyAsync()
    {
        _ = await GetUserIdAsync();
        return Options.BrowserStoragePrefix + "machine.v1";
    }

    public async Task<string> GetLegacyStorageKeyAsync() => Options.BrowserStoragePrefix
        + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(await GetUserIdAsync())))[..24];

    public BrowserDiscoveryOptions GetDiscoveryOptions(Uri application) => new(
        new Uri(application, ApiPrefix + "/").AbsoluteUri,
        GetApiUrl(application).AbsoluteUri,
        "http://127.0.0.1:" + Options.LocalBridgePort);

    public string GetBrowserModuleUrl(Uri application) => new Uri(application, ApiPrefix + "/browser.js?v=6").AbsoluteUri;

    public InstallerContext CreateApplicationEnrollment(Uri application, ClaimsPrincipal user, CancellationToken ct)
    {
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(user.FindFirstValue(ClaimTypes.NameIdentifier)))
            throw new UnauthorizedAccessException();
        ct.ThrowIfCancellationRequested();
        var credentials = store.CreateInstallation("Local workstation");
        return enrollment.CreateCode(credentials, GetApiUrl(application));
    }

    private MachineConnectionState? Connection(string? proof)
    {
        var machine = discovery.GetMachine(proof);
        return machine is null ? null : new(machine.Installation, proof!, machine.MachineName);
    }

    public async Task<MachineConnectionState?> RecognizeMachineAsync(string proof, CancellationToken ct = default)
    {
        _ = await GetUserIdAsync(); ct.ThrowIfCancellationRequested();
        var connection = Connection(proof);
        _machineProof = connection is null ? null : proof;
        return connection;
    }

    public async Task<MachineConnectionState?> GetConnectionAsync(Guid installationId, CancellationToken ct = default)
    {
        _ = await GetUserIdAsync(); ct.ThrowIfCancellationRequested();
        var connection = Connection(_machineProof);
        return connection?.Installation.Id == installationId ? connection : null;
    }

    // Migration only: an old user-owned record can be imported by its original owner.
    // New machine access is independent of this legacy user ID.
    private sealed record LegacyLink([property: JsonPropertyName("UsuarioId")] string UserId, string Token);
    public async Task<MachineConnectionState?> MigrateConnectionAsync(Guid installationId, CancellationToken ct = default)
    {
        var user = await GetUserIdAsync();
        var path = Path.Combine(DataDirectory, "links", installationId.ToString("N") + ".protected");
        if (!File.Exists(path)) return null;
        try
        {
            var protector = protection.CreateProtector(Options.LegacyProtectionPurpose, installationId.ToString("N"));
            var legacy = JsonSerializer.Deserialize<LegacyLink>(protector.Unprotect(await File.ReadAllTextAsync(path, ct)));
            if (legacy?.UserId != user || store.GetInstallation(installationId) is not { } installation) return null;
            var proof = discovery.RegisterMachine(new(installationId, legacy.Token), installation.Name);
            return await RecognizeMachineAsync(proof.Proof, ct);
        }
        catch (CryptographicException) { return null; }
        catch (JsonException) { return null; }
    }

    public async Task<MachineConnectionState> CreateConnectionAsync(string name, CancellationToken ct = default)
    {
        _ = await GetUserIdAsync();
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 100)
            throw new InvalidOperationException("Provide a machine name with up to 100 characters.");
        ct.ThrowIfCancellationRequested();
        var credentials = store.CreateInstallation(name.Trim());
        var proof = discovery.RegisterMachine(credentials, name.Trim());
        return (await RecognizeMachineAsync(proof.Proof, ct))!;
    }

    public Task<byte[]> CreateInstallerAsync(Guid installationId, Uri application, ClaimsPrincipal user, string proof, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (user.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(user.FindFirstValue(ClaimTypes.NameIdentifier)))
            throw new UnauthorizedAccessException();
        var credentials = discovery.GetCredentials(proof);
        if (credentials.InstallationId != installationId) throw new UnauthorizedAccessException();
        return Task.FromResult(enrollment.CreateInstaller(credentials, GetApiUrl(application)));
    }

    public async Task ForgetConnectionAsync(Guid installationId, CancellationToken ct = default)
    {
        _ = await GetConnectionAsync(installationId, ct) ?? throw new UnauthorizedAccessException("This computer has not been recognized in this session.");
        discovery.ForgetProof(_machineProof!);
        _machineProof = null;
    }

    public async Task<PrintJobInfo> SubmitAsync(Guid installationId, PrintDocument document, CancellationToken ct = default) =>
        (await SubmitBatchAsync(installationId, new[] { document }, ct))[0];

    /// <summary>Queues documents in input order. Reuse request keys after partial submission to avoid duplicate jobs.</summary>
    public async Task<IReadOnlyList<PrintJobInfo>> SubmitBatchAsync(Guid installationId, IReadOnlyList<PrintDocument> documents, CancellationToken ct = default)
    {
        var connection = await GetConnectionAsync(installationId, ct) ?? throw new UnauthorizedAccessException("This computer has not been recognized in this session.");
        if (documents.Count is < 1 or > 5000) throw new ArgumentException("Submit between 1 and 5000 documents per batch.");
        if (documents.Select(x => x.IdempotencyKey).Distinct(StringComparer.Ordinal).Count() != documents.Count)
            throw new ArgumentException("Each document requires a distinct idempotency key.");
        // Validate the entire request before starting. Printer language remains the caller's responsibility.
        foreach (var document in documents)
        {
            if (!connection.Installation.Printers.Any(x => x.Id == document.PrinterId))
                throw new InvalidOperationException("Select a printer reported by this agent.");
            if (document.Content is null || document.Content.Length is < 1 or > 2097152
                || string.IsNullOrWhiteSpace(document.IdempotencyKey) || document.IdempotencyKey.Length > 200)
                throw new ArgumentException("Provide content up to 2 MiB and an idempotency key up to 200 characters.");
        }
        var jobs = new List<PrintJobInfo>(documents.Count);
        foreach (var document in documents)
        {
            ct.ThrowIfCancellationRequested();
            jobs.Add(store.Submit(new(installationId, document.PrinterId, Convert.ToBase64String(document.Content), document.IdempotencyKey)));
            if (jobs.Count % 25 == 0) await Task.Yield();
        }
        return jobs;
    }

    public async Task<IReadOnlyList<PrintJobInfo>> GetJobsAsync(Guid installationId, IReadOnlyCollection<Guid> jobIds, CancellationToken ct = default)
    {
        _ = await GetConnectionAsync(installationId, ct) ?? throw new UnauthorizedAccessException("This computer has not been recognized in this session.");
        if (jobIds.Count > 5000) throw new ArgumentException("Query up to 5000 jobs at a time.");
        var results = new List<PrintJobInfo>();
        foreach (var id in jobIds.Distinct())
        {
            ct.ThrowIfCancellationRequested();
            var job = store.GetJob(id);
            if (job?.InstallationId == installationId) results.Add(job);
        }
        return results;
    }
}
