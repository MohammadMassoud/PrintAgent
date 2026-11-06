using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using SinergiaPrint.Contracts;

namespace SinergiaPrint.Server;

public static class PrintServerExtensions
{
    private static readonly JsonSerializerOptions AgentJson = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public static IServiceCollection AddSinergiaPrintServer(this IServiceCollection services, Action<PrintServerOptions>? configure = null)
    {
        if (configure is not null) services.Configure(configure);
        else services.AddOptions<PrintServerOptions>();
        services.AddSingleton<PrintStore>();
        services.AddDataProtection();
        services.AddSingleton<PrintEnrollmentService>();
        return services;
    }

    public static IEndpointRouteBuilder MapSinergiaPrintAgent(this IEndpointRouteBuilder endpoints, string prefix = "")
    {
        endpoints.MapPost(prefix.TrimEnd('/') + "/enrollment/redeem", (EnrollmentCodeRequest request, HttpContext context, PrintEnrollmentService enrollment) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
            var configuration = enrollment.Redeem(request.Code);
            return configuration is not null
                ? Results.Json(configuration, AgentJson)
                : Results.BadRequest(new { error = "Codigo de instalacao invalido, expirado ou ja utilizado. Baixe um novo instalador pelo aplicativo." });
        }).AllowAnonymous();
        var agents = endpoints.MapGroup(prefix.TrimEnd('/') + "/agent/{installationId:guid}").AllowAnonymous();
        agents.AddEndpointFilter(async (context, next) =>
        {
            var id = Guid.Parse(context.HttpContext.Request.RouteValues["installationId"]!.ToString()!);
            var store = context.HttpContext.RequestServices.GetRequiredService<PrintStore>();
            return store.Authenticate(id, context.HttpContext.Request.Headers["X-Agent-Token"].ToString()) ? await next(context) : Results.Unauthorized();
        });
        agents.MapPost("/printers", (Guid installationId, ReportPrintersRequest request, PrintStore store) =>
        {
            if (request.Printers is null || request.Printers.Count > 200 || request.Printers.Any(p => string.IsNullOrWhiteSpace(p.Id) || p.Id.Length > 500 || string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 500) || request.Printers.Select(p => p.Id).Distinct().Count() != request.Printers.Count) return Results.BadRequest();
            store.Report(installationId, request.Printers); return Results.NoContent();
        });
        agents.MapPost("/claim", (Guid installationId, PrintStore store) => store.Claim(installationId) is { } job ? Results.Ok(job) : Results.NoContent());
        // Bind with protocol options instead of changing JSON settings of the host application.
        agents.MapPost("/jobs/{jobId:guid}/complete", async Task<IResult> (Guid installationId, Guid jobId, HttpContext context, PrintStore store) =>
        {
            CompletePrintRequest? request;
            try
            {
                request = await context.Request.ReadFromJsonAsync<CompletePrintRequest>(AgentJson, context.RequestAborted);
            }
            catch (JsonException) { return Results.BadRequest(); }
            if (request is null || request.Status is not (PrintStatus.Sent or PrintStatus.Failed or PrintStatus.Unknown) || request.Error?.Length > 2000)
                return Results.BadRequest();
            return store.Complete(installationId, jobId, request) ? Results.NoContent() : Results.Conflict();
        });

        return endpoints;
    }
}
