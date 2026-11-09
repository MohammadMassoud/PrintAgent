using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using SinergiaAI.PrintAgent;
using SinergiaAI.PrintAgent.Server;

namespace PrintAgentDemo;

public sealed record CapturedDocument(DateTimeOffset ReceivedAt, int Length, string Content);
public sealed class DemoCapture
{
    private readonly object _gate = new();
    private readonly List<CapturedDocument> _documents = [];
    public IReadOnlyList<CapturedDocument> Documents { get { lock (_gate) return _documents.ToArray(); } }
    internal void Add(byte[] content)
    {
        lock (_gate)
        {
            _documents.Insert(0, new(DateTimeOffset.UtcNow, content.Length, System.Text.Encoding.UTF8.GetString(content)));
            if (_documents.Count > 20) _documents.RemoveAt(_documents.Count - 1);
        }
    }
}

/// <summary>Runs a disposable console agent and a loopback TCP receiver. Never installs a service.</summary>
internal sealed class DemoSimulator(
    IHostApplicationLifetime lifetime, IHostEnvironment environment, IServer server,
    PrintStore store, PrintDiscoveryService discovery, IOptions<PrintAgentOptions> settings,
    DemoCapture capture, ILogger<DemoSimulator> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The simulated agent requires Windows x64.");
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => ready.TrySetResult());
        await ready.Task.WaitAsync(stoppingToken);
        var address = server.Features.Get<IServerAddressesFeature>()?.Addresses
            .Select(a => Uri.TryCreate(a, UriKind.Absolute, out var uri) ? uri : null)
            .FirstOrDefault(u => u is { Scheme: "http", IsLoopback: true })
            ?? throw new InvalidOperationException("Start the simulator on an HTTP loopback URL.");
        var api = new Uri(address, "printing/").AbsoluteUri;
        // Set once after the server has selected its actual listening port.
        settings.Value.PublicBaseUrl = api;
        var credentials = store.CreateInstallation("Demo simulator");
        discovery.RegisterMachine(credentials, Environment.MachineName);
        using var receiver = new TcpListener(IPAddress.Loopback, 0);
        receiver.Start();
        var port = ((IPEndPoint)receiver.LocalEndpoint).Port;
        var agentDll = Path.Combine(AppContext.BaseDirectory, "DemoAgent", "SinergiaAI.PrintAgent.Agent.dll");
        if (!File.Exists(agentDll)) throw new FileNotFoundException("Build the sample before starting the simulator.", agentDll);
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(agentDll)! };
        start.ArgumentList.Add(agentDll);
        var agentData = Path.Combine(Path.GetFullPath(settings.Value.DataDirectory, environment.ContentRootPath), "agents", credentials.InstallationId.ToString("N"));
        var configuration = new Dictionary<string, string>
        {
            ["ApiBaseUrl"] = api,
            ["InstallationId"] = credentials.InstallationId.ToString(),
            ["Token"] = credentials.AgentToken,
            ["DataPath"] = agentData,
            ["LocalBridgePort"] = settings.Value.LocalBridgePort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["NetworkPrinters__0__Name"] = "Simulated TCP printer",
            ["NetworkPrinters__0__Address"] = $"tcp://127.0.0.1:{port}"
        };
        foreach (var pair in configuration) start.Environment["SINERGIAPRINT_Agent__" + pair.Key] = pair.Value;
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the temporary agent.");
        logger.LogInformation("Simulator started. Select Simulated TCP printer. No physical printer or permanent Windows service is used.");
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var client = await receiver.AcceptTcpClientAsync(stoppingToken);
                using var bytes = new MemoryStream();
                var buffer = new byte[8192];
                var stream = client.GetStream();
                int count;
                while ((count = await stream.ReadAsync(buffer, stoppingToken)) != 0)
                {
                    if (bytes.Length + count > 2 * 1024 * 1024) throw new InvalidOperationException("The simulated document exceeded 2 MiB.");
                    bytes.Write(buffer, 0, count);
                }
                capture.Add(bytes.ToArray());
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            receiver.Stop();
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }
}
