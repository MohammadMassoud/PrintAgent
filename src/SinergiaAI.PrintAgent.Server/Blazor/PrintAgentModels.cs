using System.Text;

namespace SinergiaAI.PrintAgent;

/// <summary>State of the local machine's print integration; independent of the application's user.</summary>
public enum PrintAgentStatus
{
    /// <summary>The local agent cannot be reached.</summary>
    Unavailable,
    /// <summary>The website is not authorized on this workstation.</summary>
    OriginDenied,
    /// <summary>The agent uses an incompatible protocol.</summary>
    Incompatible,
    /// <summary>A legacy agent belongs to another application.</summary>
    OtherApplication,
    /// <summary>The connection failed; inspect LastError.</summary>
    Error,
    /// <summary>A legacy backend connection is pending.</summary>
    AwaitingApi,
    /// <summary>The local agent is connected and authorized.</summary>
    Connected,
    /// <summary>The connection has not been started.</summary>
    NotStarted,
    /// <summary>This component explicitly disconnected.</summary>
    Unlinked,
    /// <summary>A legacy backend is unavailable.</summary>
    ServerUnavailable
}

/// <summary>A Windows queue or configured TCP destination reported by the workstation agent.</summary>
/// <param name="Id">Opaque destination identifier. Pass this value to send methods instead of the display name.</param>
/// <param name="Name">Human-readable printer name.</param>
/// <param name="IsDefault">Whether this is the default queue reported by the agent.</param>
public sealed record PrintPrinter(string Id, string Name, bool IsDefault);

/// <summary>A printer-ready document for an atomic batch. Text and binary documents use the same RAW delivery pipeline.</summary>
/// <param name="PrinterId">The destination identifier from PrintPrinter.Id.</param>
/// <param name="Content">The exact bytes to deliver, with a maximum size of 2 MiB. Do not mutate them during submission.</param>
/// <param name="IdempotencyKey">A persisted request key, unique within the website origin, identifying this destination and byte sequence.</param>
public sealed record PrintDocument(string PrinterId, byte[] Content, string IdempotencyKey)
{
    /// <summary>Encodes printer commands once, without a BOM or implicit command substitutions.</summary>
    /// <param name="printerId">The destination identifier reported by the agent.</param>
    /// <param name="content">Printer-compatible commands. Literal text such as 0x1B is not interpreted as a byte escape.</param>
    /// <param name="idempotencyKey">The stable key to persist with the resulting bytes before submission.</param>
    /// <param name="encoding">The encoding understood by the printer. Unsupported characters are rejected.</param>
    /// <returns>A document that can be combined with binary documents in SendBatchAsync.</returns>
    /// <exception cref="ArgumentException">An identifier, key, content or encoded size is invalid.</exception>
    /// <exception cref="EncoderFallbackException">The encoding cannot represent the supplied text.</exception>
    public static PrintDocument FromText(string printerId, string content, string idempotencyKey, Encoding encoding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(printerId);
        ArgumentException.ThrowIfNullOrEmpty(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(encoding);
        if (idempotencyKey.Length > 200) throw new ArgumentException("Request keys may contain at most 200 characters.", nameof(idempotencyKey));
        var strict = (Encoding)encoding.Clone();
        strict.EncoderFallback = EncoderFallback.ExceptionFallback;
        if (strict.GetByteCount(content) > 2 * 1024 * 1024) throw new ArgumentException("A document may contain at most 2 MiB of encoded bytes.", nameof(content));
        return new(printerId, strict.GetBytes(content), idempotencyKey);
    }
}

/// <summary>The durable delivery state; transport completion does not prove physical paper output.</summary>
public enum PrintJobStatus
{
    /// <summary>The job is durably queued and has not started delivery.</summary>
    Pending,
    /// <summary>Delivery has been claimed and may be in progress.</summary>
    Claimed,
    /// <summary>The bytes were delivered to the Windows spooler or TCP stream.</summary>
    Sent,
    /// <summary>The job failed. Inspect the result and printer before requesting another copy.</summary>
    Failed,
    /// <summary>Delivery may have started. Never automatically resubmit with a new key.</summary>
    Unknown
}

/// <summary>A queued job. Sent means bytes delivered to the destination, not physical paper output.</summary>
/// <param name="Id">The durable job identifier used by GetJobsAsync.</param>
/// <param name="InstallationId">The workstation identifier.</param>
/// <param name="PrinterId">The destination selected when the request was accepted.</param>
/// <param name="Status">The most recently observed delivery state.</param>
/// <param name="Error">The agent's delivery error, if available.</param>
/// <param name="CreatedAt">The acceptance timestamp.</param>
/// <param name="UpdatedAt">The most recent state-change timestamp.</param>
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
