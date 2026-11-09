using System.Text;
using Microsoft.Extensions.DependencyInjection;
using SinergiaAI.PrintAgent.Client;

var url = Environment.GetEnvironmentVariable("PRINT_API_URL") ?? "http://localhost:5189/";
var key = Environment.GetEnvironmentVariable("PRINT_API_KEY") ?? throw new InvalidOperationException("Set PRINT_API_KEY in the backend environment.");
var services = new ServiceCollection(); services.AddSinergiaAIPrintAgentClient(new Uri(url), key);
using var provider = services.BuildServiceProvider();
var printing = provider.GetRequiredService<PrintingClient>();
var installations = await printing.ListInstallationsAsync();
foreach (var installation in installations)
{
    Console.WriteLine($"{installation.Id}: {installation.Name}");
    foreach (var printer in await printing.ListPrintersAsync(installation.Id)) Console.WriteLine($"  {printer.Id}: {printer.Name}");
}
// Sending is explicit. Never print to the first printer automatically.
if (args.Length == 3 && args[0] == "--send")
{
    var installation = Guid.Parse(args[1]); var printerId = args[2];
    var zpl = Encoding.ASCII.GetBytes("^XA^FO50,50^ADN,36,20^FDPrintAgent test^FS^XZ");
    var job = await printing.PrintAsync(installation, printerId, zpl, Guid.NewGuid().ToString());
    Console.WriteLine($"Job {job.Id}: {job.Status}");
    for (var attempt = 0; attempt < 30; attempt++)
    {
        await Task.Delay(1000); job = await printing.GetJobAsync(job.Id);
        if (job.Status is not (SinergiaAI.PrintAgent.Contracts.PrintStatus.Pending or SinergiaAI.PrintAgent.Contracts.PrintStatus.Claimed)) { Console.WriteLine($"Result: {job.Status} {job.Error}"); return; }
    }
    Console.WriteLine("The job is still pending. Query the same ID; do not resubmit it automatically.");
}
