using Microsoft.Extensions.Logging;

namespace SinergiaAI.PrintAgent;

public sealed partial class PrintAgent
{
    public PrintAgentError? LastError { get; private set; }
    public string? LastErrorMessage => LastError?.Message;
    public string? LastErrorHelp => LastError?.Help;
    public string? LastErrorCode => LastError?.Code;
    public string? LastErrorReferenceId => LastError?.ReferenceId;
    public IReadOnlyList<string> LastErrorSteps => LastError?.ResolutionSteps ?? Array.Empty<string>();

    private void ClearError() => LastError = null;

    private void RecordError(Exception exception)
    {
        var code = PrintAgentErrorCatalog.Classify(exception, exception is BackendInitializationException);
        var sessionFailure = code is PrintAgentErrorCodes.AuthenticationRequired or PrintAgentErrorCodes.UserIdentifierMissing or PrintAgentErrorCodes.AccessDenied;
        if (sessionFailure && LastError?.Code == code) return;
        var error = PrintAgentErrorCatalog.Create(code);
        LastError = error;
        // The full exception stays in the host log. The browser receives only the curated description.
        if (sessionFailure) logger.LogWarning(exception, "PrintAgent failure {ErrorCode}. Reference {ErrorReferenceId}.", error.Code, error.ReferenceId);
        else logger.LogError(exception, "PrintAgent failure {ErrorCode}. Reference {ErrorReferenceId}.", error.Code, error.ReferenceId);
    }

    private void RecordError(string code)
    {
        if (LastError?.Code == code) return; // A repeated pending/connection state keeps one log reference.
        LastError = PrintAgentErrorCatalog.Create(code);
        logger.LogWarning("PrintAgent failure {ErrorCode}. Reference {ErrorReferenceId}.", LastError.Code, LastError.ReferenceId);
    }

    private void RecordDiscoveryError(PrintAgentStatus state, string? browserCode)
    {
        if (state == PrintAgentStatus.Connected) { ClearError(); return; }
        // Only known browser codes are accepted; browser exception text is never displayed or logged here.
        var code = browserCode switch
        {
            PrintAgentErrorCodes.AuthenticationRequired or PrintAgentErrorCodes.AccessDenied
                or PrintAgentErrorCodes.BackendRequestFailed or PrintAgentErrorCodes.MachineRecognitionExpired
                or PrintAgentErrorCodes.PortConflict or PrintAgentErrorCodes.ProtocolIncompatible
                or PrintAgentErrorCodes.AgentConfirmationFailed => browserCode,
            _ => state switch
            {
                PrintAgentStatus.AwaitingApi => PrintAgentErrorCodes.AgentApiPending,
                PrintAgentStatus.Unavailable => PrintAgentErrorCodes.AgentUnreachable,
                PrintAgentStatus.OriginDenied => PrintAgentErrorCodes.OriginDenied,
                PrintAgentStatus.OtherApplication => PrintAgentErrorCodes.AgentUpdateRequired,
                PrintAgentStatus.Incompatible => PrintAgentErrorCodes.ProtocolIncompatible,
                _ => PrintAgentErrorCodes.UnexpectedError
            }
        };
        RecordError(code);
    }
}
