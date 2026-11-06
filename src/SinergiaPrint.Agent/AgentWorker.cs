using System.Drawing.Printing;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SinergiaPrint.Contracts;

namespace SinergiaPrint.Agent;

public sealed class AgentWorker(AgentOptions options, IHttpClientFactory clients, AgentJournal journal, WindowsRawReceiptPrinter printer, ILogger<AgentWorker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private Dictionary<string, string> _destinations = [];
    private DateTimeOffset _lastReport = DateTimeOffset.MinValue;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var http = clients.CreateClient("api");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await FlushAsync(http, stoppingToken);
                if (DateTimeOffset.UtcNow - _lastReport > TimeSpan.FromSeconds(30)) await ReportAsync(http, stoppingToken);
                using var response = await http.PostAsync("claim", null, stoppingToken);
                response.EnsureSuccessStatusCode();
                if (response.StatusCode != HttpStatusCode.NoContent)
                {
                    var job = await response.Content.ReadFromJsonAsync<ClaimedPrintJob>(Json, stoppingToken) ?? throw new InvalidOperationException("Trabalho vazio.");
                    journal.Start(job); // Write before sending. Never automatically resend a started job.
                    try
                    {
                        if (!_destinations.TryGetValue(job.PrinterId, out var destination)) throw new InvalidOperationException("Impressora nao encontrada nesta instalacao.");
                        await printer.PrintAsync(destination, Convert.FromBase64String(job.ContentBase64), stoppingToken);
                        journal.Finish(job.Id, PrintStatus.Sent, null);
                        logger.LogInformation("Trabalho {Id} enviado.", job.Id);
                    }
                    catch (Exception ex)
                    {
                        var status = ex is OperationCanceledException ? PrintStatus.Unknown : PrintStatus.Failed;
                        journal.Finish(job.Id, status, ex.Message[..Math.Min(ex.Message.Length, 2000)]);
                        logger.LogError(ex, "Falha ao enviar trabalho {Id}. Confira a impressao antes de reenviar.", job.Id);
                    }
                    await FlushAsync(http, stoppingToken);
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "Falha de comunicacao ou processamento. Nova tentativa de comunicacao no proximo ciclo."); }
            try { await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.PollSeconds, 1, 60)), stoppingToken); }
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
            if (!Uri.TryCreate(network.Address, UriKind.Absolute, out var endpoint) || endpoint.Scheme != "tcp" || endpoint.Port is <= 0 or > 65535) throw new InvalidOperationException("Endereco de impressora de rede invalido. Use tcp://IP:9100.");
            var id = "tcp:" + network.Address; printers.Add(new(id, network.Name, false)); destinations[id] = network.Address;
        }
        using var response = await http.PostAsJsonAsync("printers", new ReportPrintersRequest(printers), Json, ct); response.EnsureSuccessStatusCode();
        _destinations = destinations; _lastReport = DateTimeOffset.UtcNow;
    }
}
