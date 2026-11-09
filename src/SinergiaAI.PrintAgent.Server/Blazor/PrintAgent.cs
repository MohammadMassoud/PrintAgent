using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Microsoft.Extensions.DependencyInjection;

namespace SinergiaAI.PrintAgent;

/// <summary>Manages browser discovery, machine proofs, enrollment and Windows agent version checks.</summary>
public sealed class PrintAgent(
    IServiceProvider services,
    IJSRuntime js,
    IOptions<PrintAgentOptions> options,
    NavigationManager navigation,
    ILogger<PrintAgent> logger) : IAsyncDisposable
{
    private sealed class BackendInitializationException(Exception inner)
        : Exception("The printing backend could not be initialized. Check the host log, SQLite dependencies and data directory permissions.", inner);
    private PrintAgentBackend printing
    {
        get
        {
            try { return services.GetRequiredService<PrintAgentBackend>(); }
            catch (Exception ex) { throw new BackendInitializationException(ex); }
        }
    }
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IJSObjectReference? _browser;
    private MachineConnectionState? _connection;
    private string? _storageKey;
    private bool _machineConfirmed;
    public IReadOnlyList<string> Printers { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<PrintPrinter> PrintersInfo { get; private set; } = Array.Empty<PrintPrinter>();
    public PrintAgentStatus Status { get; private set; } = PrintAgentStatus.NotStarted;
    public bool IsConnected => _machineConfirmed && Status == PrintAgentStatus.Connected && RecentContact;
    public bool MachineConfirmed => _machineConfirmed;
    public Guid? DeviceId => _connection?.Installation.Id;
    public string? MachineName => _connection?.MachineName;
    public string? LastErrorMessage { get; private set; }
    public string? InstalledVersion { get; private set; }
    public string AvailableVersion => options.Value.AvailableAgentVersion;
    public bool UpdateAvailable { get; private set; }
    public string BrowserStorageKey => _storageKey ?? options.Value.BrowserStoragePrefix + "machine.v1";
    public event Action? OnStatusChanged;
    public event Action? OnGetPrinters;
    private bool RecentContact => _connection?.Installation.LastSeenAt is { } last
        && DateTimeOffset.UtcNow - last < TimeSpan.FromSeconds(60);

    private void UpdatePrinters()
    {
        var printers = _machineConfirmed ? _connection?.Installation.Printers.Select(p => new PrintPrinter(p.Id, p.Name, p.IsDefault)).ToArray() ?? [] : [];
        if (PrintersInfo.SequenceEqual(printers)) return;
        PrintersInfo = Array.AsReadOnly(printers);
        Printers = Array.AsReadOnly(printers.Select(p => p.Name).ToArray());
    }

    // Subscribers are UI observers. A disposed/faulty observer must not invalidate a successful operation.
    private void Notify(Action? subscribers)
    {
        if (subscribers is null) return;
        foreach (Action subscriber in subscribers.GetInvocationList())
            try { subscriber(); }
            catch (Exception ex) { logger.LogWarning(ex, "A print connection observer failed."); }
    }

    public async Task StartAsync(CancellationToken ct = default) =>
        _ = await DiscoverAsync(new Uri(navigation.BaseUri), ct);

    public async Task DownloadInstallerAsync(CancellationToken ct = default) =>
        _ = await DownloadInstallerForAsync(new Uri(navigation.BaseUri), ct);

    public async Task RefreshPrintersAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        var previousPrinters = PrintersInfo;
        var previousStatus = Status;
        try
        {
            if (_connection is null) return;
            _connection = await printing.GetConnectionAsync(_connection.Installation.Id, ct);
            if (_connection is null)
            {
                _machineConfirmed = false;
                Status = PrintAgentStatus.Unavailable;
                LastErrorMessage = "Recognize this computer again.";
                throw new InvalidOperationException(LastErrorMessage);
            }
            if (_machineConfirmed)
            {
                Status = RecentContact ? PrintAgentStatus.Connected : PrintAgentStatus.AwaitingApi;
                LastErrorMessage = null;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastErrorMessage = "Could not query the connection on the server.";
            Status = ex is BackendInitializationException ? PrintAgentStatus.ServerUnavailable : PrintAgentStatus.Error;
            throw;
        }
        finally
        {
            UpdatePrinters();
            _gate.Release();
            if (previousStatus != Status) Notify(OnStatusChanged);
            if (!ReferenceEquals(previousPrinters, PrintersInfo)) Notify(OnGetPrinters);
        }
    }

    private Guid CurrentDevice() => DeviceId ?? throw new InvalidOperationException("Recognize this computer before using printing.");

    public async Task<PrintJob> SendAsync(string printerId, byte[] content, string idempotencyKey, CancellationToken ct = default) =>
        (await SendBatchAsync(new[] { new PrintDocument(printerId, content, idempotencyKey) }, ct))[0];

    public async Task<IReadOnlyList<PrintJob>> SendBatchAsync(IReadOnlyList<PrintDocument> documents, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (!IsConnected) throw new InvalidOperationException("Recognize the connected agent on this computer before printing.");
            return (await printing.SubmitBatchAsync(CurrentDevice(), documents, ct)).Select(PrintJob.FromContract).ToArray();
        }
        finally { _gate.Release(); }
    }

    public async Task<IReadOnlyList<PrintJob>> GetJobsAsync(IReadOnlyCollection<Guid> jobIds, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { return (await printing.GetJobsAsync(CurrentDevice(), jobIds, ct)).Select(PrintJob.FromContract).ToArray(); }
        finally { _gate.Release(); }
    }
    private sealed record BrowserDiscoveryResult(string Status, [property: JsonPropertyName("comprovante")] string? Proof, string? Version, string? Error);

    private async Task PrepareBrowserAsync(Uri application, CancellationToken ct)
    {
        _storageKey ??= await printing.GetBrowserStorageKeyAsync();
        _browser ??= await js.InvokeAsync<IJSObjectReference>("import", ct, printing.GetBrowserModuleUrl(application));
        var saved = await js.InvokeAsync<string?>("localStorage.getItem", ct, _storageKey);
        _connection = string.IsNullOrWhiteSpace(saved) ? null : await printing.RecognizeMachineAsync(saved, ct);
    }

    internal async Task<DiscoveryResult> DiscoverAsync(Uri application, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        var previousPrinters = PrintersInfo;
        try
        {
            await PrepareBrowserAsync(application, ct);
            var result = await _browser!.InvokeAsync<BrowserDiscoveryResult>("discover", ct,
                printing.GetDiscoveryOptions(application), _connection?.Proof);
            if (result.Status == "connected" && !string.IsNullOrWhiteSpace(result.Proof))
            {
                _connection = await printing.RecognizeMachineAsync(result.Proof, ct)
                    ?? throw new InvalidOperationException("Machine recognition expired. Recognize this computer again.");
                await js.InvokeVoidAsync("localStorage.setItem", ct, _storageKey, _connection.Proof);
            }
            // Only the original owner may import a legacy link. Migration alone does not prove local presence.
            if (_connection is null)
            {
                var legacyKey = await printing.GetLegacyStorageKeyAsync();
                var legacy = await js.InvokeAsync<string?>("localStorage.getItem", ct, legacyKey);
                if (Guid.TryParse(legacy, out var id)) _connection = await printing.MigrateConnectionAsync(id, ct);
                if (_connection is not null)
                    await js.InvokeVoidAsync("localStorage.setItem", ct, _storageKey, _connection.Proof);
            }
            var recent = _connection?.Installation.LastSeenAt is { } last
                && DateTimeOffset.UtcNow - last < TimeSpan.FromSeconds(60);
            var state = result.Status switch
            {
                "connected" => recent ? PrintAgentStatus.Connected : PrintAgentStatus.AwaitingApi,
                "origin-denied" => PrintAgentStatus.OriginDenied,
                "incompatible" => PrintAgentStatus.Incompatible,
                "other-application" => PrintAgentStatus.OtherApplication,
                "unavailable" => PrintAgentStatus.Unavailable,
                _ => PrintAgentStatus.Error
            };
            var available = options.Value.AvailableAgentVersion;
            // /info reports the Windows executable version, independently of the NuGet package version.
            var update = Version.TryParse(result.Version, out var installed)
                && Version.TryParse(available, out var released) && installed < released;
            _machineConfirmed = state is PrintAgentStatus.Connected or PrintAgentStatus.AwaitingApi;
            Status = state;
            InstalledVersion = result.Version;
            UpdateAvailable = update;
            LastErrorMessage = result.Error;
            return new(state, _connection, result.Version, available, update, result.Error);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _machineConfirmed = false;
            logger.LogError(ex, "Failed to initialize or recognize the local PrintAgent. Check backend configuration, SQLite dependencies and data directory permissions.");
            Status = ex is BackendInitializationException ? PrintAgentStatus.ServerUnavailable : PrintAgentStatus.Error;
            InstalledVersion = null;
            UpdateAvailable = false;
            LastErrorMessage = ex is BackendInitializationException ? ex.Message
                : "Could not recognize the agent. Try again and check the host log if the problem persists.";
            throw;
        }
        finally
        {
            UpdatePrinters();
            _gate.Release();
            Notify(OnStatusChanged);
            if (!ReferenceEquals(previousPrinters, PrintersInfo)) Notify(OnGetPrinters);
        }
    }

    internal async Task<MachineConnectionState> DownloadInstallerForAsync(Uri application, CancellationToken ct = default)
    {
        // Rediscover before downloading to reuse the existing installation on this computer.
        var result = await DiscoverAsync(application, ct);
        if (result.Status is PrintAgentStatus.OtherApplication or PrintAgentStatus.OriginDenied)
            throw new InvalidOperationException(result.Message);
        await _gate.WaitAsync(ct);
        try
        {
            if (_connection is null)
            {
                _connection = await printing.CreateConnectionAsync("Print workstation", ct);
                await js.InvokeVoidAsync("localStorage.setItem", ct, _storageKey, _connection.Proof);
            }
            await _browser!.InvokeVoidAsync("downloadInstaller", ct,
                printing.GetInstallerUrl(application, _connection.Installation.Id), _connection.Proof);
            return _connection;
        }
        finally { _gate.Release(); }
    }

    public async Task UnlinkAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_connection is not null) await printing.ForgetConnectionAsync(_connection.Installation.Id, ct);
            if (_storageKey is not null) await js.InvokeVoidAsync("localStorage.removeItem", ct, _storageKey);
            _connection = null;
            _machineConfirmed = false;
            Status = PrintAgentStatus.Unlinked;
            LastErrorMessage = null;
            InstalledVersion = null;
            UpdateAvailable = false;
        }
        finally
        {
            var previousPrinters = PrintersInfo;
            UpdatePrinters();
            _gate.Release();
            Notify(OnStatusChanged);
            if (!ReferenceEquals(previousPrinters, PrintersInfo)) Notify(OnGetPrinters);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null)
            try { await _browser.DisposeAsync(); } catch (JSDisconnectedException) { }
    }
}
