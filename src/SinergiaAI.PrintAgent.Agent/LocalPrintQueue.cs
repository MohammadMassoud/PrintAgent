using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;

namespace SinergiaAI.PrintAgent.Agent;

public sealed record LocalDocument(string PrinterId, string ContentBase64, string IdempotencyKey);
public sealed record LocalJob(Guid Id, Guid InstallationId, string PrinterId, int Status, string? Error, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record LocalDelivery(Guid Id, string PrinterId, byte[] Content);

/// <summary>Machine-wide durable FIFO. A committed delivery intent is never automatically replayed.</summary>
public sealed class LocalPrintQueue
{
    private readonly string _connection;
    private readonly object _gate = new();
    public event Action<string, LocalJob>? JobChanged;
    public Guid MachineId { get; }
    public SemaphoreSlim Wake { get; } = new(0, 1);
    public LocalPrintQueue(AgentOptions options)
    {
        Directory.CreateDirectory(options.DataPath);
        _connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(options.DataPath, "local-queue.db"), DefaultTimeout = 10 }.ToString();
        using var db = Open();
        using var command = db.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS settings(name TEXT PRIMARY KEY,value TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS origins(origin TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS jobs(seq INTEGER PRIMARY KEY AUTOINCREMENT,id TEXT UNIQUE NOT NULL,origin TEXT NOT NULL,request_key TEXT NOT NULL,printer TEXT NOT NULL,hash TEXT NOT NULL,content BLOB,status INTEGER NOT NULL,error TEXT,created TEXT NOT NULL,updated TEXT NOT NULL,UNIQUE(origin,request_key));
            CREATE INDEX IF NOT EXISTS queue_pending ON jobs(status,seq);
            UPDATE jobs SET status=4,content=NULL,error='The agent stopped during delivery. Check the printer before requesting another copy.' WHERE status=1;
            """;
        command.ExecuteNonQuery();
        command.CommandText = "INSERT OR IGNORE INTO settings VALUES('machine',$id)";
        command.Parameters.AddWithValue("$id", Guid.NewGuid().ToString()); command.ExecuteNonQuery();
        command.CommandText = "SELECT value FROM settings WHERE name='machine'";
        MachineId = Guid.Parse((string)command.ExecuteScalar()!);
        foreach (var origin in options.AllowedOrigins) ApproveOrigin(origin);
    }
    private SqliteConnection Open()
    {
        var db = new SqliteConnection(_connection); db.Open();
        using var c = db.CreateCommand(); c.CommandText = "PRAGMA synchronous=FULL; PRAGMA busy_timeout=10000;"; c.ExecuteNonQuery(); return db;
    }
    public static string NormalizeOrigin(string origin)
    {
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var u) || u.UserInfo.Length != 0 || u.AbsolutePath != "/" || u.Query.Length != 0 || u.Fragment.Length != 0
            || u.Scheme != "https" && !(u.Scheme == "http" && u.IsLoopback)) throw new ArgumentException("Use an HTTPS website origin (HTTP is allowed only on loopback).");
        return u.GetLeftPart(UriPartial.Authority).ToLowerInvariant();
    }
    public bool IsApproved(string origin)
    {
        lock (_gate) { using var db = Open(); using var c = db.CreateCommand(); c.CommandText = "SELECT COUNT(*) FROM origins WHERE origin=$o"; c.Parameters.AddWithValue("$o", NormalizeOrigin(origin)); return (long)c.ExecuteScalar()! != 0; }
    }
    public string[] ListOrigins()
    {
        lock (_gate)
        {
            using var db = Open(); using var c = db.CreateCommand(); c.CommandText = "SELECT origin FROM origins ORDER BY origin";
            using var reader = c.ExecuteReader(); var origins = new List<string>();
            while (reader.Read()) origins.Add(reader.GetString(0)); return origins.ToArray();
        }
    }
    public void ApproveOrigin(string origin) => SetOrigin(origin, true);
    public void SetOrigin(string origin, bool approved)
    {
        lock (_gate) { using var db = Open(); using var c = db.CreateCommand(); c.CommandText = approved ? "INSERT OR IGNORE INTO origins VALUES($o)" : "DELETE FROM origins WHERE origin=$o"; c.Parameters.AddWithValue("$o", NormalizeOrigin(origin)); c.ExecuteNonQuery(); }
    }
    public IReadOnlyList<LocalJob> Enqueue(string origin, IReadOnlyList<LocalDocument> documents, IReadOnlyDictionary<string,string> printers)
    {
        origin = NormalizeOrigin(origin);
        if (documents.Count is < 1 or > 5000) throw new ArgumentException("Submit between 1 and 5000 documents.");
        var prepared = documents.Select(d => {
            if (string.IsNullOrWhiteSpace(d.IdempotencyKey) || d.IdempotencyKey.Length > 200 || !printers.ContainsKey(d.PrinterId)) throw new ArgumentException("Select an available printer and a request key of up to 200 characters.");
            var bytes = Convert.FromBase64String(d.ContentBase64);
            if (bytes.Length is < 1 or > 2 * 1024 * 1024) throw new ArgumentException("Each document must contain 1 byte to 2 MiB.");
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            return (Document: d, Bytes: bytes, Hash: hash);
        }).ToArray();
        if (prepared.Sum(d => (long)d.Bytes.Length) > 4 * 1024 * 1024) throw new ArgumentException("A batch may contain at most 4 MiB of RAW data.");
        lock (_gate)
        {
            using var db = Open(); using var tx = db.BeginTransaction();
            var result = new List<LocalJob>();
            foreach (var item in prepared)
            {
                using var c = db.CreateCommand(); c.Transaction = tx;
                c.CommandText = "SELECT id,printer,status,error,created,updated,hash FROM jobs WHERE origin=$o AND request_key=$k";
                c.Parameters.AddWithValue("$o", origin); c.Parameters.AddWithValue("$k", item.Document.IdempotencyKey);
                using (var r = c.ExecuteReader())
                {
                    if (r.Read())
                    {
                        if (r.GetString(1) != item.Document.PrinterId || r.GetString(6) != item.Hash) throw new ArgumentException("This request key already belongs to different bytes or a different printer.");
                        result.Add(ReadJob(r)); continue;
                    }
                }
                var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow;
                c.CommandText = "INSERT INTO jobs(id,origin,request_key,printer,hash,content,status,created,updated) VALUES($id,$o,$k,$p,$h,$b,0,$t,$t)";
                c.Parameters.AddWithValue("$id", id.ToString()); c.Parameters.AddWithValue("$p", item.Document.PrinterId); c.Parameters.AddWithValue("$h", item.Hash); c.Parameters.AddWithValue("$b", item.Bytes); c.Parameters.AddWithValue("$t", now.ToString("O")); c.ExecuteNonQuery();
                result.Add(new(id, MachineId, item.Document.PrinterId, 0, null, now, now));
            }
            using (var quota = db.CreateCommand())
            {
                quota.Transaction = tx; quota.CommandText = "SELECT COALESCE(SUM(length(content)),0) FROM jobs WHERE status IN(0,1)";
                if ((long)quota.ExecuteScalar()! > 256 * 1024 * 1024) throw new InvalidOperationException("The local pending queue is full. Wait for existing jobs to finish.");
            }
            tx.Commit();
            if (Wake.CurrentCount == 0) Wake.Release();
            return result;
        }
    }
    private LocalJob ReadJob(SqliteDataReader r) => new(Guid.Parse(r.GetString(0)), MachineId, r.GetString(1), r.GetInt32(2), r.IsDBNull(3) ? null : r.GetString(3), DateTimeOffset.Parse(r.GetString(4)), DateTimeOffset.Parse(r.GetString(5)));
    public IReadOnlyList<LocalJob> GetJobs(string origin, Guid[] ids)
    {
        if (ids.Length > 5000) throw new ArgumentException("Query at most 5000 jobs.");
        lock (_gate)
        {
            using var db = Open(); var results = new List<LocalJob>();
            foreach (var id in ids)
            {
                using var c = db.CreateCommand(); c.CommandText = "SELECT id,printer,status,error,created,updated FROM jobs WHERE id=$id AND origin=$o";
                c.Parameters.AddWithValue("$id", id.ToString()); c.Parameters.AddWithValue("$o", NormalizeOrigin(origin)); using var r = c.ExecuteReader(); if (r.Read()) results.Add(ReadJob(r));
            }
            return results;
        }
    }
    public LocalDelivery? BeginDelivery()
    {
        lock (_gate)
        {
            using var db = Open(); using var tx = db.BeginTransaction(); using var c = db.CreateCommand(); c.Transaction = tx;
            c.CommandText = "SELECT id,printer,content FROM jobs WHERE status=0 ORDER BY seq LIMIT 1";
            LocalDelivery job;
            using (var r = c.ExecuteReader()) { if (!r.Read()) return null; job = new(Guid.Parse(r.GetString(0)), r.GetString(1), (byte[])r[2]); }
            c.CommandText = "UPDATE jobs SET status=1,updated=$t WHERE id=$id AND status=0"; c.Parameters.AddWithValue("$id", job.Id.ToString()); c.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O")); c.ExecuteNonQuery();
            tx.Commit(); return job;
        }
    }
    public void Finish(Guid id, int status, string? error)
    {
        lock (_gate)
        {
            using var db = Open(); using var c = db.CreateCommand(); c.CommandText = "UPDATE jobs SET status=$s,error=$e,content=NULL,updated=$t WHERE id=$id AND status=1";
            c.Parameters.AddWithValue("$s", status); c.Parameters.AddWithValue("$e", (object?)error ?? DBNull.Value); c.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O")); c.Parameters.AddWithValue("$id", id.ToString()); c.ExecuteNonQuery();
            c.CommandText = "SELECT id,printer,status,error,created,updated,origin FROM jobs WHERE id=$id";
            using var reader = c.ExecuteReader();
            if (reader.Read()) JobChanged?.Invoke(reader.GetString(6), ReadJob(reader));
        }
    }
}
