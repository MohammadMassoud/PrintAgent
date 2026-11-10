# Printing errors and administrator recovery

PrintAgent 0.6.2 provides a safe error message, a stable code, numbered recovery steps and a host-log reference. Guidance is included in the NuGet package; no diagnostic endpoint is required. The Windows service does not need an update for this feature.

```csharp
PrintAgent.LastErrorMessage;     // What happened.
PrintAgent.LastErrorCode;        // Stable identifier for translations or application behavior.
PrintAgent.LastErrorHelp;        // Numbered recovery instructions as plain text.
PrintAgent.LastErrorSteps;       // The same instructions as a read-only list.
PrintAgent.LastErrorReferenceId; // Reference to the original exception in the host log.
PrintAgent.LastError;            // Structured PrintAgentError, or null after recovery.
```

Messages and guidance are in English. Use the code to provide application-specific translations. The browser never receives the original exception, stack trace, credentials or arbitrary server/browser exception text through these properties. Server-side exceptions remain in the host log with the same `ErrorReferenceId` shown by the page. Browser discovery states are logged by code and reference; the browser's network tools can provide failed-request details. A caller-requested cancellation is not recorded as a printing failure.

Existing exception behavior is retained. Catch operation exceptions in your component and display the safe properties rather than `exception.ToString()`. Successful discovery and unlink clear stale guidance. These properties describe integration failures; individual print-job results remain available through `GetJobsAsync` and `PrintJob.Error`.

## Display the message and instructions

The consuming component decides how to present the error. For example:

```razor
@using SinergiaAI.PrintAgent
@inject PrintAgent PrintAgent

@if (PrintAgent.LastError is { } error)
{
    <p role="alert">@error.Message</p>
    <details>
        <summary>How to resolve</summary>
        <ol>
            @foreach (var step in error.ResolutionSteps)
            {
                <li>@step</li>
            }
        </ol>
        <small>Code: @error.Code · Reference: @error.ReferenceId</small>
    </details>
}
```

Call `StartAsync` after the first interactive render and catch failures so the component can display its error state. Do not display exception details to unauthenticated visitors. The [standalone demo](../samples/PrintAgent.Demo/README.md) includes the error panel and a simulated troubleshooting example.

## DATA_DIRECTORY_ACCESS_DENIED: IIS permissions

This code means an access-denied exception occurred while initializing the printing data store. Do not reinstall the workstation agent to correct a server storage problem.

1. Find `PrintAgentOptions.DataDirectory` in the deployed application's configuration. A relative path is resolved from the application's content root. For example, `App_Data/PrintAgent` could resolve to `D:\Sites\Example\App_Data\PrintAgent`.
2. On the server, open **IIS Manager > Sites**, select the application and open **Basic Settings**. Note its **Application Pool**. If it is an IIS application below a site, check that application's pool.
3. Open **Application Pools**, select that pool, and open **Advanced Settings > Process Model > Identity**. If it uses `ApplicationPoolIdentity`, its Windows account is `IIS AppPool\<ApplicationPoolName>`. If it uses a custom account, use that account instead.
4. In File Explorer, open the printing data folder's **Properties > Security > Edit > Add**. Select the local server under **Locations**, enter the account from the previous step and use **Check Names**.
5. Grant **Modify** on this data folder, subfolders and files. Existing `printing.db`, WAL and journal files must also be writable. Grant only the required data-folder access; do not grant broad permissions to Everyone or the entire website.
6. Keep this folder persistent when publishing. Refresh the application's printing connection. If the data path or process identity changed, restart the application during an appropriate maintenance window.
7. If your hosting is managed, send the code and reference to the hosting administrator and ask them to perform these steps. Permission belongs to the server process identity, not the signed-in ERP user or the workstation user.

On other hosts, grant the application service account equivalent access to its configured data volume. For `STORAGE_ACCESS_DENIED`, use the referenced exception to identify the specific directory; the denied location can also be the persisted Data Protection key directory.

## DATABASE_READ_ONLY and DATABASE_UNAVAILABLE

