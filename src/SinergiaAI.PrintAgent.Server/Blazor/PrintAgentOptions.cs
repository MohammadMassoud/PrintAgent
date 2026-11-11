using System.Text;

namespace SinergiaAI.PrintAgent;

/// <summary>Configures the browser bridge, text encoding and Windows installer for a Blazor Server application.</summary>
public sealed class PrintAgentOptions
{
    /// <summary>Gets or sets the default text encoding. UTF-8 is used unless overridden; unrepresentable characters are rejected.</summary>
    /// <remarks>The printer must understand the selected encoding. No BOM, line endings or printer commands are added.</remarks>
    public Encoding TextEncoding { get; set; } = new UTF8Encoding(false, true);
    // Must match the Windows installer pinned by the bootstrap shipped in this package.
    /// <summary>Gets or sets the Windows agent version offered for download.</summary>
    public string AvailableAgentVersion { get; set; } = "2.0.0";
    /// <summary>Gets or sets the HTTPS URL of the generic Windows installer.</summary>
    public string InstallerUrl { get; set; } = "https://www.sinergiaai.com/downloads/SinergiaPrint-Setup.exe";
    /// <summary>Gets or sets whether connecting requests native website authorization when required.</summary>
    public bool AutomaticAuthorization { get; set; } = true;
    /// <summary>Gets or sets the workstation's loopback WebSocket port. The default is 17831.</summary>
    public int LocalBridgePort { get; set; } = 17831;
    /// <summary>Gets or sets the host route prefix used to serve the embedded browser module.</summary>
    public string ApiPrefix { get; set; } = "/printing";
    /// <summary>Gets or sets the legacy backend data directory. The direct browser bridge does not use it.</summary>
    public string DataDirectory { get; set; } = "App_Data/PrintAgent";
    /// <summary>Gets or sets the legacy public backend URL. The direct browser bridge does not use it.</summary>
    public string? PublicBaseUrl { get; set; }
    /// <summary>Gets or sets the browser storage namespace retained for integration compatibility.</summary>
    public string BrowserStoragePrefix { get; set; } = "PrintAgent.";
    /// <summary>Gets or sets the legacy data-protection purpose. The direct browser bridge does not use it.</summary>
    public string LegacyProtectionPurpose { get; set; } = "PrintAgent.Connection.v1";
}
