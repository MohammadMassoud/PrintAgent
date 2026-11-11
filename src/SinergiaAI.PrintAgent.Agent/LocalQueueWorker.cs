namespace SinergiaAI.PrintAgent.Agent;

public sealed class LocalPrinters(AgentOptions options)
{
    public IReadOnlyDictionary<string,string> Destinations()
        => Destinations(options.OnlyNetworkPrinters ? [] : WindowsPrinterDiscovery.List());
    private IReadOnlyDictionary<string,string> Destinations(IReadOnlyList<WindowsPrinterDiscovery.Printer> installed)
    {
        var items = new Dictionary<string,string>(StringComparer.Ordinal);
        foreach (var printer in installed) items["windows:" + printer.Name] = printer.Name;
        foreach (var p in options.NetworkPrinters)
        {
            if (!Uri.TryCreate(p.Address, UriKind.Absolute, out var u) || u.Scheme != "tcp" || u.Port is < 1 or > 65535) throw new InvalidOperationException("Invalid configured TCP printer.");
            items["tcp:" + p.Address] = p.Address;
        }
        return items;
    }
    public object[] List()
    {
        var installed = options.OnlyNetworkPrinters ? [] : WindowsPrinterDiscovery.List();
        var defaultName = installed.FirstOrDefault(p => p.IsDefault)?.Name;
        return Destinations(installed).Select(p => (object)new { id = p.Key, name = p.Key.StartsWith("windows:") ? p.Value : options.NetworkPrinters.First(n => n.Address == p.Value).Name, isDefault = string.Equals(p.Value, defaultName, StringComparison.OrdinalIgnoreCase) }).ToArray();
    }
}

public sealed class LocalQueueWorker(LocalPrintQueue queue, LocalPrinters printers, WindowsRawReceiptPrinter printer, ILogger<LocalQueueWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var job = queue.BeginDelivery();
            if (job is null) { await queue.Wake.WaitAsync(stoppingToken); continue; }
            // The intent is durable before any bytes can leave this process.
            try
            {
                if (!printers.Destinations().TryGetValue(job.PrinterId, out var destination))
                { queue.Finish(job.Id, 3, "The selected printer is no longer installed or configured."); continue; }
                await printer.PrintAsync(destination, job.Content, stoppingToken);
                queue.Finish(job.Id, 2, null);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Local job {JobId} could not complete. It will not be automatically resent.", job.Id);
                queue.Finish(job.Id, 4, "Delivery could not be confirmed. Check the printer before requesting another copy.");
            }
        }
    }
}
