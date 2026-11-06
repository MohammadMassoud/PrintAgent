namespace SinergiaAI.PrintAgent.Server;

public sealed partial class PrintStore
{
    private void EnsureDiscoveryTables()
    {
        using var db = Open();
        Execute(db, null, """
            CREATE TABLE IF NOT EXISTS discovery_machines (installation_id TEXT PRIMARY KEY, protected_credentials TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS discovery_challenges (code_hash TEXT PRIMARY KEY, expires_at TEXT NOT NULL, installation_id TEXT, consumed INTEGER NOT NULL DEFAULT 0);
            CREATE TABLE IF NOT EXISTS discovery_grants (proof_hash TEXT PRIMARY KEY, installation_id TEXT NOT NULL, expires_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_discovery_grants_expiry ON discovery_grants(expires_at);
            """);
    }

    internal void AddDiscoveryChallenge(string hash, DateTimeOffset expires)
    {
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        Execute(db, tx, "DELETE FROM discovery_challenges WHERE expires_at<$now OR consumed=1", ("$now", Now()));
        Execute(db, tx, "INSERT INTO discovery_challenges(code_hash,expires_at) VALUES($hash,$expires)", ("$hash", hash), ("$expires", expires.ToString("O")));
        tx.Commit();
    }

    internal bool ConfirmDiscovery(string hash, Guid id, string encrypted)
    {
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        var count = Execute(db, tx, "UPDATE discovery_challenges SET installation_id=$id WHERE code_hash=$hash AND consumed=0 AND expires_at>$now AND (installation_id IS NULL OR installation_id=$id)",
            ("$id", id.ToString()), ("$hash", hash), ("$now", Now()));
        if (count == 0) return false;
        Execute(db, tx, "INSERT INTO discovery_machines VALUES($id,$encrypted) ON CONFLICT(installation_id) DO UPDATE SET protected_credentials=excluded.protected_credentials", ("$id", id.ToString()), ("$encrypted", encrypted));
        tx.Commit(); return true;
    }

    internal Guid? ConsumeDiscovery(string hash)
    {
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        using var command = Command(db, tx, "SELECT installation_id FROM discovery_challenges WHERE code_hash=$hash AND consumed=0 AND expires_at>$now AND installation_id IS NOT NULL", ("$hash", hash), ("$now", Now()));
        var id = command.ExecuteScalar() as string;
        if (id is null) return null;
        Execute(db, tx, "UPDATE discovery_challenges SET consumed=1 WHERE code_hash=$hash", ("$hash", hash));
        tx.Commit(); return Guid.Parse(id);
    }

    internal void SaveMachine(Guid id, string encrypted)
    {
        using var db = Open();
        Execute(db, null, "INSERT INTO discovery_machines VALUES($id,$encrypted) ON CONFLICT(installation_id) DO UPDATE SET protected_credentials=excluded.protected_credentials", ("$id", id.ToString()), ("$encrypted", encrypted));
    }
    internal string? ReadMachine(Guid id)
    {
        using var db = Open(); using var command = Command(db, null, "SELECT protected_credentials FROM discovery_machines WHERE installation_id=$id", ("$id", id.ToString()));
        return command.ExecuteScalar() as string;
    }
    internal void AddMachineGrant(string hash, Guid id, DateTimeOffset expires)
    {
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        Execute(db, tx, "DELETE FROM discovery_grants WHERE expires_at<$now", ("$now", Now()));
        Execute(db, tx, "INSERT INTO discovery_grants VALUES($hash,$id,$expires)", ("$hash", hash), ("$id", id.ToString()), ("$expires", expires.ToString("O")));
        tx.Commit();
    }
    internal Guid? FindMachineGrant(string hash)
    {
        using var db = Open(); using var command = Command(db, null, "SELECT installation_id FROM discovery_grants WHERE proof_hash=$hash AND expires_at>$now", ("$hash", hash), ("$now", Now()));
        return command.ExecuteScalar() is string id ? Guid.Parse(id) : null;
    }
    internal void RemoveMachineGrant(string hash)
    {
        using var db = Open(); Execute(db, null, "DELETE FROM discovery_grants WHERE proof_hash=$hash", ("$hash", hash));
    }
}
