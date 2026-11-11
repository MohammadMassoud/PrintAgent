using System.Security.Principal;
using System.Text.Json;
using SinergiaAI.PrintAgent.Agent;

// Read-only regression probe: callbacks never open a printer or send bytes.
var printers = WindowsPrinterDiscovery.List();
Require(printers.Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == printers.Count, "Unique installed queues");
Require(printers.Count(p => p.IsDefault) <= 1, "At most one default queue");
foreach (var printer in printers)
{
    var calls = 0;
    WindowsPrinterDiscovery.WithPrinterIdentity(printer.Name, () => calls++);
    Require(calls == 1, "Available queue resolves to one delivery context");
    var expected = new IOException("Simulated failure after delivery begins");
    calls = 0;
    try
    {
        WindowsPrinterDiscovery.WithPrinterIdentity(printer.Name, () => { calls++; throw expected; });
        throw new Exception("Expected the simulated delivery failure");
    }
    catch (IOException ex) when (ReferenceEquals(ex, expected)) { }
    Require(calls == 1, "Delivery failure never retries in another identity");
}
var missingCalls = 0;
try
{
    WindowsPrinterDiscovery.WithPrinterIdentity("missing-" + Guid.NewGuid(), () => missingCalls++);
    throw new Exception("Expected unavailable queue rejection");
}
catch (InvalidOperationException) { }
Require(missingCalls == 0, "Missing queue fails before delivery");
using var identity = WindowsIdentity.GetCurrent();
Console.WriteLine(JsonSerializer.Serialize(new { Identity = identity.Name, identity.IsSystem, Printers = printers }));
Console.WriteLine("PASS read-only discovery and single-attempt delivery context checks; no bytes sent");

static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
