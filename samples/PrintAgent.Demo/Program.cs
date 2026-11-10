using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using PrintAgentDemo;
using SinergiaAI.PrintAgent;

var simulate = args.Contains("--simulate", StringComparer.OrdinalIgnoreCase);
var builder = WebApplication.CreateBuilder(args.Where(a => !a.Equals("--simulate", StringComparison.OrdinalIgnoreCase)).ToArray());
if (string.IsNullOrEmpty(builder.Configuration["urls"])) builder.WebHost.UseUrls("http://localhost:5199");
var data = Path.GetFullPath(builder.Configuration["Demo:DataDirectory"] ?? ".demo-data", builder.Environment.ContentRootPath);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuthenticationStateProvider, DemoAuthenticationState>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o =>
{
    o.LoginPath = "/demo/sign-in";
    o.Cookie.Name = "PrintAgent.Demo";
});
builder.Services.AddAuthorization();
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(data, "keys")));
builder.Services.AddPrintAgent(o =>
{
    o.DataDirectory = data;
    o.AutomaticAuthorization = builder.Configuration.GetValue<bool?>("Demo:AutomaticAuthorization") ?? builder.Configuration["Demo:ErrorExample"] != "authorization";
    o.BrowserStoragePrefix = simulate ? "PrintAgent.Simulator." : "PrintAgent.Demo.";
    o.LocalBridgePort = builder.Configuration.GetValue<int?>("Demo:BridgePort") ?? (simulate ? 17832 : 17831);
});
var errorExample = builder.Configuration["Demo:ErrorExample"];
if (!string.IsNullOrWhiteSpace(errorExample) && !simulate)
    throw new InvalidOperationException("Error examples require --simulate.");
builder.Services.AddSingleton(new DemoMode(simulate, errorExample));
builder.Services.AddSingleton<DemoCapture>();
if (errorExample is not null && errorExample is not ("unavailable" or "authorization"))
    throw new ArgumentException("Use unavailable or authorization for Demo:ErrorExample.");
if (simulate && errorExample != "unavailable") builder.Services.AddHostedService<DemoSimulator>();

var app = builder.Build();
// This sample is intentionally restricted to a local development workstation.
app.Use(async (context, next) =>
{
    if (context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip))
    { context.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
    await next(context);
});
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapGet("/demo/sign-in", async (HttpContext context) =>
{
    var user = context.Request.Query["user"].ToString();
    if (string.IsNullOrWhiteSpace(user)) user = "local-demo";
    if (user.Length > 64) return Results.BadRequest();
    var identity = new ClaimsIdentity(new[]
    {
        new Claim(ClaimTypes.NameIdentifier, user),
        new Claim(ClaimTypes.Name, user)
    }, CookieAuthenticationDefaults.AuthenticationScheme);
    await context.SignInAsync(new ClaimsPrincipal(identity));
    return Results.LocalRedirect("/");
}).AllowAnonymous();
app.MapGet("/health", () => Results.Ok()).AllowAnonymous();
app.MapPrintAgent();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.Run();

namespace PrintAgentDemo
{
    public sealed record DemoMode(bool Simulated, string? ErrorExample = null);
    internal sealed class DemoAuthenticationState(IHttpContextAccessor accessor) : AuthenticationStateProvider
    {
        private readonly ClaimsPrincipal _user = accessor.HttpContext?.User ?? new();
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => Task.FromResult(new AuthenticationState(_user));
    }
}
