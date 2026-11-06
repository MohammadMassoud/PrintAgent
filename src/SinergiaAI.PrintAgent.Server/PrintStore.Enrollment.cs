using Microsoft.Data.Sqlite;

namespace SinergiaAI.PrintAgent.Server;

public sealed partial class PrintStore
{
    internal void SaveEnrollment(string codeHash, string protectedConfiguration, DateTimeOffset expiresAt)
    {
        using var db = Open();
        using var transaction = db.BeginTransaction(deferred: false);
        Execute(db, transaction, "DELETE FROM enrollments WHERE expires_at < $now OR consumed = 1", ("$now", Now()));
        Execute(db, transaction,
            "INSERT INTO enrollments(code_hash,protected_configuration,expires_at) VALUES($hash,$configuration,$expires)",
            ("$hash", codeHash), ("$configuration", protectedConfiguration), ("$expires", expiresAt.ToString("O")));
        transaction.Commit();
    }

    internal string? ConsumeEnrollment(string codeHash)
    {
        using var db = Open();
        // Serialize concurrent redemptions. Only one process can consume this code.
        using var transaction = db.BeginTransaction(deferred: false);
        using var command = Command(db, transaction,
            "SELECT protected_configuration FROM enrollments WHERE code_hash=$hash AND consumed=0 AND expires_at>$now",
            ("$hash", codeHash), ("$now", Now()));
        var configuration = command.ExecuteScalar() as string;
        if (configuration is null) return null;
        Execute(db, transaction, "UPDATE enrollments SET consumed=1 WHERE code_hash=$hash", ("$hash", codeHash));
        transaction.Commit();
        return configuration;
    }
}
