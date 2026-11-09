using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SinergiaAI.PrintAgent.Server;

namespace SinergiaAI.PrintAgent;

public static class PrintAgentExtensions
{
    public static IServiceCollection AddPrintAgent(this IServiceCollection services, Action<PrintAgentOptions>? configure = null)
    {
        var options = services.AddOptions<PrintAgentOptions>();
        if (configure is not null) options.Configure(configure);
        options.Validate(o => !string.IsNullOrWhiteSpace(o.ApiPrefix) && o.ApiPrefix.Trim('/').Length > 0
            && o.ApiPrefix.Trim('/').Split('/').All(s => s.Length > 0 && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            && !string.IsNullOrWhiteSpace(o.DataDirectory)
            && o.LocalBridgePort is >= 1024 and <= 65535
            && Version.TryParse(o.AvailableAgentVersion, out _)
            && !string.IsNullOrWhiteSpace(o.BrowserStoragePrefix) && !string.IsNullOrWhiteSpace(o.LegacyProtectionPurpose), "Invalid PrintAgent configuration.").ValidateOnStart();
        services.AddSinergiaAIPrintAgentServer();
        services.AddOptions<PrintServerOptions>().Configure<IOptions<PrintAgentOptions>, IWebHostEnvironment>((server, agent, environment) =>
        {
            server.DatabasePath = Path.Combine(Path.GetFullPath(agent.Value.DataDirectory, environment.ContentRootPath), "printing.db");
        });
        services.AddScoped<PrintAgentBackend>();
        services.AddScoped<PrintAgent>();
        return services;
    }

    public static IEndpointRouteBuilder MapPrintAgent(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<PrintAgentOptions>>().Value;
        var prefix = "/" + options.ApiPrefix.Trim('/');
        endpoints.MapSinergiaAIPrintAgent(prefix);
        endpoints.MapGet(prefix + "/browser.js", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var stream = typeof(PrintAgentExtensions).Assembly.GetManifestResourceStream("PrintAgent.Browser.js")!;
            return Results.Stream(stream, "text/javascript; charset=utf-8");
        }).AllowAnonymous();
        endpoints.MapGet(prefix + "/machine/challenge", (HttpContext context, PrintDiscoveryService discovery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { challenge = discovery.CreateChallenge() });
        }).RequireAuthorization();
        endpoints.MapPost(prefix + "/machine/enrollment", IResult (
            ApplicationEnrollmentRequest request, HttpContext context, PrintAgentBackend service, CancellationToken ct) =>
        {
            // A JSON body forces a CORS preflight on cross-origin requests; no form-based enrollment.
            if (!request.Enroll) return Results.BadRequest();
            context.Response.Headers.CacheControl = "no-store";
            var http = context.Request;
            var application = new Uri($"{http.Scheme}://{http.Host}{http.PathBase}/");
            return Results.Ok(service.CreateApplicationEnrollment(application, context.User, ct));
        }).RequireAuthorization();
        endpoints.MapGet(prefix + "/machine/proof", (HttpContext context, PrintDiscoveryService discovery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var proof = discovery.RedeemChallenge(context.Request.Headers["X-PrintAgent-Challenge"].ToString(), context.Request.Headers["X-PrintAgent-Machine"].ToString());
            return proof is not null ? Results.Ok(proof) : Results.NotFound();
        }).RequireAuthorization();
        endpoints.MapGet(prefix + "/installers/{installationId:guid}", async Task<IResult> (
            Guid installationId, HttpContext context, PrintAgentBackend service, CancellationToken ct) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            try
            {
                var request = context.Request;
                var baseAddress = new Uri($"{request.Scheme}://{request.Host}{request.PathBase}/");
                if (baseAddress.Scheme == "http" && !baseAddress.IsLoopback)
                    baseAddress = new UriBuilder("https", request.Host.Host, request.Host.Port ?? -1, request.PathBase + "/").Uri;
                var installer = await service.CreateInstallerAsync(installationId, baseAddress, context.User, context.Request.Headers["X-PrintAgent-Machine"].ToString(), ct);
                return Results.File(installer, "application/octet-stream", "SinergiaAI.PrintAgent-Setup.exe");
            }
            catch (UnauthorizedAccessException) { return Results.NotFound(); }
        }).RequireAuthorization();
        return endpoints;
    }

    private sealed record ApplicationEnrollmentRequest(bool Enroll);
}
