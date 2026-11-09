using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SinergiaAI.PrintAgent.Agent;

/// <summary>Machine-wide application profiles. Credentials stay in the service's protected data directory.</summary>
public sealed class ApplicationConnections : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly AgentOptions _machine;
    private readonly IHttpClientFactory _clients;
    private readonly WindowsRawReceiptPrinter _printer;
    private readonly ILoggerFactory _logs;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<AgentOptions> _profiles = [];
    private readonly List<AgentWorker> _workers = [];
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationToken _stopping;
    private string FilePath => Path.Combine(_machine.DataPath, "applications.json");

    public ApplicationConnections(AgentOptions machine, IHttpClientFactory clients, WindowsRawReceiptPrinter printer, ILoggerFactory logs)
    { _machine = machine; _clients = clients; _printer = printer; _logs = logs; }

    public static string Normalize(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.UserInfo.Length != 0
            || uri.Query.Length != 0 || uri.Fragment.Length != 0
            || uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            throw new ArgumentException("Use an HTTPS API URL (HTTP is allowed only on loopback).");
        return uri.AbsoluteUri.TrimEnd('/') + "/";
    }

    public static bool HasCredentials(AgentOptions profile) => !string.IsNullOrEmpty(profile.ApiBaseUrl)
        || profile.InstallationId != Guid.Empty || !string.IsNullOrEmpty(profile.Token);

    public static void Validate(AgentOptions profile)
    {
        _ = Normalize(profile.ApiBaseUrl);
        if (profile.InstallationId == Guid.Empty || string.IsNullOrWhiteSpace(profile.Token))
            throw new ArgumentException("An application profile requires an installation ID and token.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _stopping = stoppingToken;
        try
        {
            if (File.Exists(FilePath))
            {
                var profiles = JsonSerializer.Deserialize<List<AgentOptions>>(await File.ReadAllTextAsync(FilePath, stoppingToken), Json)
                    ?? throw new InvalidDataException("Invalid application profile file.");
                if (profiles.Count > 32) throw new InvalidDataException("Too many application profiles.");
                foreach (var profile in profiles)
                {
                    Validate(profile);
                    if (_profiles.Any(p => Normalize(p.ApiBaseUrl) == Normalize(profile.ApiBaseUrl)))
                        throw new InvalidDataException("Duplicate application profile.");
                    _profiles.Add(profile);
                }
            }
            // Preserve the existing installation and its journal when upgrading a single-application agent.
            if (HasCredentials(_machine) && !_profiles.Any(p => Normalize(p.ApiBaseUrl) == Normalize(_machine.ApiBaseUrl)))
                _profiles.Add(_machine);
            await PersistAsync(stoppingToken);
            foreach (var profile in _profiles) await StartWorkerAsync(profile, stoppingToken);
            _ready.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { _ready.TrySetCanceled(stoppingToken); }
        catch (Exception ex) { _ready.TrySetException(ex); throw; }
        finally
        {
            await _gate.WaitAsync(CancellationToken.None);
            try
            {
                foreach (var worker in _workers) { await worker.StopAsync(CancellationToken.None); worker.Dispose(); }
            }
            finally { _gate.Release(); }
        }
    }

    public async Task<AgentOptions?> FindAsync(string api, CancellationToken ct)
    {
        await _ready.Task.WaitAsync(ct);
        await _gate.WaitAsync(ct);
        try { return _profiles.FirstOrDefault(p => Normalize(p.ApiBaseUrl) == Normalize(api)); }
        finally { _gate.Release(); }
    }

    public async Task<AgentOptions> AddAsync(string api, Func<Task<AgentOptions>> enroll, CancellationToken ct)
    {
        await _ready.Task.WaitAsync(ct);
        await _gate.WaitAsync(ct);
        try
        {
            _stopping.ThrowIfCancellationRequested();
            if (_profiles.FirstOrDefault(p => Normalize(p.ApiBaseUrl) == Normalize(api)) is { } existing) return existing;
            if (_profiles.Count >= 32) throw new InvalidOperationException("This computer has reached the limit of 32 applications.");
            var profile = await enroll();
            Validate(profile);
            if (Normalize(profile.ApiBaseUrl) != Normalize(api)) throw new InvalidDataException("Enrollment returned a different API URL.");
            var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(api))))[..24];
            profile.DataPath = Path.Combine(_machine.DataPath, "applications", key);
            profile.LocalBridgePort = 0;
            profile.PollSeconds = _machine.PollSeconds;
            profile.LongPollSeconds = _machine.LongPollSeconds;
            profile.NetworkPrinters = _machine.NetworkPrinters;
            _profiles.Add(profile);
            try { await PersistAsync(ct); }
            catch { _profiles.Remove(profile); throw; }
            // The committed profile survives disconnection or process restart. Start only one worker per API.
            await StartWorkerAsync(profile, _stopping);
            return profile;
        }
        finally { _gate.Release(); }
    }

    private async Task PersistAsync(CancellationToken ct)
    {
        var temporary = FilePath + ".tmp";
        await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(_profiles, Json), ct);
        File.Move(temporary, FilePath, overwrite: true);
    }

    private async Task StartWorkerAsync(AgentOptions profile, CancellationToken ct)
    {
        profile.NetworkPrinters = _machine.NetworkPrinters;
        profile.PollSeconds = _machine.PollSeconds;
        profile.LongPollSeconds = _machine.LongPollSeconds;
        var worker = new AgentWorker(profile, new ProfileClients(_clients, profile),
            new AgentJournal(profile.DataPath, profile.InstallationId), _printer, _logs.CreateLogger<AgentWorker>());
        await worker.StartAsync(ct);
        _workers.Add(worker);
    }

    private sealed class ProfileClients(IHttpClientFactory clients, AgentOptions profile) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = clients.CreateClient("api");
            client.BaseAddress = new Uri(Normalize(profile.ApiBaseUrl) + $"agent/{profile.InstallationId}/");
            client.DefaultRequestHeaders.Add("X-Agent-Token", profile.Token);
            return client;
        }
    }
}
