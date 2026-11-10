using System.Globalization;
using System.Reflection;
using SinergiaAI.PrintAgent.Agent;
var language = Assembly.Load("SinergiaAI.PrintAgent.Approval").GetType("SinergiaAI.PrintAgent.Approval.UiText",true)!;
var method = language.GetMethod("Get",BindingFlags.Static|BindingFlags.NonPublic)!;
var original = CultureInfo.CurrentUICulture;
foreach (var culture in new[] { "en-US", "en-GB", "pt-BR", "pt-PT", "de-DE" })
{
    CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
    var value = (string)method.Invoke(null,new object[] { "English", "Português" })!;
    if (value != (culture.StartsWith("en",StringComparison.Ordinal) ? "English" : "Português")) throw new Exception("Wrong Windows UI language: " + culture);
}
CultureInfo.CurrentUICulture = original;
var session = Assembly.Load("SinergiaAI.PrintAgent.Approval").GetType("SinergiaAI.PrintAgent.Approval.DesktopSession",true)!;
_ = (bool)session.GetProperty("IsActive",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
Console.WriteLine("PASS native Windows session query. Display culture: " + original.Name);
var requests = new AuthorizationRequests();
if (requests.Ask("https://example.test") is not null) throw new Exception("Expected pending");
var claim = requests.Claim() ?? throw new Exception("Expected native request");
if (requests.Claim() is not null) throw new Exception("A request must not open two simultaneous dialogs");
if (requests.Resolve(Guid.NewGuid(),claim.Origin,true)) throw new Exception("Stale ID accepted");
if (!requests.Resolve(claim.Id,claim.Origin,false) || requests.Ask(claim.Origin) != false) throw new Exception("Denial not retained");
Console.WriteLine("PASS Windows display language: English for en, Portuguese otherwise. Native requests are claimed once, reject stale IDs and retain denial.");

// Exercise the actual desktop client, including its async stream disposal order.
var desktopPipe = Assembly.Load("SinergiaAI.PrintAgent.Approval").GetType("SinergiaAI.PrintAgent.Approval.DesktopPipe",true)!;
var call = desktopPipe.GetMethod("CallAsync",BindingFlags.Static|BindingFlags.Public)!;
var fixturePort = Random.Shared.Next(100000,1000000000);
foreach (var response in new[] { "null", "[\"https://example.test\"]" })
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    using var pipe = new System.IO.Pipes.NamedPipeServerStream("SinergiaAI.PrintAgent.Approval." + fixturePort,System.IO.Pipes.PipeDirection.InOut,1,System.IO.Pipes.PipeTransmissionMode.Byte,System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly);
    var serve = Task.Run(async () =>
    {
        await pipe.WaitForConnectionAsync(timeout.Token);
        using var reader = new StreamReader(pipe,leaveOpen:true);
        await reader.ReadLineAsync(timeout.Token);
        await using var writer = new StreamWriter(pipe,leaveOpen:true) { AutoFlush = true };
        await writer.WriteLineAsync(response);
    });
    var client = (Task<string>)call.Invoke(null,new object[] { fixturePort,new { Operation = "origins" } })!;
    var actual = await client;
    await serve;
    if (actual != response) throw new Exception("Desktop pipe response mismatch");
}
Console.WriteLine("PASS actual desktop pipe client preserves poll and authorized-site responses through disposal.");

var stateType = Assembly.Load("SinergiaAI.PrintAgent.Approval").GetType("SinergiaAI.PrintAgent.Approval.DesktopServiceState",true)!;
bool StateFlag(object state,string name) => (bool)stateType.GetProperty(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(state)!;
foreach (var value in Enum.GetValues<System.ServiceProcess.ServiceControllerStatus>())
{
    foreach (var changing in new[] { false,true })
    {
        var state = Activator.CreateInstance(stateType,new object[] { value,changing })!;
        if (StateFlag(state,"CanStart") != (!changing && value == System.ServiceProcess.ServiceControllerStatus.Stopped)) throw new Exception("Invalid start button state");
        if (StateFlag(state,"CanStop") != (!changing && value is System.ServiceProcess.ServiceControllerStatus.Running or System.ServiceProcess.ServiceControllerStatus.Paused)) throw new Exception("Invalid stop button state");
        if (StateFlag(state,"CanManageSites") != (!changing && value == System.ServiceProcess.ServiceControllerStatus.Running)) throw new Exception("Invalid sites button state");
    }
}
Console.WriteLine("PASS start/stop/sites controls for every Windows service state and busy transitions.");
if (args.Contains("--check-installed-service"))
{
    // Read only: never install, start, stop, authorize or submit to the real service.
    var json = await (Task<string>)call.Invoke(null,new object[] { 17831,new { Operation = "origins" } })!;
    var origins = System.Text.Json.JsonSerializer.Deserialize<string[]>(json)!;
    Console.WriteLine("PASS actual installed service: authorized-site list read successfully (" + origins.Length + " entries). No state changed.");
}
