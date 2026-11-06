using System.Security.Claims;
using System.Text.Encodings.Web;
using Fixture;
using Maui.Comercial.Pages.Testes;
using Maui.Comercial.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using SinergiaPrint.Server;
using Syncfusion.Blazor;

var builder = WebApplication.CreateBuilder(args);
var data = Environment.GetEnvironmentVariable("PRINT_FIXTURE_DATA") ?? Path.Combine(builder.Environment.ContentRootPath, "state");
builder.Configuration["SinergiaPrint:DataDirectory"] = data;
builder.Services.AddSinergiaPrintServer(o => o.DatabasePath = Path.Combine(data, "printing.db"));
builder.Services.AddRazorComponents().AddInteractiveServerComponents();
builder.Services.AddSyncfusionBlazor();
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(data,"keys")));
builder.Services.AddAuthentication("Fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthHandler>("Fixture", _ => {});
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<AuthenticationStateProvider, FixtureAuthState>();
builder.Services.AddScoped<AgenteImpressaoTesteService>();
builder.Services.AddScoped<IAgenteImpressaoTesteService>(s=>s.GetRequiredService<AgenteImpressaoTesteService>());
var app = builder.Build();
app.UseAuthentication();app.UseAuthorization();app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();
app.MapSinergiaPrintAgent("/sinergiaprint");
app.MapComercialPrintInstaller();
app.MapGet("/fixture/connection/{id:guid}", async (Guid id,IAgenteImpressaoTesteService service)=> await service.ObterConexaoAsync(id) is {} connection ? Results.Ok(connection) : Results.NotFound());
app.MapGet("/health",()=>"ok");
app.Run();

sealed class FixtureAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,ILoggerFactory logger,UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options,logger,encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var user = Request.Headers["X-Fixture-User"].FirstOrDefault() ?? "fixture-user";
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
