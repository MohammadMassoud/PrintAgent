using System.Net;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace SinergiaAI.PrintAgent.Agent;

/// <summary>Protocol 2: browser to local durable queue. No remote API, user token or server database.</summary>
public sealed class LocalWebSocketBridge(AgentOptions options, LocalPrintQueue queue, LocalPrinters printers, AuthorizationRequests requests, ILogger<LocalWebSocketBridge> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _connections = new(16);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.LocalBridgePort == 0) return;
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(s => { s.Listen(IPAddress.Loopback, options.LocalBridgePort); s.Limits.MaxRequestBodySize = 4096; s.Limits.MaxConcurrentConnections = 32; s.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(5); });
        builder.Services.AddCors(c => c.AddDefaultPolicy(p => p.SetIsOriginAllowed(ValidOrigin).WithMethods("GET").WithHeaders("content-type")));
        await using var app = builder.Build();
        app.Use(async (context,next) =>
        {
            if (context.Request.Host.Host != "127.0.0.1" || context.Connection.RemoteIpAddress is not { } ip || !IPAddress.IsLoopback(ip) || !ValidOrigin(context.Request.Headers.Origin.ToString()))
            { context.Response.StatusCode = 403; return; }
            context.Response.Headers.CacheControl = "no-store";
            if (context.Request.Headers["Access-Control-Request-Private-Network"] == "true") context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
            await next(context);
        });
        app.UseCors(); app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20), KeepAliveTimeout = TimeSpan.FromSeconds(20) });
        app.MapGet("/info", () => Results.Ok(new { protocol = 2, version = typeof(LocalWebSocketBridge).Assembly.GetName().Version!.ToString(3), product = "SinergiaAI.PrintAgent", transport = "websocket", desktopAuthorization = true, multiApplication = true }));
        app.Map("/ws", async context =>
        {
            if (!context.WebSockets.IsWebSocketRequest) { context.Response.StatusCode = 400; return; }
            if (!await _connections.WaitAsync(0,context.RequestAborted)) { context.Response.StatusCode = 429; return; }
            try
            {
                using var socket = await context.WebSockets.AcceptWebSocketAsync();
                var origin = LocalPrintQueue.NormalizeOrigin(context.Request.Headers.Origin.ToString());
                await ServeAsync(socket,origin,context.RequestAborted);
            }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or IOException) { }
            finally { _connections.Release(); }
        });
        logger.LogInformation("Local WebSocket printing listening on 127.0.0.1:{Port}. Legacy API workers: {LegacyEnabled}.", options.LocalBridgePort, options.EnableLegacyApi);
        await app.RunAsync(stoppingToken);
    }
    private static bool ValidOrigin(string? value) { try { _ = LocalPrintQueue.NormalizeOrigin(value ?? ""); return true; } catch (ArgumentException) { return false; } }
    private async Task ServeAsync(WebSocket socket, string origin, CancellationToken ct)
    {
        var buffer = new byte[16384];
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ct = lifetime.Token;
        using var sends = new SemaphoreSlim(1,1);
        var changes = Channel.CreateBounded<LocalJob>(1000);
        void Changed(string source,LocalJob job) { if (source == origin && !changes.Writer.TryWrite(job)) changes.Writer.TryComplete(); }
        queue.JobChanged += Changed;
        var notifications = Task.Run(async () =>
        {
            try
            {
                await foreach (var job in changes.Reader.ReadAllAsync(ct))
                {
                    if (!queue.IsApproved(origin)) continue;
                    await sends.WaitAsync(ct);
                    try { await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { eventType = "jobChanged", job },Json),WebSocketMessageType.Text,true,ct); }
                    finally { sends.Release(); }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or IOException) { }
        },CancellationToken.None);
        try
        {
        while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            using var data = new MemoryStream(); WebSocketReceiveResult part;
            do
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(2));
                part = await socket.ReceiveAsync(buffer,timeout.Token);
                if (part.MessageType == WebSocketMessageType.Close) { await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure,"Closed",ct); return; }
                if (part.MessageType != WebSocketMessageType.Text || data.Length + part.Count > 8 * 1024 * 1024)
                { await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig,"Text messages up to 8 MiB only",ct); return; }
                data.Write(buffer,0,part.Count);
            } while (!part.EndOfMessage);
            string? id = null;
            try
            {
                using var document = JsonDocument.Parse(data.ToArray()); var root = document.RootElement;
                id = root.GetProperty("id").GetString();
                if (id is null || id.Length > 64) throw new ArgumentException("Invalid request ID.");
                var method = root.GetProperty("method").GetString();
                if (!queue.IsApproved(origin))
                {
                    if (method == "hello" && root.TryGetProperty("requestAuthorization",out var ask) && ask.ValueKind == JsonValueKind.True)
                    {
                        var decision = requests.Ask(origin);
                        await Reply(socket,sends,id,new { authorizationPending = decision is null, authorizationDenied = decision == false },null,ct);
                    }
                    else await Reply(socket,sends,id,null,"LOCAL_ORIGIN_DENIED",ct);
                    continue;
                }
                object result = method switch
                {
                    "hello" or "printers" => new { protocol = 2, version = typeof(LocalWebSocketBridge).Assembly.GetName().Version!.ToString(3), machineId = queue.MachineId, machineName = Environment.MachineName, printers = printers.List() },
                    "enqueue" => queue.Enqueue(origin, root.GetProperty("documents").Deserialize<LocalDocument[]>(Json) ?? [], printers.Destinations()),
                    "jobs" => queue.GetJobs(origin, root.GetProperty("jobIds").Deserialize<Guid[]>(Json) ?? []),
                    "ping" => new { alive = true },
                    _ => throw new ArgumentException("Unknown operation.")
                };
                await Reply(socket,sends,id,result,null,ct);
            }
            catch (Exception ex) when (ex is ArgumentException or FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
            { await Reply(socket,sends,id,null,"REQUEST_INVALID",ct); }
            catch (Exception ex)
            {
                logger.LogError(ex,"Local request failed. Reference {Reference}.", id);
                await Reply(socket,sends,id,null,"LOCAL_STORAGE_UNAVAILABLE",ct);
            }
        }
        }
        finally { queue.JobChanged -= Changed; changes.Writer.TryComplete(); await lifetime.CancelAsync(); await notifications; }
    }
    private static async Task Reply(WebSocket socket,SemaphoreSlim gate,string? id,object? result,string? error,CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(new { id, result, error },Json),WebSocketMessageType.Text,true,ct); }
        finally { gate.Release(); }
    }
}
