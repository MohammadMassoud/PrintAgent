using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using System.ComponentModel;

namespace SinergiaAI.PrintAgent;

/// <summary>Bridges a Blazor Server application to Windows queues and TCP printers through the user's browser.</summary>
/// <remarks>SendAsync accepts text, bytes or lists. All overloads use the same durable local RAW queue.</remarks>
/// <param name="services">The application service provider supplied by dependency injection.</param>
/// <param name="js">The interactive component's JavaScript runtime.</param>
/// <param name="options">Bridge, installer and text-encoding configuration.</param>
/// <param name="navigation">The component's base URI provider.</param>
/// <param name="logger">The host logger for technical errors and safe reference identifiers.</param>
public sealed partial class PrintAgent(IServiceProvider services, IJSRuntime js, IOptions<PrintAgentOptions> options, NavigationManager navigation, ILogger<PrintAgent> logger) : IAsyncDisposable
{
    // Retained for the legacy error classifier; the direct transport never initializes a backend.
    private sealed class BackendInitializationException(Exception inner) : Exception("Legacy printing backend initialization failed.", inner);
    private IJSObjectReference? _browser;
    private DotNetObjectReference<PrintAgent>? _callback;
    private readonly SemaphoreSlim _gate = new(1,1);
    /// <summary>Gets printer display names. Use PrintersInfo to obtain destination identifiers for sending.</summary>
    public IReadOnlyList<string> Printers { get; private set; } = Array.Empty<string>();
    /// <summary>Gets discovered Windows queues and configured TCP destinations on the connected workstation.</summary>
    public IReadOnlyList<PrintPrinter> PrintersInfo { get; private set; } = Array.Empty<PrintPrinter>();
    /// <summary>Gets the current browser-to-agent connection state.</summary>
    public PrintAgentStatus Status { get; private set; } = PrintAgentStatus.NotStarted;
    /// <summary>Gets whether the browser has an authorized connection to the local agent.</summary>
    public bool IsConnected => Status == PrintAgentStatus.Connected;
    /// <summary>Gets the legacy alias of IsConnected.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool MachineConfirmed => IsConnected;
    /// <summary>Gets the connected workstation's persistent identifier, or null while disconnected.</summary>
    public Guid? DeviceId { get; private set; }
    /// <summary>Gets the connected Windows computer name.</summary>
    public string? MachineName { get; private set; }
    /// <summary>Gets the agent version reported by the most recent connection attempt.</summary>
    public string? InstalledVersion { get; private set; }
    /// <summary>Gets the Windows agent version offered by this integration.</summary>
    public string AvailableVersion => options.Value.AvailableAgentVersion;
    /// <summary>Gets whether the reported installed version is older than the offered version.</summary>
    public bool UpdateAvailable => Version.TryParse(InstalledVersion,out var installed) && Version.TryParse(AvailableVersion,out var available) && installed < available;
    /// <summary>Gets the legacy browser storage key. This is not a credential or an idempotency key.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public string BrowserStorageKey => options.Value.BrowserStoragePrefix + "machine.v2";
    /// <summary>Occurs when connection status changes. Marshal UI updates through the component's InvokeAsync.</summary>
    public event Action? OnStatusChanged;
    /// <summary>Occurs when the discovered printer list changes after connection or refresh.</summary>
    public event Action? OnGetPrinters;
    /// <summary>Occurs when a job update arrives. Query GetJobsAsync after reconnecting to recover missed updates.</summary>
    public event Action<PrintJob>? OnJobChanged;
    private bool _disposed;
    private sealed record BridgeInfo(int Protocol, string? Version, Guid? MachineId, string? MachineName, PrintPrinter[]? Printers, string? Error);
    private void Notify(Action? subscribers)
    {
        if (_disposed || subscribers is null) return;
        foreach (Action subscriber in subscribers.GetInvocationList())
            try { subscriber(); } catch (Exception ex) { logger.LogWarning(ex,"Print connection observer failed."); }
    }
    private async Task PrepareAsync(CancellationToken ct)
    {
        _ = services;
        _callback ??= DotNetObjectReference.Create(this);
        _browser ??= await js.InvokeAsync<IJSObjectReference>("import",ct,new Uri(new Uri(navigation.BaseUri),options.Value.ApiPrefix.Trim('/') + "/browser.js?v=11").AbsoluteUri);
    }
    /// <summary>Connects to the local agent, requests website approval when needed and loads the printer list.</summary>
    /// <param name="ct">Cancels connection or approval waiting.</param>
    /// <returns>A task completing after the connection attempt. Check IsConnected and LastError.</returns>
    /// <remarks>Call after the first interactive render. Keep the Windows tray application running for native approval.</remarks>
    public async Task StartAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            await PrepareAsync(ct);
            var info = await _browser!.InvokeAsync<BridgeInfo>("connect",ct,new { port = options.Value.LocalBridgePort, automaticAuthorization = options.Value.AutomaticAuthorization },_callback);
            InstalledVersion = info.Version;
            if (info.Error is not null)
            {
                SetDisconnected(info.Error == PrintAgentErrorCodes.OriginDenied ? PrintAgentStatus.OriginDenied : info.Error == PrintAgentErrorCodes.AgentUpdateRequired ? PrintAgentStatus.Incompatible : PrintAgentStatus.Unavailable);
                RecordError(SafeCode(info.Error)); return;
            }
            if (info.Protocol != 2 || info.MachineId is null) throw new PrintAgentOperationException(PrintAgentErrorCodes.ProtocolIncompatible,"Update the Windows agent.");
            DeviceId = info.MachineId; MachineName = info.MachineName; SetPrinters(info.Printers ?? []);
            Status = PrintAgentStatus.Connected; ClearError();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { SetDisconnected(PrintAgentStatus.Error); RecordTransportError(ex); throw; }
        finally { _gate.Release(); Notify(OnStatusChanged); }
    }
    /// <summary>Refreshes installed Windows queues and configured TCP destinations from the connected workstation.</summary>
    /// <param name="ct">Cancels the request or its wait.</param>
    /// <returns>A task completing after Printers and PrintersInfo have been updated.</returns>
    public async Task RefreshPrintersAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { var info = await RequestAsync<BridgeInfo>("printers",new { },ct); SetPrinters(info.Printers ?? []); ClearError(); }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { RecordTransportError(ex); throw; }
        finally { _gate.Release(); }
    }
    /// <summary>Atomically accepts 1 to 5,000 printer-ready documents into the workstation's durable queue.</summary>
    /// <param name="documents">Text documents created with CreateTextDocument and/or binary PrintDocument instances. Maximum 2 MiB each and 4 MiB total.</param>
    /// <param name="ct">Cancels submission waiting; accepted jobs may continue printing.</param>
    /// <returns>Accepted or previously accepted jobs, in the supplied document order.</returns>
    /// <remarks>Persist each document's destination, exact bytes and key before sending. A timeout does not prove rejection; reuse the original keys and bytes.</remarks>
    public async Task<IReadOnlyList<PrintJob>> SendAsync(IReadOnlyList<PrintDocument> documents,CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (documents is null || documents.Count is < 1 or > 5000 || documents.Any(d => d is null || string.IsNullOrWhiteSpace(d.PrinterId) || d.Content is null || d.Content.Length is < 1 or > 2 * 1024 * 1024 || string.IsNullOrWhiteSpace(d.IdempotencyKey) || d.IdempotencyKey.Length > 200) || documents.Sum(d => (long)d.Content.Length) > 4 * 1024 * 1024)
                throw new PrintAgentOperationException(PrintAgentErrorCodes.RequestInvalid,"Submit 1–5000 documents, at most 2 MiB each and 4 MiB per batch, with stable request keys.");
            var jobs = await RequestAsync<PrintJob[]>("enqueue",new { documents = documents.Select(d => new { d.PrinterId, contentBase64 = Convert.ToBase64String(d.Content), d.IdempotencyKey }) },ct);
            ClearError(); return jobs;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { RecordTransportError(ex); throw; }
        finally { _gate.Release(); }
    }
    /// <summary>Queries persisted job results for this website on the connected workstation.</summary>
    /// <param name="jobIds">Identifiers returned when jobs were accepted.</param>
    /// <param name="ct">Cancels the query or its wait.</param>
    /// <returns>The known jobs visible to this website origin.</returns>
    public async Task<IReadOnlyList<PrintJob>> GetJobsAsync(IReadOnlyCollection<Guid> jobIds,CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { var jobs = await RequestAsync<PrintJob[]>("jobs",new { jobIds },ct); ClearError(); return jobs; }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { RecordTransportError(ex); throw; }
        finally { _gate.Release(); }
    }
    private async Task<T> RequestAsync<T>(string method,object data,CancellationToken ct)
    {
        if (!IsConnected || _browser is null) throw new PrintAgentOperationException(PrintAgentErrorCodes.AgentUnreachable,"Connect to the local agent first.");
        return await _browser.InvokeAsync<T>("request",ct,method,data);
    }
    /// <summary>Opens the Windows authorization dialog for this website. Call from a user action.</summary>
    /// <param name="ct">Cancels the browser request.</param>
    /// <returns>A task completing after requesting the native dialog.</returns>
    public async Task AuthorizeAsync(CancellationToken ct = default)
    { await PrepareAsync(ct); await _browser!.InvokeVoidAsync("authorize",ct,options.Value.LocalBridgePort,false); }
    /// <summary>Opens the Windows dialog to revoke this website's machine-wide access.</summary>
    /// <param name="ct">Cancels the browser request.</param>
    /// <returns>A task completing after requesting the native dialog. Already accepted jobs are preserved.</returns>
    public async Task RevokeAuthorizationAsync(CancellationToken ct = default)
    { await PrepareAsync(ct); await _browser!.InvokeVoidAsync("authorize",ct,options.Value.LocalBridgePort,true); }
    /// <summary>Starts the browser download of the configured generic Windows installer.</summary>
    /// <param name="ct">Cancels the browser request.</param>
    /// <returns>A task completing after the download has been requested; it does not install the agent.</returns>
    public async Task DownloadInstallerAsync(CancellationToken ct = default)
    { await PrepareAsync(ct); await _browser!.InvokeVoidAsync("downloadInstaller",ct,options.Value.InstallerUrl); }
    /// <summary>Disconnects this component without revoking website authorization or cancelling accepted jobs.</summary>
    /// <param name="ct">Cancels the browser request.</param>
    /// <returns>A task completing after disconnection.</returns>
    public async Task UnlinkAsync(CancellationToken ct = default)
    {
        if (_browser is not null) await _browser.InvokeVoidAsync("disconnect",ct);
        SetDisconnected(PrintAgentStatus.Unlinked); ClearError(); Notify(OnStatusChanged);
    }
    /// <summary>Receives internal browser job notifications. Applications should subscribe to OnJobChanged.</summary>
    /// <param name="job">The job update received from the browser bridge.</param>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [JSInvokable] public void JobChanged(PrintJob job)
    {
        if (_disposed || !IsConnected || job.InstallationId != DeviceId || OnJobChanged is null) return;
        foreach (Action<PrintJob> subscriber in OnJobChanged.GetInvocationList())
            try { subscriber(job); } catch (Exception ex) { logger.LogWarning(ex,"Print job observer failed."); }
    }
    /// <summary>Receives internal browser disconnection notifications.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    [JSInvokable] public void BridgeClosed()
    { if (_disposed || Status == PrintAgentStatus.OriginDenied) return; SetDisconnected(PrintAgentStatus.Unavailable); RecordError(PrintAgentErrorCodes.AgentUnreachable); Notify(OnStatusChanged); }
    private void SetDisconnected(PrintAgentStatus status) { Status = status; DeviceId = null; MachineName = null; SetPrinters([]); }
    private void SetPrinters(PrintPrinter[] printers)
    {
        if (PrintersInfo.SequenceEqual(printers)) return;
        PrintersInfo = Array.AsReadOnly(printers); Printers = Array.AsReadOnly(printers.Select(p => p.Name).ToArray()); Notify(OnGetPrinters);
    }
    private static string SafeCode(string code) => code switch
    {
        PrintAgentErrorCodes.OriginDenied or PrintAgentErrorCodes.AgentUpdateRequired or PrintAgentErrorCodes.ProtocolIncompatible
            or PrintAgentErrorCodes.AgentUnreachable or PrintAgentErrorCodes.RequestInvalid or PrintAgentErrorCodes.OperationTimedOut
            or PrintAgentErrorCodes.LocalStorageUnavailable => code,
        _ => PrintAgentErrorCodes.UnexpectedError
    };
    private void RecordTransportError(Exception ex)
    {
        if (ex is PrintAgentOperationException operation) { RecordError(SafeCode(operation.Code)); return; }
        if (ex is JSException)
        {
            var code = new[] { PrintAgentErrorCodes.OriginDenied, PrintAgentErrorCodes.AgentUnreachable, PrintAgentErrorCodes.RequestInvalid, PrintAgentErrorCodes.OperationTimedOut, PrintAgentErrorCodes.LocalStorageUnavailable }.FirstOrDefault(c => ex.Message.Split('\n')[0].Trim().EndsWith(c,StringComparison.Ordinal));
            if (code is not null) { if (code is PrintAgentErrorCodes.OriginDenied or PrintAgentErrorCodes.AgentUnreachable) SetDisconnected(code == PrintAgentErrorCodes.OriginDenied ? PrintAgentStatus.OriginDenied : PrintAgentStatus.Unavailable); RecordError(code); return; }
        }
        RecordError(ex);
    }
    /// <summary>Releases browser interop references and closes this component's connection. Dependency injection normally owns disposal.</summary>
    /// <returns>A task completing after resources have been released.</returns>
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_browser is not null)
            try { await _browser.InvokeVoidAsync("disconnect"); await _browser.DisposeAsync(); } catch (JSDisconnectedException) { } catch (TaskCanceledException) { }
        _callback?.Dispose();
    }
}
