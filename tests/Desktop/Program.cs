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
