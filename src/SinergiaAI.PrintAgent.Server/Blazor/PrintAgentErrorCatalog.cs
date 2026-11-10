using System.Security.Cryptography;
using Microsoft.JSInterop;
using static SinergiaAI.PrintAgent.PrintAgentErrorCodes;

namespace SinergiaAI.PrintAgent;

internal static class PrintAgentErrorCatalog
{
    internal static PrintAgentError Create(string code)
    {
        var (message, steps) = Describe(code);
        return new(code, message, string.Join(Environment.NewLine, steps.Select((s, i) => $"{i + 1}. {s}")),
            Array.AsReadOnly(steps), Guid.NewGuid().ToString("N")[..12]);
    }

    internal static string Classify(Exception error, bool initializing)
    {
        // Inspect wrappers without returning their messages or paths to the browser.
        var errors = Flatten(error).ToArray();
        foreach (var ex in errors)
        {
            if (ex is PrintAgentAuthenticationException authentication) return authentication.Code;
            if (ex is PrintAgentOperationException operation) return operation.Code;
        }
        foreach (var ex in errors)
        {
            if (ex is BadImageFormatException) return ArchitectureMismatch;
            if (ex is DllNotFoundException or FileNotFoundException or FileLoadException)
            {
                var name = ex is FileNotFoundException file ? file.FileName ?? ex.Message : ex is FileLoadException load ? load.FileName ?? ex.Message : ex.Message;
                if (SqliteName(name)) return SqliteDependencyMissing;
                if (initializing) return DependencyMissing;
            }
        }
        foreach (var ex in errors)
        {
            // Reflection keeps classification usable even when Microsoft.Data.Sqlite itself cannot load.
            if (ex.GetType().FullName == "Microsoft.Data.Sqlite.SqliteException"
                && ex.GetType().GetProperty("SqliteErrorCode")?.GetValue(ex) is int sqliteCode)
                return sqliteCode switch
                {
                    5 or 6 => DatabaseBusy, 8 => DatabaseReadOnly, 13 => StorageFull,
                    14 => DatabaseUnavailable, 11 or 26 => DatabaseDamaged,
                    _ => initializing ? BackendInitializationFailed : UnexpectedError
                };
        }
        foreach (var ex in errors)
        {
            if (ex is TimeoutException or OperationCanceledException) return OperationTimedOut;
            if (ex is UnauthorizedAccessException)
                return initializing ? errors.Any(e => e.StackTrace?.Contains("SinergiaAI.PrintAgent.Server.PrintStore", StringComparison.Ordinal) == true)
                    ? DataDirectoryAccessDenied : StorageAccessDenied : AccessDenied;
            if (ex is PathTooLongException or DirectoryNotFoundException) return DataDirectoryInvalid;
            if (ex is IOException && initializing && ((ex.HResult & 0xffff) is 80 or 183)) return DataDirectoryInvalid;
            if (ex is CryptographicException) return ProtectedDataUnavailable;
            if (ex is JSException or JSDisconnectedException) return BrowserInteropFailed;
            if (ex is InvalidOperationException && ex.Message.Contains("SQLitePCL.raw.SetProvider", StringComparison.Ordinal)) return SqliteProviderUnavailable;
            if (ex is ArgumentException && !initializing) return RequestInvalid;
        }
        return initializing ? BackendInitializationFailed : UnexpectedError;
    }

    private static bool SqliteName(string? value) => value?.Contains("sqlite", StringComparison.OrdinalIgnoreCase) == true;
    private static IEnumerable<Exception> Flatten(Exception error)
    {
        yield return error;
        if (error is AggregateException aggregate)
        {
            foreach (var child in aggregate.InnerExceptions)
                foreach (var inner in Flatten(child)) yield return inner;
        }
        else if (error.InnerException is { } cause)
            foreach (var inner in Flatten(cause)) yield return inner;
    }

