namespace SinergiaAI.PrintAgent.Agent;

/// <summary>Bounded, short-lived requests. Only the local desktop pipe may resolve them.</summary>
public sealed class AuthorizationRequests
{
    public sealed record Request(Guid Id, string Origin, DateTimeOffset ExpiresAt, DateTimeOffset? ClaimedUntil = null, bool? Approved = null);
    private readonly object _gate = new();
    private readonly Dictionary<string,Request> _requests = new(StringComparer.Ordinal);
    public bool? Ask(string origin)
    {
        lock (_gate)
        {
            Prune();
            if (_requests.TryGetValue(origin,out var existing)) return existing.Approved == true ? false : existing.Approved;
            if (_requests.Count >= 32) return false;
            _requests[origin] = new(Guid.NewGuid(),origin,DateTimeOffset.UtcNow.AddMinutes(2)); return null;
        }
    }
    public Request? Claim()
    {
        lock (_gate)
        {
            Prune();
            var item = _requests.Values.FirstOrDefault(r => r.Approved is null && (r.ClaimedUntil is null || r.ClaimedUntil < DateTimeOffset.UtcNow));
            if (item is null) return null;
            _requests[item.Origin] = item with { ClaimedUntil = DateTimeOffset.UtcNow.AddSeconds(70) }; return item;
        }
    }
    public bool Resolve(Guid id,string origin,bool approved)
    {
        lock (_gate)
        {
            Prune();
            if (!_requests.TryGetValue(origin,out var item) || item.Id != id) return false;
            _requests[origin] = item with { Approved = approved }; return true;
        }
    }
    private void Prune() { foreach (var key in _requests.Where(p => p.Value.ExpiresAt < DateTimeOffset.UtcNow).Select(p => p.Key).ToArray()) _requests.Remove(key); }
}
