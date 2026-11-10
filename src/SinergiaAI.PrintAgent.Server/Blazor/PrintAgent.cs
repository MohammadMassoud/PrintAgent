using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace SinergiaAI.PrintAgent;

/// <summary>Direct browser-to-agent printing. The host does not store print jobs or credentials.</summary>
public sealed partial class PrintAgent(IServiceProvider services, IJSRuntime js, IOptions<PrintAgentOptions> options, NavigationManager navigation, ILogger<PrintAgent> logger) : IAsyncDisposable
{
    // Retained for the legacy error classifier; the direct transport never initializes a backend.
    private sealed class BackendInitializationException(Exception inner) : Exception("Legacy printing backend initialization failed.", inner);
    private IJSObjectReference? _browser;
    private DotNetObjectReference<PrintAgent>? _callback;
    private readonly SemaphoreSlim _gate = new(1,1);
    public IReadOnlyList<string> Printers { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<PrintPrinter> PrintersInfo { get; private set; } = Array.Empty<PrintPrinter>();
    public PrintAgentStatus Status { get; private set; } = PrintAgentStatus.NotStarted;
    public bool IsConnected => Status == PrintAgentStatus.Connected;
    public bool MachineConfirmed => IsConnected;
    public Guid? DeviceId { get; private set; }
    public string? MachineName { get; private set; }
    public string? InstalledVersion { get; private set; }
    public string AvailableVersion => options.Value.AvailableAgentVersion;
    public bool UpdateAvailable => Version.TryParse(InstalledVersion,out var installed) && Version.TryParse(AvailableVersion,out var available) && installed < available;
    public string BrowserStorageKey => options.Value.BrowserStoragePrefix + "machine.v2";
    public event Action? OnStatusChanged;
    public event Action? OnGetPrinters;
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
    public async Task RefreshPrintersAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try { var info = await RequestAsync<BridgeInfo>("printers",new { },ct); SetPrinters(info.Printers ?? []); ClearError(); }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { RecordTransportError(ex); throw; }
        finally { _gate.Release(); }
    }
    public async Task<PrintJob> SendAsync(string printerId,byte[] content,string idempotencyKey,CancellationToken ct = default) => (await SendBatchAsync(new[] {new PrintDocument(printerId,content,idempotencyKey)},ct))[0];
    public async Task<IReadOnlyList<PrintJob>> SendBatchAsync(IReadOnlyList<PrintDocument> documents,CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (documents.Count is < 1 or > 5000 || documents.Any(d => d.Content is null || d.Content.Length is < 1 or > 2 * 1024 * 1024 || string.IsNullOrWhiteSpace(d.IdempotencyKey) || d.IdempotencyKey.Length > 200) || documents.Sum(d => (long)d.Content.Length) > 4 * 1024 * 1024)
                throw new PrintAgentOperationException(PrintAgentErrorCodes.RequestInvalid,"Submit 1–5000 documents, at most 2 MiB each and 4 MiB per batch, with stable request keys.");
            var jobs = await RequestAsync<PrintJob[]>("enqueue",new { documents = documents.Select(d => new { d.PrinterId, contentBase64 = Convert.ToBase64String(d.Content), d.IdempotencyKey }) },ct);
            ClearError(); return jobs;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested) { RecordTransportError(ex); throw; }
        finally { _gate.Release(); }
    }
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
    public async Task AuthorizeAsync(CancellationToken ct = default)
    { await PrepareAsync(ct); await _browser!.InvokeVoidAsync("authorize",ct,options.Value.LocalBridgePort,false); }
    /// <summary>Opens the Windows dialog to revoke this website's machine-wide access.</summary>
    public async Task RevokeAuthorizationAsync(CancellationToken ct = default)
    { await PrepareAsync(ct); await _browser!.InvokeVoidAsync("authorize",ct,options.Value.LocalBridgePort,true); }
    public async Task DownloadInstallerAsync(CancellationToken ct = default)
    { await PrepareAsync(ct); await _browser!.InvokeVoidAsync("downloadInstaller",ct,options.Value.InstallerUrl); }
    public async Task UnlinkAsync(CancellationToken ct = default)
    {
        if (_browser is not null) await _browser.InvokeVoidAsync("disconnect",ct);
        SetDisconnected(PrintAgentStatus.Unlinked); ClearError(); Notify(OnStatusChanged);
    }
    [JSInvokable] public void JobChanged(PrintJob job)
    {
        if (_disposed || !IsConnected || job.InstallationId != DeviceId || OnJobChanged is null) return;
        foreach (Action<PrintJob> subscriber in OnJobChanged.GetInvocationList())
            try { subscriber(job); } catch (Exception ex) { logger.LogWarning(ex,"Print job observer failed."); }
    }
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
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_browser is not null)
            try { await _browser.InvokeVoidAsync("disconnect"); await _browser.DisposeAsync(); } catch (JSDisconnectedException) { } catch (TaskCanceledException) { }
        _callback?.Dispose();
    }
}
