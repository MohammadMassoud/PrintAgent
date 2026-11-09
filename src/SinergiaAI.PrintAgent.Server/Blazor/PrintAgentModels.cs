namespace SinergiaAI.PrintAgent;

/// <summary>State of the local machine's print integration; independent of the application's user.</summary>
public enum PrintAgentStatus
{
    Unavailable, OriginDenied, Incompatible, OtherApplication, Error,
    AwaitingApi, Connected, NotStarted, Unlinked, ServerUnavailable
}

public sealed record PrintPrinter(string Id, string Name, bool IsDefault);
public sealed record PrintDocument(string PrinterId, byte[] Content, string IdempotencyKey);
public enum PrintJobStatus { Pending, Claimed, Sent, Failed, Unknown }

/// <summary>A queued job. Sent means bytes delivered to the destination, not physical paper output.</summary>
public sealed record PrintJob(Guid Id, Guid InstallationId, string PrinterId, PrintJobStatus Status,
    string? Error, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    internal static PrintJob FromContract(Contracts.PrintJobInfo job) => new(job.Id, job.InstallationId,
        job.PrinterId, (PrintJobStatus)job.Status, job.Error, job.CreatedAt, job.UpdatedAt);
}

// Internal enrollment/discovery plumbing; application pages use the PrintAgent facade.
internal sealed record MachineConnectionState(Contracts.InstallationInfo Installation, string Proof, string MachineName);
internal sealed record BrowserDiscoveryOptions(string BrowserApiBaseUrl, string AgentApiBaseUrl, string BridgeUrl);
internal sealed record DiscoveryResult(PrintAgentStatus Status, MachineConnectionState? Connection,
    string? InstalledVersion, string AvailableVersion, bool UpdateAvailable, string? Message);
