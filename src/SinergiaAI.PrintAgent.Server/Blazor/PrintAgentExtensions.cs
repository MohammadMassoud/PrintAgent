using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
namespace SinergiaAI.PrintAgent;

public static class PrintAgentExtensions
{
    public static IServiceCollection AddPrintAgent(this IServiceCollection services, Action<PrintAgentOptions>? configure = null)
    {
        var options = services.AddOptions<PrintAgentOptions>();
        if (configure is not null) options.Configure(configure);
        options.Validate(o => o.LocalBridgePort is >= 1024 and <= 65535
            && !string.IsNullOrWhiteSpace(o.ApiPrefix) && o.ApiPrefix.Trim('/').Split('/').All(s => s.Length > 0 && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            && Uri.TryCreate(o.InstallerUrl,UriKind.Absolute,out var installer) && installer.Scheme == "https", "Invalid PrintAgent configuration.").ValidateOnStart();
        services.AddScoped<PrintAgent>(); return services;
    }
    /// <summary>Serves the browser module. Does not create a printing API, database or remote worker.</summary>
    public static IEndpointRouteBuilder MapPrintAgent(this IEndpointRouteBuilder endpoints)
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<PrintAgentOptions>>().Value;
        endpoints.MapGet("/" + options.ApiPrefix.Trim('/') + "/browser.js", (HttpContext context) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            return Results.Stream(typeof(PrintAgentExtensions).Assembly.GetManifestResourceStream("PrintAgent.Browser.js")!, "text/javascript; charset=utf-8");
        }).AllowAnonymous();
        return endpoints;
    }
}
