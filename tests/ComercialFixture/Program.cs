using System.Security.Claims;
using System.Text.Encodings.Web;
using Fixture;
using Maui.Comercial.Pages.Testes;
using Sinergia.Maui.Common.Services;
using Sinergia.Maui.Common.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using SinergiaAI.PrintAgent.Server;
using Syncfusion.Blazor;

var builder = WebApplication.CreateBuilder(args);
var data = Environment.GetEnvironmentVariable("PRINT_FIXTURE_DATA") ?? Path.Combine(builder.Environment.ContentRootPath, "state");
builder.Services.AddAgenteImpressao(o =>
{
    o.ApiPrefix = "/sinergiaprint";
    o.DataDirectory = data;
    o.LocalBridgePort = int.TryParse(Environment.GetEnvironmentVariable("PRINT_BRIDGE_PORT"), out var port) ? port : 17831;
    o.BrowserStoragePrefix = "SinergiaPrint.Comercial.";
    o.ProtectionPurpose = "Comercial.SinergiaPrint.TestLink.v1";
});
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSyncfusionBlazor();
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(data,"keys")));
builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthHandler>("Fixture", _ => {});
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuthenticationStateProvider, FixtureAuthState>();
var app = builder.Build();
app.UseAuthentication();app.UseAuthorization();app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapAgenteImpressao();
app.MapGet("/fixture/connection/{id:guid}", async (Guid id, HttpContext context, IAgenteImpressaoService service) =>
{
    await service.ReconhecerMaquinaAsync(context.Request.Headers["X-PrintAgent-Machine"].ToString());
    return await service.ObterConexaoAsync(id) is {} connection ? Results.Ok(connection) : Results.NotFound();
});
app.MapPost("/fixture/send/{id:guid}", async Task<IResult> (Guid id, List<DocumentoAgenteImpressao> documents, HttpContext context, IAgenteImpressaoService service) =>
{
    try { await service.ReconhecerMaquinaAsync(context.Request.Headers["X-PrintAgent-Machine"].ToString()); return Results.Ok(await service.EnviarLoteAsync(id, documents)); }
    catch (UnauthorizedAccessException) { return Results.Unauthorized(); }
    catch (ArgumentException ex) { return Results.BadRequest(ex.Message); }
});
app.MapGet("/health",()=>"ok");
app.Run();

sealed class FixtureAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Request.Headers["X-Fixture-User"].FirstOrDefault() ?? Request.Cookies["FixtureUser"] ?? "fixture-user";
        if(user=="anonymous") return Task.FromResult(AuthenticateResult.NoResult());
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[]{new Claim(ClaimTypes.NameIdentifier,user),new Claim(ClaimTypes.Name,user)},"Fixture"));
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal,"Fixture")));
    }
}
sealed class FixtureAuthState(IHttpContextAccessor context) : AuthenticationStateProvider
{
    private readonly ClaimsPrincipal _user = context.HttpContext?.User ?? new ClaimsPrincipal();
    public override Task<AuthenticationState> GetAuthenticationStateAsync()=>Task.FromResult(new AuthenticationState(_user));
}
