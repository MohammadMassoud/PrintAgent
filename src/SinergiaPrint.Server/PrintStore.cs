using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SinergiaPrint.Contracts;

namespace SinergiaPrint.Server;

public sealed partial class PrintStore
{
    private readonly string _connectionString;
    public PrintStore(IOptions<PrintServerOptions> options, IHostEnvironment environment)
    {
        var path = Path.GetFullPath(options.Value.DatabasePath, environment.ContentRootPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = path, DefaultTimeout = 15 }.ToString();
        using var db = Open();
        Execute(db, null, """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS installations (id TEXT PRIMARY KEY, name TEXT NOT NULL, token_hash TEXT NOT NULL, last_seen TEXT, printers TEXT NOT NULL DEFAULT '[]');
            CREATE TABLE IF NOT EXISTS jobs (id TEXT PRIMARY KEY, installation_id TEXT NOT NULL, printer_id TEXT NOT NULL, content TEXT NOT NULL, request_key TEXT NOT NULL UNIQUE, status INTEGER NOT NULL, error TEXT, claim_id TEXT, claimed_at TEXT, created_at TEXT NOT NULL, updated_at TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_jobs_pending ON jobs(installation_id,status,created_at);
            CREATE TABLE IF NOT EXISTS enrollments (code_hash TEXT PRIMARY KEY, protected_configuration TEXT NOT NULL, expires_at TEXT NOT NULL, consumed INTEGER NOT NULL DEFAULT 0);
            """);
    }
    public static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string Now() => DateTimeOffset.UtcNow.ToString("O");
    private SqliteConnection Open() { var db = new SqliteConnection(_connectionString); db.Open(); return db; }
    private static SqliteCommand Command(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] args)
    {
        var c = db.CreateCommand(); c.Transaction = tx; c.CommandText = sql;
        foreach (var (name, value) in args) c.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return c;
    }
    private static int Execute(SqliteConnection db, SqliteTransaction? tx, string sql, params (string, object?)[] args)
    { using var c = Command(db, tx, sql, args); return c.ExecuteNonQuery(); }
    public InstallationCredentials CreateInstallation(string name)
    {
        var id = Guid.NewGuid(); var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var db = Open(); Execute(db, null, "INSERT INTO installations(id,name,token_hash) VALUES($id,$name,$hash)", ("$id", id.ToString()), ("$name", name), ("$hash", Hash(token)));
        return new(id, token);
    }
    public bool Authenticate(Guid id, string token)
    {
        using var db = Open(); using var c = Command(db, null, "SELECT token_hash FROM installations WHERE id=$id", ("$id", id.ToString()));
        var expected = c.ExecuteScalar() as string;
        return expected is not null && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(expected), Convert.FromHexString(Hash(token)));
    }
    public List<InstallationInfo> Installations()
    {
        using var db = Open(); using var c = Command(db, null, "SELECT id,name,last_seen,printers FROM installations ORDER BY name"); using var rd = c.ExecuteReader();
        var result = new List<InstallationInfo>();
        while (rd.Read()) result.Add(new(Guid.Parse(rd.GetString(0)), rd.GetString(1), rd.IsDBNull(2) ? null : DateTimeOffset.Parse(rd.GetString(2)), JsonSerializer.Deserialize<List<PrinterInfo>>(rd.GetString(3)) ?? []));
        return result;
    }
    public InstallationInfo? GetInstallation(Guid id)
    {
        using var db = Open();
        using var command = Command(db, null, "SELECT id,name,last_seen,printers FROM installations WHERE id=$id", ("$id", id.ToString()));
        using var reader = command.ExecuteReader();
        return reader.Read() ? new(Guid.Parse(reader.GetString(0)), reader.GetString(1),
            reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)),
            JsonSerializer.Deserialize<List<PrinterInfo>>(reader.GetString(3)) ?? []) : null;
    }

    public void Report(Guid id, IReadOnlyList<PrinterInfo> printers)
    {
        using var db = Open(); Execute(db, null, "UPDATE installations SET printers=$printers,last_seen=$now WHERE id=$id", ("$printers", JsonSerializer.Serialize(printers)), ("$now", Now()), ("$id", id.ToString()));
    }
    private static PrintJobInfo ReadJob(SqliteDataReader rd) => new(Guid.Parse(rd.GetString(0)), Guid.Parse(rd.GetString(1)), rd.GetString(2), (PrintStatus)rd.GetInt32(3), rd.IsDBNull(4) ? null : rd.GetString(4), DateTimeOffset.Parse(rd.GetString(5)), DateTimeOffset.Parse(rd.GetString(6)));
    private static PrintJobInfo? Find(SqliteConnection db, SqliteTransaction? tx, string where, string value)
    {
        using var c = Command(db, tx, $"SELECT id,installation_id,printer_id,status,error,created_at,updated_at FROM jobs WHERE {where}=$value", ("$value", value));
        using var rd = c.ExecuteReader(); return rd.Read() ? ReadJob(rd) : null;
    }
    private static void Expire(SqliteConnection db, SqliteTransaction tx)
    {
        Execute(db, tx, "UPDATE jobs SET status=4,error='Agente sem confirmacao; verifique a impressao antes de reenviar.',updated_at=$now WHERE status=1 AND claimed_at<$cutoff", ("$now", Now()), ("$cutoff", DateTimeOffset.UtcNow.AddMinutes(-2).ToString("O")));
    }
    public PrintJobInfo? GetJob(Guid id)
    {
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false); Expire(db, tx); var job = Find(db, tx, "id", id.ToString()); tx.Commit(); return job;
    }
    public PrintJobInfo Submit(SubmitPrintRequest request)
    {
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        var existing = Find(db, tx, "request_key", request.IdempotencyKey);
        if (existing is not null)
        {
            using var c = Command(db, tx, "SELECT content FROM jobs WHERE id=$id", ("$id", existing.Id.ToString()));
            if (existing.InstallationId != request.InstallationId || existing.PrinterId != request.PrinterId || (string?)c.ExecuteScalar() != request.ContentBase64) throw new InvalidOperationException("Chave de idempotencia ja usada com outro conteudo ou destino.");
            tx.Commit(); return existing;
        }
        var now = Now(); var id = Guid.NewGuid();
        Execute(db, tx, "INSERT INTO jobs(id,installation_id,printer_id,content,request_key,status,created_at,updated_at) VALUES($id,$installation,$printer,$content,$key,0,$now,$now)", ("$id", id.ToString()), ("$installation", request.InstallationId.ToString()), ("$printer", request.PrinterId), ("$content", request.ContentBase64), ("$key", request.IdempotencyKey), ("$now", now));
        tx.Commit(); return new(id, request.InstallationId, request.PrinterId, PrintStatus.Pending, null, DateTimeOffset.Parse(now), DateTimeOffset.Parse(now));
    }
    public ClaimedPrintJob? Claim(Guid installationId)
    {
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false); Expire(db, tx);
        // One job per installation at a time, including when two copies of the agent run.
        using var c = Command(db, tx, "SELECT id,printer_id,content FROM jobs WHERE installation_id=$installation AND status=0 AND NOT EXISTS(SELECT 1 FROM jobs WHERE installation_id=$installation AND status=1) ORDER BY created_at LIMIT 1", ("$installation", installationId.ToString()));
        ClaimedPrintJob? job;
        using (var rd = c.ExecuteReader()) job = rd.Read() ? new(Guid.Parse(rd.GetString(0)), Guid.NewGuid(), rd.GetString(1), rd.GetString(2)) : null;
        if (job is not null) Execute(db, tx, "UPDATE jobs SET status=1,claim_id=$claim,claimed_at=$now,updated_at=$now WHERE id=$id", ("$claim", job.ClaimId.ToString()), ("$now", Now()), ("$id", job.Id.ToString()));
        Execute(db, tx, "UPDATE installations SET last_seen=$now WHERE id=$id", ("$now", Now()), ("$id", installationId.ToString()));
        tx.Commit(); return job;
    }
    public bool Complete(Guid installationId, Guid jobId, CompletePrintRequest request)
    {
        using var db = Open(); using var tx = db.BeginTransaction(deferred: false);
        using var c = Command(db, tx, "SELECT status,error FROM jobs WHERE id=$id AND installation_id=$installation AND claim_id=$claim", ("$id", jobId.ToString()), ("$installation", installationId.ToString()), ("$claim", request.ClaimId.ToString()));
        int current;
        using (var rd = c.ExecuteReader()) { if (!rd.Read()) return false; current = rd.GetInt32(0); }
        // Repeated acknowledgements are idempotent; definitive results cannot be overwritten.
        if (current is 2 or 3) { tx.Commit(); return current == (int)request.Status; }
        Execute(db, tx, "UPDATE jobs SET status=$status,error=$error,updated_at=$now WHERE id=$id", ("$status", (int)request.Status), ("$error", request.Error), ("$now", Now()), ("$id", jobId.ToString()));
        tx.Commit(); return true;
    }
}
