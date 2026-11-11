namespace SinergiaAI.PrintAgent;

/// <summary>Safe, actionable error information. Technical exceptions remain in the host log.</summary>
/// <param name="Code">Stable code for application-specific handling and translation.</param>
/// <param name="Message">A safe error description suitable for display.</param>
/// <param name="Help">Additional context for resolving the error.</param>
/// <param name="ResolutionSteps">Ordered recovery instructions.</param>
/// <param name="ReferenceId">The reference that correlates with the application's technical log.</param>
public sealed record PrintAgentError(string Code, string Message, string Help,
    IReadOnlyList<string> ResolutionSteps, string ReferenceId);

/// <summary>Stable identifiers for application-specific translations and presentation.</summary>
public static class PrintAgentErrorCodes
{
    /// <summary>The workstation cannot access its durable local queue.</summary>
    public const string LocalStorageUnavailable = "LOCAL_STORAGE_UNAVAILABLE";
    /// <summary>The legacy backend cannot write its data directory.</summary>
    public const string DataDirectoryAccessDenied = "DATA_DIRECTORY_ACCESS_DENIED";
    /// <summary>Access to required storage was denied.</summary>
    public const string StorageAccessDenied = "STORAGE_ACCESS_DENIED";
    /// <summary>The legacy database could not be opened.</summary>
    public const string DatabaseUnavailable = "DATABASE_UNAVAILABLE";
    /// <summary>The legacy database is read-only.</summary>
    public const string DatabaseReadOnly = "DATABASE_READ_ONLY";
    /// <summary>The legacy database is locked or busy.</summary>
    public const string DatabaseBusy = "DATABASE_BUSY";
    /// <summary>The required storage is full.</summary>
    public const string StorageFull = "STORAGE_FULL";
    /// <summary>The legacy database is invalid or damaged.</summary>
    public const string DatabaseDamaged = "DATABASE_DAMAGED";
    /// <summary>The legacy data-directory configuration is invalid.</summary>
    public const string DataDirectoryInvalid = "DATA_DIRECTORY_INVALID";
    /// <summary>A required SQLite native dependency is missing.</summary>
    public const string SqliteDependencyMissing = "SQLITE_DEPENDENCY_MISSING";
    /// <summary>A required SQLite provider was not initialized.</summary>
    public const string SqliteProviderUnavailable = "SQLITE_PROVIDER_UNAVAILABLE";
    /// <summary>A required backend dependency is missing.</summary>
    public const string DependencyMissing = "BACKEND_DEPENDENCY_MISSING";
    /// <summary>A backend dependency targets an incompatible architecture.</summary>
    public const string ArchitectureMismatch = "BACKEND_ARCHITECTURE_MISMATCH";
    /// <summary>The legacy application session requires authentication.</summary>
    public const string AuthenticationRequired = "AUTHENTICATION_REQUIRED";
    /// <summary>The legacy session has no required user identifier.</summary>
    public const string UserIdentifierMissing = "USER_IDENTIFIER_MISSING";
    /// <summary>The application denied the operation.</summary>
    public const string AccessDenied = "ACCESS_DENIED";
    /// <summary>The browser cannot reach the workstation agent.</summary>
    public const string AgentUnreachable = "AGENT_UNREACHABLE";
    /// <summary>The website origin is not authorized on this workstation.</summary>
    public const string OriginDenied = "LOCAL_ORIGIN_DENIED";
    /// <summary>The configured local bridge port is already in use.</summary>
    public const string PortConflict = "LOCAL_PORT_CONFLICT";
    /// <summary>The agent protocol is incompatible with the integration.</summary>
    public const string ProtocolIncompatible = "AGENT_PROTOCOL_INCOMPATIBLE";
    /// <summary>The Windows agent must be updated.</summary>
    public const string AgentUpdateRequired = "AGENT_UPDATE_REQUIRED";
    /// <summary>The legacy agent confirmation failed.</summary>
    public const string AgentConfirmationFailed = "AGENT_CONFIRMATION_FAILED";
    /// <summary>The legacy agent API connection is pending.</summary>
    public const string AgentApiPending = "AGENT_API_PENDING";
    /// <summary>The legacy workstation recognition proof expired.</summary>
    public const string MachineRecognitionExpired = "MACHINE_RECOGNITION_EXPIRED";
    /// <summary>A legacy backend request failed.</summary>
    public const string BackendRequestFailed = "BACKEND_REQUEST_FAILED";
    /// <summary>A legacy API URL is invalid.</summary>
    public const string ApiUrlInvalid = "API_URL_INVALID";
    /// <summary>Browser JavaScript interop failed.</summary>
    public const string BrowserInteropFailed = "BROWSER_INTEROP_FAILED";
    /// <summary>Legacy protected data could not be read.</summary>
    public const string ProtectedDataUnavailable = "PROTECTED_DATA_UNAVAILABLE";
    /// <summary>The destination, key, content, encoding or payload limits are invalid.</summary>
    public const string RequestInvalid = "REQUEST_INVALID";
    /// <summary>The operation timed out; submission may already have been accepted.</summary>
    public const string OperationTimedOut = "OPERATION_TIMED_OUT";
    /// <summary>The legacy backend could not be initialized.</summary>
    public const string BackendInitializationFailed = "BACKEND_INITIALIZATION_FAILED";
    /// <summary>An unexpected error occurred; inspect the correlated host log.</summary>
    public const string UnexpectedError = "UNEXPECTED_ERROR";
}

internal sealed class PrintAgentOperationException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

internal sealed class PrintAgentAuthenticationException(string code, string message) : UnauthorizedAccessException(message)
{
    public string Code { get; } = code;
}
