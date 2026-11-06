namespace SinergiaPrint.Contracts;

public record PrinterInfo(string Id, string Name, bool IsDefault);
public record InstallationInfo(Guid Id, string Name, DateTimeOffset? LastSeenAt, IReadOnlyList<PrinterInfo> Printers);
public record CreateInstallationRequest(string Name);
public record InstallationCredentials(Guid InstallationId, string AgentToken);
public record SubmitPrintRequest(Guid InstallationId, string PrinterId, string ContentBase64, string IdempotencyKey);
public enum PrintStatus { Pending, Claimed, Sent, Failed, Unknown }
public record PrintJobInfo(Guid Id, Guid InstallationId, string PrinterId, PrintStatus Status, string? Error, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public record ClaimedPrintJob(Guid Id, Guid ClaimId, string PrinterId, string ContentBase64);
public record ReportPrintersRequest(IReadOnlyList<PrinterInfo> Printers);
public record CompletePrintRequest(Guid ClaimId, PrintStatus Status, string? Error);
public record EnrollmentCodeRequest(string Code);
public record AgentSetupConfiguration(string ApiBaseUrl, Guid InstallationId, string Token);
