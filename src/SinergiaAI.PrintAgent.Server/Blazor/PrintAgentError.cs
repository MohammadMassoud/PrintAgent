namespace SinergiaAI.PrintAgent;

/// <summary>Safe, actionable error information. Technical exceptions remain in the host log.</summary>
public sealed record PrintAgentError(string Code, string Message, string Help,
    IReadOnlyList<string> ResolutionSteps, string ReferenceId);

/// <summary>Stable identifiers for application-specific translations and presentation.</summary>
public static class PrintAgentErrorCodes
{
    public const string LocalStorageUnavailable = "LOCAL_STORAGE_UNAVAILABLE";
    public const string DataDirectoryAccessDenied = "DATA_DIRECTORY_ACCESS_DENIED";
    public const string StorageAccessDenied = "STORAGE_ACCESS_DENIED";
    public const string DatabaseUnavailable = "DATABASE_UNAVAILABLE";
    public const string DatabaseReadOnly = "DATABASE_READ_ONLY";
    public const string DatabaseBusy = "DATABASE_BUSY";
    public const string StorageFull = "STORAGE_FULL";
    public const string DatabaseDamaged = "DATABASE_DAMAGED";
    public const string DataDirectoryInvalid = "DATA_DIRECTORY_INVALID";
    public const string SqliteDependencyMissing = "SQLITE_DEPENDENCY_MISSING";
    public const string SqliteProviderUnavailable = "SQLITE_PROVIDER_UNAVAILABLE";
    public const string DependencyMissing = "BACKEND_DEPENDENCY_MISSING";
    public const string ArchitectureMismatch = "BACKEND_ARCHITECTURE_MISMATCH";
    public const string AuthenticationRequired = "AUTHENTICATION_REQUIRED";
    public const string UserIdentifierMissing = "USER_IDENTIFIER_MISSING";
    public const string AccessDenied = "ACCESS_DENIED";
    public const string AgentUnreachable = "AGENT_UNREACHABLE";
    public const string OriginDenied = "LOCAL_ORIGIN_DENIED";
    public const string PortConflict = "LOCAL_PORT_CONFLICT";
    public const string ProtocolIncompatible = "AGENT_PROTOCOL_INCOMPATIBLE";
    public const string AgentUpdateRequired = "AGENT_UPDATE_REQUIRED";
    public const string AgentConfirmationFailed = "AGENT_CONFIRMATION_FAILED";
    public const string AgentApiPending = "AGENT_API_PENDING";
    public const string MachineRecognitionExpired = "MACHINE_RECOGNITION_EXPIRED";
    public const string BackendRequestFailed = "BACKEND_REQUEST_FAILED";
    public const string ApiUrlInvalid = "API_URL_INVALID";
    public const string BrowserInteropFailed = "BROWSER_INTEROP_FAILED";
    public const string ProtectedDataUnavailable = "PROTECTED_DATA_UNAVAILABLE";
    public const string RequestInvalid = "REQUEST_INVALID";
    public const string OperationTimedOut = "OPERATION_TIMED_OUT";
    public const string BackendInitializationFailed = "BACKEND_INITIALIZATION_FAILED";
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
