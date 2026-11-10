using System.IO.Pipes;
using System.Text.Json;
using System.Windows.Forms;

namespace SinergiaAI.PrintAgent.Approval;
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            if (args.Length != 1 || !Uri.TryCreate(args[0],UriKind.Absolute,out var request) || request.Scheme != "sinergia-printagent" || request.Host != "authorize") throw new ArgumentException("Open website authorization from your printing application.");
            var values = request.Query.TrimStart('?').Split('&').Select(p => p.Split('=',2)).Where(p => p.Length == 2).ToDictionary(p => p[0],p => Uri.UnescapeDataString(p[1]));
            if (!values.TryGetValue("origin",out var origin) || !Uri.TryCreate(origin,UriKind.Absolute,out var site)
                || site.UserInfo.Length != 0 || site.AbsolutePath != "/" || site.Query.Length != 0 || site.Fragment.Length != 0
                || site.Scheme != "https" && !(site.Scheme == "http" && site.IsLoopback)) throw new ArgumentException("Invalid website origin.");
            var port = values.TryGetValue("port",out var raw) && int.TryParse(raw,out var parsed) ? parsed : 17831;
            if (port is < 1024 or > 65535) throw new ArgumentException("Invalid local port.");
            var revoke = values.GetValueOrDefault("revoke") == "true";
            var verb = revoke ? "Remove printing access for" : "Allow printing from";
            if (MessageBox.Show($"{verb}:\n\n{site.GetLeftPart(UriPartial.Authority)}\n\nThis permission applies to all users of this computer. Only authorize websites you trust.","PrintAgent · SinergiaAI",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            using var pipe = new NamedPipeClientStream(".","SinergiaAI.PrintAgent.Approval." + port,PipeDirection.InOut,PipeOptions.None);
            pipe.Connect(5000);
            using var writer = new StreamWriter(pipe,leaveOpen:true) { AutoFlush = true };
            writer.WriteLine(JsonSerializer.Serialize(new { Origin = site.GetLeftPart(UriPartial.Authority), Approve = !revoke }));
            using var reader = new StreamReader(pipe);
            if (reader.ReadLine() != "OK") throw new IOException("The Windows service did not confirm authorization.");
            MessageBox.Show("Website permission updated. Return to your application and refresh the connection.","PrintAgent",MessageBoxButtons.OK,MessageBoxIcon.Information);
        }
        catch (Exception) { MessageBox.Show("Could not update website permission. Verify that the PrintAgent Windows service is running, then try again.","PrintAgent",MessageBoxButtons.OK,MessageBoxIcon.Error); }
    }
}
