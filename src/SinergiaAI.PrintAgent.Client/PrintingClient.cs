using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using SinergiaAI.PrintAgent.Contracts;

namespace SinergiaAI.PrintAgent.Client;

public sealed class PrintingClient(HttpClient http)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        using (response)
        {
            if (!response.IsSuccessStatusCode) throw new HttpRequestException($"API de impressao retornou {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(ct)}", null, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<T>(Json, ct) ?? throw new InvalidOperationException("Resposta vazia da API de impressao.");
        }
    }
    public async Task<List<InstallationInfo>> ListInstallationsAsync(CancellationToken ct = default) => await ReadAsync<List<InstallationInfo>>(await http.GetAsync("api/installations", ct), ct);
    public async Task<List<PrinterInfo>> ListPrintersAsync(Guid installationId, CancellationToken ct = default) => await ReadAsync<List<PrinterInfo>>(await http.GetAsync($"api/installations/{installationId}/printers", ct), ct);
    public async Task<PrintJobInfo> PrintAsync(Guid installationId, string printerId, ReadOnlyMemory<byte> content, string idempotencyKey, CancellationToken ct = default) => await ReadAsync<PrintJobInfo>(await http.PostAsJsonAsync("api/jobs", new SubmitPrintRequest(installationId, printerId, Convert.ToBase64String(content.Span), idempotencyKey), Json, ct), ct);
    public async Task<PrintJobInfo> GetJobAsync(Guid jobId, CancellationToken ct = default) => await ReadAsync<PrintJobInfo>(await http.GetAsync($"api/jobs/{jobId}", ct), ct);
}
public static class PrintingRegistration
{
    // Register in the server backend. Never expose the API key in WebAssembly/browser code.
    public static IServiceCollection AddSinergiaAIPrintAgentClient(this IServiceCollection services, Uri baseAddress, string apiKey)
    {
        services.AddHttpClient<PrintingClient>(http => { http.BaseAddress = new Uri(baseAddress.AbsoluteUri.TrimEnd('/') + "/"); http.DefaultRequestHeaders.Add("X-Api-Key", apiKey); http.Timeout = TimeSpan.FromSeconds(30); });
        return services;
    }
}
