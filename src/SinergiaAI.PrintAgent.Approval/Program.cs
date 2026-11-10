using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using System.Windows.Forms;
namespace SinergiaAI.PrintAgent.Approval;
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        if (args.Contains("--preview"))
        {
            using var preview = new PermissionForm("https://app.example.com");
            preview.Text += UiText.Get(" · Preview (no changes)"," · Prévia (sem alterações)");
            preview.ShowDialog(); return;
        }
        if (args.Length == 1 && args[0] is "--start-service" or "--stop-service")
        {
            try
            {
                using var service = new ServiceController("SinergiaPrint Agent");
                if (args[0] == "--start-service") { if (service.Status != ServiceControllerStatus.Running) service.Start(); service.WaitForStatus(ServiceControllerStatus.Running,TimeSpan.FromSeconds(30)); }
                else { if (service.Status != ServiceControllerStatus.Stopped) service.Stop(); service.WaitForStatus(ServiceControllerStatus.Stopped,TimeSpan.FromSeconds(30)); }
            }
            catch (Exception) { Environment.ExitCode = 1; }
            return;
        }
        if (args.Length == 1 && args[0].StartsWith("sinergia-printagent:",StringComparison.OrdinalIgnoreCase)) { ManualPermission(args[0]); return; }
        var port = 17831;
        var portArg = Array.IndexOf(args,"--port");
        if (portArg >= 0 && (portArg + 1 >= args.Length || !int.TryParse(args[portArg+1],out port) || port is < 1024 or > 65535)) return;
        var name = "Local\\SinergiaAI.PrintAgent.Desktop." + WindowsIdentity.GetCurrent().User!.Value + "." + System.Diagnostics.Process.GetCurrentProcess().SessionId + "." + port;
        using var single = new Mutex(true,name,out var first);
        using var activate = new EventWaitHandle(false,EventResetMode.AutoReset,name + ".Activate");
        if (!first) { if (!args.Contains("--background")) activate.Set(); return; }
        Application.Run(new DesktopContext(port,args.Contains("--background"),activate));
    }
    private static void ManualPermission(string value)
    {
        try
        {
            var request = new Uri(value);
            if (request.Host != "authorize") return;
            var values = request.Query.TrimStart('?').Split('&').Select(p => p.Split('=',2)).Where(p => p.Length == 2).ToDictionary(p => p[0],p => Uri.UnescapeDataString(p[1]));
            var site = new Uri(values["origin"]);
            if (site.UserInfo.Length != 0 || site.AbsolutePath != "/" || site.Query.Length != 0 || site.Fragment.Length != 0 || site.Scheme != "https" && !(site.Scheme == "http" && site.IsLoopback)) return;
            var port = values.TryGetValue("port",out var raw) && int.TryParse(raw,out var number) ? number : 17831;
            if (port is < 1024 or > 65535) return;
            var revoke = values.GetValueOrDefault("revoke") == "true";
            using var form = new PermissionForm(site.GetLeftPart(UriPartial.Authority),revoke);
            if (form.ShowDialog() != DialogResult.OK) return;
            if (DesktopPipe.CallAsync(port,new { Origin = site.GetLeftPart(UriPartial.Authority), Approve = !revoke }).GetAwaiter().GetResult() != "OK") throw new IOException();
        }
        catch (Exception) { MessageBox.Show(UiText.Get("Could not update website permission. Check the Windows service.","Não foi possível atualizar a permissão do site. Verifique o serviço Windows."),"SinergiaAI PrintAgent"); }
    }
}