1. Check the configured directory and its volume. It must be accessible to the server process and must point to a directory, not a regular file.
2. Check the database file's read-only attribute, the process identity's permissions on the file and its parent directory, and any read-only volume mount.
3. SQLite needs to create companion files beside `printing.db`; allowing writes only to the database file is insufficient.
4. Check disk space and hosting quotas. If these checks do not identify the cause, locate the reference in the host log.
5. Do not delete the database or journal as a workaround. `DATABASE_UNAVAILABLE` identifies an open failure; it does not establish that permissions are the cause.

## SQLite dependencies and architecture

Codes: `SQLITE_DEPENDENCY_MISSING`, `SQLITE_PROVIDER_UNAVAILABLE`, `BACKEND_DEPENDENCY_MISSING`, `BACKEND_ARCHITECTURE_MISMATCH`.

1. Find the original exception by its reference and identify the assembly or native library that failed to load.
2. Determine the server process architecture and the application's publish target: x86, x64 or ARM64. On IIS, check **Application Pools > Advanced Settings > Enable 32-Bit Applications** as well.
3. Choose a publish target compatible with the existing hosting environment. Do not change the production pool's architecture without checking the application's other native dependencies.
4. Restore NuGet dependencies and publish the complete output, including SQLite native runtime assets. Review conflicting SQLitePCLRaw packages if the error concerns provider initialization.
5. Deploy the matching files and restart the application or recycle its pool during maintenance. Preserve the printing data and Data Protection keys. Do not download individual replacement DLLs from untrusted websites.

## Busy, full or damaged databases

| Code | Administrator action |
| --- | --- |
| `DATABASE_BUSY` | Wait briefly; if persistent, investigate other processes, long transactions or hosting instances using the database. |
| `STORAGE_FULL` | Check free disk space and volume quotas; free space without deleting queue or journal files. |
| `DATABASE_DAMAGED` | Stop new submissions, arrange a consistent backup and investigate before using a documented recovery or verified backup. Preserve any associated WAL files. |

A previous submission may have succeeded even when its response failed. Preserve its original bytes, destination and idempotency key. Reconcile job state and actual printer output before deliberately creating another copy. No recovery instruction authorizes automatic physical resubmission.

## Sign-in and browser connection

| Code | What to do |
| --- | --- |
| `AUTHENTICATION_REQUIRED` | Sign in through the application's normal login page, then return to printing. |
| `USER_IDENTIFIER_MISSING` | Ask the developer to include `ClaimTypes.NameIdentifier` in the authenticated principal. |
| `ACCESS_DENIED` | Check the application's account permissions and session. |
| `AGENT_UNREACHABLE` | Check Windows Services, this site's local-network permission and matching bridge ports. A failed fetch does not prove the agent is missing. |
| `LOCAL_ORIGIN_DENIED` | Check the application's HTTPS origin, browser permission and API origin configuration. |
| `AGENT_API_PENDING` / `AGENT_CONFIRMATION_FAILED` | Check API availability, PublicBaseUrl, outbound access, certificate trust, logs and credentials. |
| `LOCAL_PORT_CONFLICT` | Ask the workstation administrator to identify the process using the bridge port; keep the configured ports consistent. |
| `AGENT_PROTOCOL_INCOMPATIBLE` / `AGENT_UPDATE_REQUIRED` | Update the service with the official installer, preserving its configuration and journals. |
| `MACHINE_RECOGNITION_EXPIRED` | Refresh discovery and check the application session. If persistent, check protected application data. |
| `API_URL_INVALID` | Configure the complete HTTPS API URL, including prefix and application path base; exclude credentials, query and fragment. |
| `BROWSER_INTEROP_FAILED` | Start after interactive rendering, check JavaScript/storage availability and the embedded module route. |
| `PROTECTED_DATA_UNAVAILABLE` | Restore the intended Data Protection key persistence/access configuration; do not delete existing keys. |

## Unclassified failures

For `BACKEND_INITIALIZATION_FAILED`, `BACKEND_REQUEST_FAILED` or `UNEXPECTED_ERROR`, the message does not claim a specific root cause. Give the code and reference to the administrator, who can locate `PrintAgent failure` and that reference in the application's normal host log. Read the original exception before deciding which server setting to change.

The UI requires a running application. Startup failures that prevent the entire host from starting must be investigated through the hosting platform's startup diagnostics.
