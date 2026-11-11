using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using SinergiaAI.PrintAgent;

namespace PrintAgentDemo;

public partial class Home : IAsyncDisposable
{
    [Inject] private IJSRuntime JS { get; set; } = default!;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _loading = true, _busy, _refreshing, _disposed;
    private string _format = "ZPL", _content = "^XA\n^CI28\n^FO40,40^A0N,35,35^FDPrintAgent sample^FS\n^FO40,100^BY2^BCN,80,Y,N,N^FD123456789^FS\n^XZ";
    private string? _printerId, _error, _message;
    private string ConnectionHeading => _loading ? "Connecting…" : PrintAgent.IsConnected ? "Agent connected"
        : PrintAgent.Status == PrintAgentStatus.ServerUnavailable ? "Printing backend unavailable"
        : PrintAgent.LastErrorCode == PrintAgentErrorCodes.AuthenticationRequired ? "Sign-in required"
        : PrintAgent.Status == PrintAgentStatus.Unlinked ? "Browser link removed" : "Agent not connected";
    private int _quantity = 1;
    private Guid? _restoredDevice;
    private RequestPlan? _request;
    private List<PrintJob> _jobs = [];
    private IEnumerable<PrintPrinter> AvailablePrinters => PrintAgent.PrintersInfo.Where(p => !Mode.Simulated
        || p.Name == "Simulated TCP printer" && p.Id.StartsWith("tcp:tcp://127.0.0.1:", StringComparison.Ordinal));
    private string StorageKey => "PrintAgent.Demo.Direct.Request." + PrintAgent.DeviceId;
    private bool CanCreateRequest => _request is null || _jobs.Count == _request.Quantity && _jobs.All(IsFinal);
    private static bool IsFinal(PrintJob job) => job.Status is PrintJobStatus.Sent or PrintJobStatus.Failed or PrintJobStatus.Unknown;
    private sealed record RequestPlan(Guid Id, string PrinterId, string ContentBase64, int Quantity, List<Guid> Jobs);

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        PrintAgent.OnStatusChanged += ConnectionChanged;
        PrintAgent.OnJobChanged += JobChanged;
        Capture.Changed += ConnectionChanged;
        await ConnectAsync();
        _loading = false;
        StateHasChanged();
    }

    private async Task ConnectAsync()
    {
        if (_refreshing || _busy) return;
        _refreshing = true;
        try
        {
            await PrintAgent.StartAsync(_lifetime.Token);
            await RestoreAsync();
            await RefreshJobsAsync();
            _error = null;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { _error = PrintAgent.LastErrorMessage ?? ex.Message; }
        finally { _refreshing = false; }
    }

    private void ConnectionChanged() { if (!_disposed) _ = InvokeAsync(StateHasChanged); }
    private void JobChanged(PrintJob job)
    {
        if (_disposed) return;
        _ = InvokeAsync(() =>
        {
            var index = _jobs.FindIndex(j => j.Id == job.Id);
            if (index >= 0) _jobs[index] = job;
            StateHasChanged();
        });
    }

    private async Task RestoreAsync()
    {
        if (PrintAgent.DeviceId is null || _restoredDevice == PrintAgent.DeviceId) return;
        _request = null; _jobs.Clear();
        var saved = await JS.InvokeAsync<string?>("localStorage.getItem", _lifetime.Token, StorageKey);
        if (saved is not null)
        {
            _request = JsonSerializer.Deserialize<RequestPlan>(saved);
            if (_request is not null)
            {
                _printerId = _request.PrinterId;
                _quantity = _request.Quantity;
                _content = Encoding.UTF8.GetString(Convert.FromBase64String(_request.ContentBase64));
                _message = "The previous request was restored. Retry it with the same keys if its submission was interrupted.";
            }
        }
        _restoredDevice = PrintAgent.DeviceId;
    }

    private async Task SaveAsync() => await JS.InvokeVoidAsync("localStorage.setItem", _lifetime.Token, StorageKey, JsonSerializer.Serialize(_request));

    private async Task RefreshJobsAsync()
    {
        if (_request?.Jobs.Count > 0) _jobs = (await PrintAgent.GetJobsAsync(_request.Jobs, _lifetime.Token)).ToList();
    }

    private async Task SendAsync()
    {
        if (_busy || !PrintAgent.IsConnected) return;
        _busy = true; _error = null;
        try
        {
            var destination = _request?.PrinterId ?? _printerId;
            if (!AvailablePrinters.Any(p => p.Id == destination))
                throw new InvalidOperationException("Select an available printer for this demo mode.");
            if (_request is null)
            {
                if (_quantity is < 1 or > 100 || string.IsNullOrWhiteSpace(_printerId) || string.IsNullOrWhiteSpace(_content))
                    throw new InvalidOperationException("Select a printer, enter content and choose a quantity between 1 and 100.");
                var bytes = PrintAgent.CreateTextDocument(_printerId, _content, "demo:prepare", Encoding.UTF8).Content;
                if (bytes.Length > 8192) throw new InvalidOperationException("The demo accepts payloads up to 8 KiB.");
                _request = new(Guid.NewGuid(), _printerId, Convert.ToBase64String(bytes), _quantity, []);
            }
            await SaveAsync(); // Persist immutable content, destination and keys before submitting.
            var documents = Enumerable.Range(0, _request.Quantity).Select(i => new PrintDocument(
                _request.PrinterId, Convert.FromBase64String(_request.ContentBase64), $"demo:{_request.Id:N}:{i}")).ToArray();
            // Single text sends demonstrate SendAsync; stored bytes remain authoritative for reconciliation.
            _jobs = documents.Length == 1
                ? [await PrintAgent.SendAsync(documents[0].PrinterId, new UTF8Encoding(false, true).GetString(documents[0].Content), documents[0].IdempotencyKey, Encoding.UTF8, _lifetime.Token)]
                : (await PrintAgent.SendAsync(documents, _lifetime.Token)).ToList();
            _request = _request with { Jobs = _jobs.Select(j => j.Id).ToList() };
            await SaveAsync();
            await RefreshJobsAsync();
            _message = $"{_jobs.Count} job(s) queued. Retrying this request reuses its original keys.";
        }
        catch (Exception ex) { _error = PrintAgent.LastErrorMessage ?? ex.Message; }
        finally { _busy = false; }
    }

    private async Task NewRequestAsync()
    {
        if (_busy || !CanCreateRequest) return;
        if (_jobs.Any(j => j.Status is PrintJobStatus.Failed or PrintJobStatus.Unknown)
            && !await JS.InvokeAsync<bool>("confirm", "Check the printer before creating another copy. Continue?")) return;
        if (PrintAgent.DeviceId is not null) await JS.InvokeVoidAsync("localStorage.removeItem", StorageKey);
        _request = null; _jobs.Clear(); _error = _message = null;
    }

    private void SelectFormat() => _content = _format == "ZPL"
        ? "^XA\n^FO40,40^A0N,35,35^FDPrintAgent sample^FS\n^FO40,100^BY2^BCN,80,Y,N,N^FD123456789^FS\n^XZ"
        : "\u001b@PrintAgent sample\nRAW receipt test\n\n\n\u001dVB\u0000";

    private async Task InstallAsync()
    {
        if (_busy) return;
        _busy = true; _error = null;
        try { await PrintAgent.DownloadInstallerAsync(_lifetime.Token); _message = "Run the downloaded installer, approve the Windows prompt and refresh the connection."; }
        catch (Exception ex) { _error = PrintAgent.LastErrorMessage ?? ex.Message; }
        finally { _busy = false; }
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await _lifetime.CancelAsync();
        PrintAgent.OnStatusChanged -= ConnectionChanged;
        PrintAgent.OnJobChanged -= JobChanged;
        Capture.Changed -= ConnectionChanged;
        _lifetime.Dispose();
    }
}