    private static (string Message, string[] Steps) Describe(string code) => code switch
    {
        DataDirectoryAccessDenied => ("The server application does not have permission to write printing data.", [
            "Find the configured PrintAgentOptions.DataDirectory folder in the application's server configuration.",
            "On IIS, identify the application's Application Pool and its process identity. For ApplicationPoolIdentity, use IIS AppPool\\<ApplicationPoolName>.",
            "Ask the server administrator to grant that identity Modify permission on the printing data folder, including existing database files and inherited child files. Other hosting platforms must grant their service account equivalent read/write access.",
            "Keep the folder persistent across deployments. Refresh the printing page after the permission change. If hosting is managed, send the error code and reference to the hosting administrator." ]),
        StorageAccessDenied => ("The server cannot access storage required by the printing backend.", [
            "Ask the administrator to locate this error reference in the application host log to identify the denied file or folder.",
            "Check the configured printing data folder and the persisted ASP.NET Core Data Protection key directory.",
            "Grant the application process identity the required access only to those directories, then refresh the page. Do not delete keys or printing data." ]),
        DatabaseReadOnly => ("The server can open the printing database, but cannot write to it.", [
            "Locate the printing.db file in the configured printing data directory.",
            "Ask the administrator to check the file's read-only attribute and the application identity's Modify permission on both the file and its parent folder. SQLite also needs to create journal/WAL files beside the database.",
            "Check whether the hosting volume is mounted read-only. Correct its access settings, then refresh the printing page." ]),
        DatabaseUnavailable => ("The server could not open the printing database.", [
            "Verify that the configured data path exists or can be created by the application identity, and that it points to a folder rather than a file.",
            "Check read/write access to printing.db and its parent folder, available disk space, and availability of the hosting volume.",
            "If the path and access are correct, ask the administrator to search the host log using this reference. Do not delete or replace the database as a troubleshooting shortcut." ]),
        DatabaseBusy => ("The printing database is busy or locked.", [
            "Wait briefly, then refresh the connection or job status.",
            "If the problem continues, ask the administrator to check other processes, long-running transactions and hosting instances using the same SQLite database.",
            "If a submission may have succeeded, retry only with its original destination, bytes and idempotency key. Never create replacement keys automatically." ]),
        StorageFull => ("The server does not have enough storage for printing data.", [
            "Ask the administrator to check free space and quotas on the volume containing the printing database.",
            "Free space or increase the quota without deleting printing databases, WAL files or agent journals.",
            "Refresh the page. Preserve original idempotency keys if a previous submission has an uncertain outcome." ]),
        DatabaseDamaged => ("The printing database is damaged or is not a valid SQLite database.", [
            "Stop submitting new print requests and ask the administrator to investigate the referenced host log entry.",
            "Arrange a consistent backup before attempting repair, preserving the database and any associated WAL files. Do not delete the queue or the agent journal.",
            "Restore a verified backup or use a documented recovery procedure during maintenance. Reconcile uncertain jobs with actual printer output before deliberately requesting new copies." ]),
        DataDirectoryInvalid => ("The printing data directory is invalid or unavailable.", [
            "Review PrintAgentOptions.DataDirectory. Use a valid directory path for the server's operating system.",
            "Ensure no regular file occupies the directory path, the volume is available, and the application identity can create or access the directory.",
            "Keep the existing database and Data Protection keys when correcting the path, then restart the application if its configuration changed." ]),
        SqliteDependencyMissing or DependencyMissing or ArchitectureMismatch or SqliteProviderUnavailable => DependencyHelp(code),
        AuthenticationRequired => ("Sign in to the application before using printing.", [
            "Sign in through the application's normal login page.",
            "Return to the printing page and refresh the connection. An expired application session does not mean the Windows agent is missing." ]),
        UserIdentifierMissing => ("The signed-in application user has no identifier required by printing.", [
            "Ask the application administrator to check its authentication configuration.",
            "The authenticated principal must include ClaimTypes.NameIdentifier. Correct the claim mapping and sign in again." ]),
        AccessDenied => ("The application did not authorize this printing operation.", [
            "Check that your application session is still signed in.",
            "Ask the application administrator to check printing permissions for your account, then refresh the connection." ]),
        AgentUnreachable => ("The browser could not reach the local Windows agent.", [
            "On this computer, open Windows Services and check that SinergiaPrint Agent is running. Install the agent only if it is not installed.",
            "Check this site's local-network access permission in the browser. A failed browser request can mean a stopped service, a blocked permission or an unavailable port; it does not prove the agent is missing.",
            "Check that the application's LocalBridgePort matches the agent's port (17831 by default), then refresh the connection." ]),
        LocalStorageUnavailable => ("The Windows agent could not access its local queue.", [
            "Check free disk space and the PrintAgent Windows service account's access to its ProgramData folder.",
            "Preserve local-queue.db, WAL files, legacy journals and configuration. Do not delete the queue to resolve a permission issue.",
            "Ask the workstation administrator to check the Windows Application log. No printing database or folder permission is required on the application server." ]),
        OriginDenied => ("This website has not been authorized to use the local agent.", [
            "Open SinergiaAI PrintAgent from the Windows Start menu if its tray app is not running, then refresh the application connection.",
            "Verify the exact website address shown in that dialog before approving. Authorization is shared by all Windows users and browser profiles on this computer.",
            "StartAsync requests the native permission dialog automatically. If denied or timed out, refresh when ready. Browser local-network permission may also be required." ]),
        PortConflict or ProtocolIncompatible or AgentUpdateRequired => (code == PortConflict ? "Another service is responding on the local agent port." : "The installed agent must be updated for this application's integration.", [
            "Compare the installed agent and integration versions shown by the application.",
            "Update using the application's official installer. Preserve the existing configuration and journals.",
            "For a port conflict, ask the workstation administrator to identify the process using the configured bridge port. Keep the service and application's LocalBridgePort settings consistent, then refresh the page." ]),
        AgentApiPending or AgentConfirmationFailed => (code == AgentApiPending ? "The local agent was found, but its API connection is not ready." : "The local agent was found, but could not confirm its connection with the application's API.", [
            "Keep the application API running and wait briefly before refreshing the connection.",
            "Ask the administrator to verify PublicBaseUrl, outbound HTTPS access, server certificate trust and any proxy or firewall between the Windows service and the API.",
            "Check the service and API logs for rejected credentials or server errors. For local development, ensure the configured API port is actually listening. Reinstalling alone does not correct an API or server failure." ]),
        MachineRecognitionExpired => ("This computer's application recognition expired or is no longer valid.", [
            "Refresh the connection to perform machine discovery again.",
            "If your session expired, sign in again. If recognition still fails, ask the administrator to check persistent application data and Data Protection keys." ]),
        ApiUrlInvalid => ("The application's public printing API URL is invalid.", [
            "Set PrintAgentOptions.PublicBaseUrl to the full HTTPS printing API URL, including its configured prefix and any application path base.",
            "Do not include credentials, query parameters or fragments. HTTP is allowed only for loopback development.",
            "Publish the corrected application configuration and refresh the printing connection." ]),
        BrowserInteropFailed => ("The printing integration could not complete its browser operation.", [
            "Call StartAsync after the first interactive component render, not during static prerendering.",
            "Check that JavaScript and this site's browser storage are available, and that MapPrintAgent serves its embedded browser module.",
            "Reload the page. If the issue continues, ask the administrator to check browser errors and the referenced host log entry." ]),
        ProtectedDataUnavailable => ("Protected printing data could not be read or written on the server.", [
            "Verify that ASP.NET Core Data Protection keys are persisted across deployments and accessible to the application identity.",
            "Keep the application's Data Protection name and key location consistent. Check disk access and encryption/certificate settings in the host log.",
            "Do not delete keys, credentials, queues or journals. Ask the administrator to restore the intended key configuration before reconnecting." ]),
        RequestInvalid => ("The printing request contains invalid arguments.", [
            "Check the printer ID, non-empty RAW content and documented batch and payload limits.",
            "Each document needs its own stable idempotency key. Reuse the original key, bytes and destination only when retrying the same request; do not change keys to work around a submission error." ]),
        OperationTimedOut => ("The printing operation did not complete within its allowed time.", [
            "Check the browser connection and local service availability, then refresh the connection or query job status.",
            "A timed-out submission may have succeeded. Reuse its original idempotency key, destination and bytes if retrying that same request; inspect the printer before requesting a new copy." ]),
        BackendRequestFailed => ("The application's printing API returned an error.", [
            "Ask the administrator to check the API endpoint, server health and application host log.",
            "Confirm PublicBaseUrl and the mapped printing prefix match the deployed application. A server error does not establish that the workstation agent is missing." ]),
        _ => (code == BackendInitializationFailed ? "The printing backend could not be initialized on the server." : "The printing operation could not be completed.", [
            "Send this error code and reference to the application administrator.",
            "Ask the administrator to locate the reference in the application's host log and inspect the original exception before choosing a correction.",
            "Preserve printing data and journals. If a submission's outcome is uncertain, check its job status and the printer before requesting another copy." ])
    };

    private static (string, string[]) DependencyHelp(string code) => (
        code switch
        {
            SqliteDependencyMissing => "A SQLite dependency is missing or could not be loaded on the server.",
            ArchitectureMismatch => "A printing backend dependency is invalid or incompatible with the server process architecture.",
            SqliteProviderUnavailable => "The SQLite provider was not initialized or has conflicting dependencies.",
            _ => "A required printing backend dependency could not be loaded on the server."
        }, [
            "Ask the administrator to inspect the referenced exception to identify the missing, incompatible or conflicting assembly/native library.",
            "Compare the server process architecture (x86, x64 or ARM64), operating system and publish target. On IIS, also check the Application Pool's Enable 32-Bit Applications setting.",
            "Restore the application's NuGet dependencies and publish the complete output, including SQLite native runtime assets. Resolve conflicting SQLitePCLRaw packages; do not download individual DLLs from third-party sites.",
            "Deploy the matching publish output and restart the application or recycle its pool during an appropriate maintenance window, then refresh the printing page. Preserve the data directory and Data Protection keys." ]);
}
