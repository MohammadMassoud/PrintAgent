using System.Drawing.Printing;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SinergiaAI.PrintAgent.Contracts;

namespace SinergiaAI.PrintAgent.Agent;

public sealed class AgentWorker(AgentOptions options, IHttpClientFactory clients, AgentJournal journal, WindowsRawReceiptPrinter printer, ILogger<AgentWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private Dictionary<string, string> _destinations = [];
    private DateTimeOffset _lastReport = DateTimeOffset.MinValue;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var http = clients.CreateClient("api");
        var failures = 0;
        bool? previousSupport = null;
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = TimeSpan.FromSeconds(Math.Clamp(options.PollSeconds, 1, 60));
            try
            {
                await FlushAsync(http, stoppingToken);
                if (DateTimeOffset.UtcNow - _lastReport > TimeSpan.FromSeconds(30)) await ReportAsync(http, stoppingToken);
                var waitSeconds = Math.Clamp(options.LongPollSeconds, 0, PrintProtocol.MaxLongPollSeconds);
                using var response = await http.PostAsync($"claim?waitSeconds={waitSeconds}", null, stoppingToken);
                response.EnsureSuccessStatusCode();
                var supported = response.Headers.TryGetValues(PrintProtocol.LongPollHeader, out var values)
                    && values.Contains(PrintProtocol.MaxLongPollSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (waitSeconds > 0 && previousSupport != supported)
                {
                    if (supported) logger.LogInformation("Long polling enabled: waiting up to {Seconds}s while the queue is empty.", waitSeconds);
                    else logger.LogWarning("The API does not support long polling. Falling back to a {Seconds}s interval; update the backend.", Math.Clamp(options.PollSeconds, 1, 60));
                    previousSupport = supported;
                }
                if (response.StatusCode != HttpStatusCode.NoContent)
                {
                    var job = await response.Content.ReadFromJsonAsync<ClaimedPrintJob>(Json, stoppingToken) ?? throw new InvalidOperationException("The API returned an empty print job.");
                    journal.Start(job); // Write before sending. Never automatically resend a started job.
                    try
                    {
                        if (!_destinations.TryGetValue(job.PrinterId, out var destination)) throw new InvalidOperationException("The printer was not found in this installation.");
                        await printer.PrintAsync(destination, Convert.FromBase64String(job.ContentBase64), stoppingToken);
                        journal.Finish(job.Id, PrintStatus.Sent, null);
                        logger.LogInformation("Print job {Id} sent.", job.Id);
                    }
                    catch (Exception ex)
                    {
                        var status = ex is OperationCanceledException ? PrintStatus.Unknown : PrintStatus.Failed;
                        journal.Finish(job.Id, status, ex.Message[..Math.Min(ex.Message.Length, 2000)]);
                        logger.LogError(ex, "Print job {Id} failed. Check the printer before requesting another copy.", job.Id);
                    }
                    await FlushAsync(http, stoppingToken);
                    failures = 0;
                    continue;
                }
                failures = 0;
                // A long-poll timeout is normal. Renew immediately; older APIs need the legacy delay.
                if (waitSeconds > 0 && supported) delay = TimeSpan.Zero;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex)
            {
                failures = Math.Min(failures + 1, 7);
                var seconds = Math.Min(60, Math.Clamp(options.PollSeconds, 1, 60) * Math.Pow(2, failures - 1));
                delay = TimeSpan.FromSeconds(Math.Clamp(seconds * (0.8 + Random.Shared.NextDouble() * 0.4), 1, 60));
                logger.LogWarning(ex, "Communication or processing failed. Attempt {Attempt}; reconnecting in {Seconds:N1}s.", failures, delay.TotalSeconds);
            }
            try { await Task.Delay(delay, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
    private async Task FlushAsync(HttpClient http, CancellationToken ct)
    {
        foreach (var entry in journal.Pending())
        {
            using var response = await http.PostAsJsonAsync($"jobs/{entry.JobId}/complete", new CompletePrintRequest(entry.ClaimId, entry.Status, entry.Error), Json, ct);
            response.EnsureSuccessStatusCode(); journal.Acknowledge(entry.JobId);
        }
    }
    private async Task ReportAsync(HttpClient http, CancellationToken ct)
    {
        var printers = new List<PrinterInfo>(); var destinations = new Dictionary<string, string>();
        var defaultName = new PrinterSettings().PrinterName;
        foreach (var name in PrinterSettings.InstalledPrinters.Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var id = "windows:" + name; printers.Add(new(id, name, string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase))); destinations[id] = name;
        }
        foreach (var network in options.NetworkPrinters)
        {
            if (!Uri.TryCreate(network.Address, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "tcp" || endpoint.Port is <= 0 or > 65535) throw new InvalidOperationException("Invalid network printer address. Use tcp://IP:9100.");
            var id = "tcp:" + network.Address; printers.Add(new(id, network.Name, false)); destinations[id] = network.Address;
        }
        using var response = await http.PostAsJsonAsync("printers", new ReportPrintersRequest(printers), Json, ct); response.EnsureSuccessStatusCode();
        _destinations = destinations; _lastReport = DateTimeOffset.UtcNow;
    }
}
