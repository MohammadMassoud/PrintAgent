using System.Diagnostics;
using SinergiaAI.PrintAgent.Contracts;

namespace SinergiaAI.PrintAgent.Server;

public sealed partial class PrintStore
{
    private readonly PrintWorkSignal _workAvailable = new();

    /// <summary>Waits without holding a SQLite connection/transaction. A notification never claims a job.</summary>
    public async Task<ClaimedPrintJob?> ClaimAsync(Guid installationId, int waitSeconds = 0, CancellationToken ct = default)
    {
        if (waitSeconds is < 0 or > PrintProtocol.MaxLongPollSeconds)
            throw new ArgumentOutOfRangeException(nameof(waitSeconds));
        ct.ThrowIfCancellationRequested();
        if (waitSeconds == 0) return Claim(installationId);
        var started = Stopwatch.GetTimestamp();
        var budget = TimeSpan.FromSeconds(waitSeconds);
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            // Subscribe BEFORE checking SQLite to avoid losing a commit between the check and wait.
            using var subscription = _workAvailable.Subscribe(installationId, out var changed);
            if (Claim(installationId) is { } job) return job;
            var remaining = budget - Stopwatch.GetElapsedTime(started);
            if (remaining <= TimeSpan.Zero) return null;
            try { await changed.WaitAsync(remaining, ct); }
            catch (TimeoutException) { return null; }
            // Another caller may have claimed first. Recheck the durable queue after every hint.
        }
    }
}
