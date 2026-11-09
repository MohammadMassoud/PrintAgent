using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using SinergiaAI.PrintAgent.Contracts;

namespace SinergiaAI.PrintAgent.Agent;

/// <summary>Loopback discovery and automatic application enrollment. Never accepts print bytes or returns credentials.</summary>
public sealed class LocalAgentBridge(AgentOptions options, ApplicationConnections connections, ILogger<LocalAgentBridge> logger) : BackgroundService
{
    private sealed record ConnectRequest(string ApiBaseUrl, string Challenge, string? EnrollmentCode);

    private static bool AllowedOrigin(string? origin) => Uri.TryCreate(origin, UriKind.Absolute, out var site)
        && site.UserInfo.Length == 0 && site.AbsolutePath == "/" && site.Query.Length == 0 && site.Fragment.Length == 0
        && (site.Scheme == "https" || site.Scheme == "http" && site.IsLoopback);

    private static bool SameApplication(Uri api, Uri site) =>
        string.Equals(api.GetLeftPart(UriPartial.Authority), site.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)
        // Local development can use an HTTPS UI with a separate HTTP API port.
        || api.IsLoopback && site.IsLoopback;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.LocalBridgePort == 0) return;
        if (options.LocalBridgePort is < 1024 or > 65535) throw new InvalidOperationException("Invalid local discovery port.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await RunBridgeAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { logger.LogWarning("Local discovery unavailable ({Kind}). Printing workers remain active.", ex.GetType().Name); }
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
        builder.Services.AddCors(cors => cors.AddDefaultPolicy(policy => policy.SetIsOriginAllowed(AllowedOrigin)
            .WithMethods("GET", "POST").WithHeaders("content-type")));
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
        app.MapGet("/info", () => Results.Ok(new { protocol = 1, version = "0.5.0", product = "SinergiaAI.PrintAgent", multiApplication = true }));
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(12) };
        app.MapPost("/connect", async Task<IResult> (ConnectRequest request, HttpContext context, CancellationToken ct) =>
        {
            if (!Uri.TryCreate(request.ApiBaseUrl, UriKind.Absolute, out var requested)
                || requested.UserInfo.Length != 0 || requested.Query.Length != 0 || requested.Fragment.Length != 0
                || requested.AbsoluteUri.Length > 2048
                || requested.Scheme != "https" && !(requested.Scheme == "http" && requested.IsLoopback)
                || request.Challenge is not { Length: 64 } || !request.Challenge.All(Uri.IsHexDigit)) return Results.BadRequest();
            var site = new Uri(context.Request.Headers.Origin.ToString());
            // A page can enroll only its own API, not redirect the service to arbitrary destinations.
            if (!SameApplication(requested, site)) return Results.StatusCode(403);
            var api = ApplicationConnections.Normalize(requested.AbsoluteUri);
            try
            {
                var profile = await connections.FindAsync(api, ct);
                if (profile is null)
                {
                    if (request.EnrollmentCode is null) return Results.Json(new { enrollmentRequired = true }, statusCode: 428);
                    if (request.EnrollmentCode.Length != 64 || !request.EnrollmentCode.All(Uri.IsHexDigit)) return Results.BadRequest();
                    profile = await connections.AddAsync(api, async () =>
                    {
                        using var response = await client.PostAsJsonAsync(new Uri(new Uri(api), "enrollment/redeem"), new EnrollmentCodeRequest(request.EnrollmentCode), ct);
                        response.EnsureSuccessStatusCode();
                        var setup = await response.Content.ReadFromJsonAsync<AgentSetupConfiguration>(ct)
                            ?? throw new InvalidDataException("The application returned no enrollment configuration.");
                        return new AgentOptions { ApiBaseUrl = setup.ApiBaseUrl, InstallationId = setup.InstallationId, Token = setup.Token };
                    }, ct);
                }
                using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(api), "machine/confirm"));
                message.Headers.Add("X-Agent-Token", profile.Token);
                message.Content = JsonContent.Create(new { request.Challenge, profile.InstallationId, MachineName = Environment.MachineName });
                using var confirmed = await client.SendAsync(message, ct);
                return confirmed.IsSuccessStatusCode ? Results.Ok(new { connected = true })
                    : Results.Json(new { error = "The agent could not confirm this machine with the application." }, statusCode: 502);
            }
            catch (HttpRequestException) { return Results.Json(new { error = "The agent could not reach or enroll with the application API." }, statusCode: 502); }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { return Results.StatusCode(504); }
            catch (Exception ex) when (ex is InvalidDataException or ArgumentException or InvalidOperationException)
            { return Results.Json(new { error = "The application connection could not be saved. Check the agent log and configuration." }, statusCode: 502); }
        });
        logger.LogInformation("Machine discovery listening on loopback port {Port}; multiple applications are supported.", options.LocalBridgePort);
        await app.RunAsync(cancellationToken);
    }
}
