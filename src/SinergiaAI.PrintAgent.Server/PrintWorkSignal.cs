namespace SinergiaAI.PrintAgent.Server;

/// <summary>Process-local hints only. SQLite remains the authoritative durable queue.</summary>
internal sealed class PrintWorkSignal
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, Entry> _waiting = new();
    private sealed class Entry
    {
        internal readonly TaskCompletionSource Source = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Subscribers;
    }

    internal IDisposable Subscribe(Guid installationId, out Task changed)
    {
        lock (_gate)
        {
            if (!_waiting.TryGetValue(installationId, out var entry))
                _waiting.Add(installationId, entry = new());
            entry.Subscribers++;
            changed = entry.Source.Task;
            return new Subscription(this, installationId, entry);
        }
    }

    internal void Notify(Guid installationId)
    {
        Entry? entry;
        lock (_gate) _waiting.Remove(installationId, out entry);
        entry?.Source.TrySetResult();
    }

    private sealed class Subscription(PrintWorkSignal owner, Guid id, Entry entry) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            lock (owner._gate)
            {
                if (_disposed) return;
                _disposed = true;
                if (--entry.Subscribers == 0 && owner._waiting.TryGetValue(id, out var current)
                    && ReferenceEquals(current, entry)) owner._waiting.Remove(id);
            }
        }
    }
}
