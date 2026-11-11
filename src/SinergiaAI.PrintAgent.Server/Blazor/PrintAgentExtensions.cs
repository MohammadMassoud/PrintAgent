using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
namespace SinergiaAI.PrintAgent;

/// <summary>Registers the scoped Blazor Server integration and its embedded browser module.</summary>
public static class PrintAgentExtensions
{
    /// <summary>Adds PrintAgent and validates its bridge and encoding options at application startup.</summary>
    /// <param name="services">The host service collection.</param>
    /// <param name="configure">Optional bridge, installer and default text-encoding settings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddPrintAgent(this IServiceCollection services, Action<PrintAgentOptions>? configure = null)
    {
        var options = services.AddOptions<PrintAgentOptions>();
        if (configure is not null) options.Configure(configure);
        options.Validate(o => o.TextEncoding is not null && o.LocalBridgePort is >= 1024 and <= 65535
            && !string.IsNullOrWhiteSpace(o.ApiPrefix) && o.ApiPrefix.Trim('/').Split('/').All(s => s.Length > 0 && s.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            && Uri.TryCreate(o.InstallerUrl,UriKind.Absolute,out var installer) && installer.Scheme == "https", "Invalid PrintAgent configuration.").ValidateOnStart();
        services.AddScoped<PrintAgent>(); return services;
    }
    /// <summary>Serves the browser module. Does not create a printing API, database or remote worker.</summary>
    /// <param name="endpoints">The application's endpoint route builder.</param>
    /// <returns>The route builder for chaining.</returns>
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
