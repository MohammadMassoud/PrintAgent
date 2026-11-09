using Microsoft.Data.Sqlite; 
using SinergiaAI.PrintAgent.Contracts;
namespace SinergiaAI.PrintAgent.Agent;

public sealed record JournalEntry(Guid JobId, Guid ClaimId, PrintStatus Status, string? Error);
public sealed class AgentJournal
{
    private readonly string _cs;
    public AgentJournal(string dataPath, Guid installationId)
    {
        Directory.CreateDirectory(dataPath);
        _cs = new SqliteConnectionStringBuilder { DataSource = Path.Combine(dataPath, $"journal-{installationId:N}.db") }.ToString();
        using var db = Open(); using var c = db.CreateCommand();
        c.CommandText = """
            PRAGMA journal_mode=WAL;
            CREATE TABLE IF NOT EXISTS journal(id TEXT PRIMARY KEY,claim_id TEXT NOT NULL,status INTEGER NOT NULL,error TEXT,acked INTEGER NOT NULL DEFAULT 0);
            UPDATE journal SET status=4,error='The agent stopped during delivery; check the printer before requesting another copy.' WHERE status=1;
            """; c.ExecuteNonQuery();
    }
    private SqliteConnection Open() { var db = new SqliteConnection(_cs); db.Open(); return db; }
    public void Start(ClaimedPrintJob job)
    {
        using var db = Open(); using var c = db.CreateCommand(); c.CommandText = "INSERT INTO journal(id,claim_id,status) VALUES($id,$claim,1)"; c.Parameters.AddWithValue("$id", job.Id.ToString()); c.Parameters.AddWithValue("$claim", job.ClaimId.ToString()); c.ExecuteNonQuery();
    }
    public void Finish(Guid id, PrintStatus status, string? error)
    {
        using var db = Open(); using var c = db.CreateCommand(); c.CommandText = "UPDATE journal SET status=$status,error=$error WHERE id=$id"; c.Parameters.AddWithValue("$id", id.ToString()); c.Parameters.AddWithValue("$status", (int)status); c.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value); c.ExecuteNonQuery();
    }
    public List<JournalEntry> Pending()
    {
        using var db = Open(); using var c = db.CreateCommand(); c.CommandText = "SELECT id,claim_id,status,error FROM journal WHERE acked=0"; using var rd = c.ExecuteReader(); var entries = new List<JournalEntry>();
        while (rd.Read()) entries.Add(new(Guid.Parse(rd.GetString(0)), Guid.Parse(rd.GetString(1)), (PrintStatus)rd.GetInt32(2), rd.IsDBNull(3) ? null : rd.GetString(3)));
        return entries;
    }
    public void Acknowledge(Guid id)
    {
        using var db = Open(); using var c = db.CreateCommand(); c.CommandText = "UPDATE journal SET acked=1 WHERE id=$id"; c.Parameters.AddWithValue("$id", id.ToString()); c.ExecuteNonQuery();
    }
}
