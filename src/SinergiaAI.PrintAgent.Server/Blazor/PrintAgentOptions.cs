namespace SinergiaAI.PrintAgent;

public sealed class PrintAgentOptions
{
    // Must match the Windows installer pinned by the bootstrap shipped in this package.
    public string AvailableAgentVersion { get; set; } = "0.4.0";
    public int LocalBridgePort { get; set; } = 17831;
    public string ApiPrefix { get; set; } = "/printing";
    public string DataDirectory { get; set; } = "App_Data/PrintAgent";
    public string? PublicBaseUrl { get; set; }
    public string BrowserStoragePrefix { get; set; } = "PrintAgent.";
    public string LegacyProtectionPurpose { get; set; } = "PrintAgent.Connection.v1";
}
