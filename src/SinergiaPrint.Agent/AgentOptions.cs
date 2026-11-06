namespace SinergiaPrint.Agent;

public sealed class AgentOptions
{
    public string ApiBaseUrl { get; set; } = "";
    public Guid InstallationId { get; set; }
    public string Token { get; set; } = "";
    public int PollSeconds { get; set; } = 2;
    public List<NetworkPrinter> NetworkPrinters { get; set; } = [];
    public string DataPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "SinergiaPrint");
}
public record NetworkPrinter(string Name, string Address);
