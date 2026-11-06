using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace SinergiaAI.PrintAgent.Agent;

/// <summary>Loopback-only machine discovery. Never returns a token or accepts print jobs.</summary>
public sealed class LocalAgentBridge(AgentOptions options, ILogger<LocalAgentBridge> logger) : BackgroundService
{
    private sealed record ConnectRequest(string ApiBaseUrl, string Challenge);
    private bool AllowedOrigin(string? origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var site) || site.UserInfo.Length != 0 || site.AbsolutePath != "/" || site.Query.Length != 0 || site.Fragment.Length != 0) return false;
        var api = new Uri(options.ApiBaseUrl);
        // Different HTTP/HTTPS ports are common with a development HTTPS UI and local service API.
        return api.IsLoopback && site.IsLoopback && site.Scheme is "https" or "http"
            || string.Equals(site.GetLeftPart(UriPartial.Authority), api.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
    }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.LocalBridgePort == 0) return;
        if (options.LocalBridgePort is < 1024 or > 65535) throw new InvalidOperationException("Invalid local discovery port.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunBridgeAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning("Local discovery unavailable ({Kind}). Printing worker remains active.", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
        }
    }
    private async Task RunBridgeAsync(CancellationToken cancellationToken)
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(server =>
        {
            server.Listen(IPAddress.Loopback, options.LocalBridgePort);
            server.Limits.MaxRequestBodySize = 4096;
            server.Limits.MaxRequestHeadersTotalSize = 8192;
            server.Limits.MaxConcurrentConnections = 16;
            server.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5);
            server.Limits.KeepAliveTimeout = TimeSpan.FromSeconds(5);
        });
        builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy.SetIsOriginAllowed(AllowedOrigin).WithMethods("GET", "POST").WithHeaders("content-type")));
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            if (context.Request.Host.Host != "127.0.0.1" || context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip)
                || !AllowedOrigin(context.Request.Headers.Origin.ToString()))
            { context.Response.StatusCode = 403; return; }
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            if (context.Request.Headers["Access-Control-Request-Private-Network"] == "true")
                context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
            await next(context);
        });
        app.UseCors();
        app.MapGet("/info", () => Results.Ok(new { protocol = 1, version = "0.3.0", product = "SinergiaAI.PrintAgent" }));
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(12) };
        app.MapPost("/connect", async Task<IResult> (ConnectRequest request, CancellationToken ct) =>
        {
            if (!Uri.TryCreate(request.ApiBaseUrl, UriKind.Absolute, out var requested)
                || requested.UserInfo.Length != 0 || requested.Query.Length != 0 || requested.Fragment.Length != 0
                || request.Challenge is not { Length: 64 } || !request.Challenge.All(Uri.IsHexDigit)) return Results.BadRequest();
            var configured = new Uri(options.ApiBaseUrl.TrimEnd('/') + "/");
            if (Uri.Compare(new Uri(requested.AbsoluteUri.TrimEnd('/') + "/"), configured, UriComponents.HttpRequestUrl, UriFormat.UriEscaped, StringComparison.Ordinal) != 0)
                return Results.Conflict(new { error = "O agente está configurado para outro aplicativo." });
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(configured, "machine/confirm"));
            message.Headers.Add("X-Agent-Token", options.Token);
            message.Content = JsonContent.Create(new { request.Challenge, options.InstallationId, MachineName = Environment.MachineName });
            try
            {
                using var response = await client.SendAsync(message, ct);
                return response.IsSuccessStatusCode ? Results.Ok(new { connected = true })
                    : Results.Json(new { error = "Não foi possível confirmar a máquina no aplicativo." }, statusCode: 502);
            }
            catch (HttpRequestException) { return Results.Json(new { error = "O agente não alcançou a API do aplicativo." }, statusCode: 502); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Results.StatusCode(504); }
        });
        logger.LogInformation("Machine discovery listening on loopback port {Port}.", options.LocalBridgePort);
        await app.RunAsync(cancellationToken);
    }
}
