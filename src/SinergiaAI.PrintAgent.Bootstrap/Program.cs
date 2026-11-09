using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SinergiaAI.PrintAgent.Bootstrap
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            if (args.Length == 2 && args[0] == "--download-only")
            {
                try { SetupWindow.DownloadInstallerAsync(args[1]).GetAwaiter().GetResult(); return 0; }
                catch (Exception ex)
                {
                    File.WriteAllText(args[1] + ".error", ex.GetType().Name + ": " + ex.Message);
                    return 1;
                }
            }
            // Diagnostic mode validates redemption without ever launching an installer/service.
            if (args.Length == 2 && args[0] == "--resolve-only")
            {
                try
                {
                    var config = SetupContext.Read().ResolveAsync().GetAwaiter().GetResult();
                    File.WriteAllText(args[1], config, new UTF8Encoding(false));
                    return 0;
                }
                catch (Exception ex)
                {
                    File.WriteAllText(args[1] + ".error", ex.GetType().Name + ": " + ex.Message);
                    return 1;
                }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new SetupWindow());
            return 0;
        }
    }

    internal sealed class SetupContext
    {
        private const string Marker = "SINERGIAPRINT-CONTEXT-V1";
        private readonly Uri _api;
        private readonly string _code;
        private SetupContext(Uri api, string code) { _api = api; _code = code; }

        internal static SetupContext Read()
        {
            using (var stream = File.OpenRead(Assembly.GetExecutingAssembly().Location))
            using (var reader = new BinaryReader(stream, Encoding.UTF8))
            {
                var marker = Encoding.ASCII.GetBytes(Marker);
                if (stream.Length < marker.Length + 4) throw MissingContext();
                stream.Position = stream.Length - marker.Length;
                if (!reader.ReadBytes(marker.Length).SequenceEqual(marker)) throw MissingContext();
                stream.Position = stream.Length - marker.Length - 4;
                var length = reader.ReadInt32();
                if (length <= 0 || length > 16384 || length > stream.Length - marker.Length - 4) throw MissingContext();
                stream.Position -= length + 4;
                var json = new JavaScriptSerializer { MaxJsonLength = 16384 };
                var context = json.Deserialize<ContextData>(Encoding.UTF8.GetString(reader.ReadBytes(length)));
                Uri? api;
                DateTimeOffset expires;
                if (context == null || !Uri.TryCreate(context.apiBaseUrl, UriKind.Absolute, out api) || api == null ||
                    (api.Scheme != "https" && !(api.Scheme == "http" && api.IsLoopback)) ||
                    api.UserInfo.Length != 0 || api.Query.Length != 0 || api.Fragment.Length != 0 ||
                    context.code == null || context.code.Length != 64 || !context.code.All(Uri.IsHexDigit) ||
                    !DateTimeOffset.TryParse(context.expiresAt, CultureInfo.InvariantCulture, DateTimeStyles.None, out expires))
                    throw MissingContext();
                return new SetupContext(new Uri(api.AbsoluteUri.TrimEnd('/') + "/"), context.code);
            }
        }

        private static Exception MissingContext() => new InvalidOperationException(
            "Download the installer using the Install agent button in your application. This file has no valid enrollment context.");

        internal async Task<string> ResolveAsync()
        {
            var json = new JavaScriptSerializer { MaxJsonLength = 16384 };
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            using (var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30), MaxResponseContentBufferSize = 16384 })
            using (var content = new StringContent(json.Serialize(new { code = _code }), Encoding.UTF8, "application/json"))
            using (var response = await client.PostAsync(new Uri(_api, "enrollment/redeem"), content))
            {
                if (response.StatusCode == HttpStatusCode.BadRequest)
                    throw new InvalidOperationException("This installer has expired or has already been used. Return to the application and download a new installer.");
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException("Could not connect to the application. Check your connection and download a new installer. HTTP " + (int)response.StatusCode);
                var text = await response.Content.ReadAsStringAsync();
                if (text.Length > 16384) throw new InvalidOperationException("Invalid configuration response.");
                var config = json.Deserialize<ConfigurationData>(text);
                Guid installationId;
                Uri? api;
                if (config == null || !Guid.TryParse(config.installationId, out installationId) || installationId == Guid.Empty ||
                    string.IsNullOrWhiteSpace(config.token) || config.token!.Length < 32 ||
                    !Uri.TryCreate(config.apiBaseUrl, UriKind.Absolute, out api) || api == null ||
                    !string.Equals(api.AbsoluteUri.TrimEnd('/'), _api.AbsoluteUri.TrimEnd('/'), StringComparison.Ordinal))
                    throw new InvalidOperationException("The configuration does not match this application.");
                return json.Serialize(new { Agent = new { ApiBaseUrl = api.AbsoluteUri, InstallationId = installationId.ToString(), Token = config.token, NetworkPrinters = new object[0] } });
            }
        }

        private sealed class ContextData
        {
            public string? apiBaseUrl { get; set; }
            public string? code { get; set; }
            public string? expiresAt { get; set; }
        }
        private sealed class ConfigurationData
        {
            public string? apiBaseUrl { get; set; }
            public string? installationId { get; set; }
            public string? token { get; set; }
        }
    }

    internal sealed class SetupWindow : Form
    {
        private readonly Label _status = new Label { AutoSize = false, Left = 26, Top = 82, Width = 480, Height = 95 };
        private readonly Button _close = new Button { Text = "Close", Left = 402, Top = 190, Width = 100, Enabled = false };
        private bool _working = true;

        internal SetupWindow()
        {
            Text = "PrintAgent Setup";
            ClientSize = new Size(530, 245);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10);
            Controls.Add(new Label { Text = "Connect your printers", Left = 26, Top = 25, Width = 480, Height = 35, Font = new Font("Segoe UI", 16, FontStyle.Bold) });
            Controls.Add(_status); Controls.Add(_close);
            _close.Click += (_, __) => Close();
            FormClosing += (_, e) => { if (_working) e.Cancel = true; };
            Shown += async (_, __) => await InstallAsync();
        }

        private async Task InstallAsync()
        {
            string? directory = null;
            try
            {
                _status.Text = "Retrieving your application configuration...";
                var configuration = await SetupContext.Read().ResolveAsync();
                directory = CreatePrivateDirectory();
                var configPath = Path.Combine(directory, "configuration.json");
                File.WriteAllText(configPath, configuration, new UTF8Encoding(false));
                var installer = Path.Combine(directory, "SinergiaPrint-Setup.exe");
                _status.Text = "Downloading and verifying the agent...";
                await DownloadInstallerAsync(installer);
                _status.Text = "Approve the Windows installation. No URL, GUID or token needs to be entered.";
                using (var process = Process.Start(new ProcessStartInfo(installer,
                    "/SP- /SILENT /SUPPRESSMSGBOXES /NORESTART /CONFIGFILE=\"" + configPath + "\"")
                    { UseShellExecute = true, Verb = "runas", WorkingDirectory = directory }))
                {
                    if (process == null) throw new InvalidOperationException("Could not start the installer.");
                    await Task.Run(() => process.WaitForExit());
                    if (process.ExitCode != 0) throw new InvalidOperationException("Installation did not complete. Download a new installer from the application to try again.");
                }
                _status.Text = "Installation complete. Return to the application; the agent will connect and report available printers automatically.";
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            { _status.Text = "Installation was cancelled in Windows. Download a new installer from the application when you are ready to try again."; }
            catch (Exception ex)
            { _status.Text = ex is InvalidOperationException ? ex.Message : "Could not complete installation. Check your connection and download a new installer from the application."; }
            finally
            {
                // Remove credentials after the elevated installer has finished reading them.
                if (directory != null)
                {
                    foreach (var name in new[] { "configuration.json", "SinergiaPrint-Setup.exe" })
                        try { File.Delete(Path.Combine(directory, name)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    try { Directory.Delete(directory); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
                _working = false; _close.Enabled = true;
            }
        }

        private static string CreatePrivateDirectory()
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SinergiaPrint", "Setup", Guid.NewGuid().ToString("N"));
            var security = new DirectorySecurity();
            security.SetAccessRuleProtection(true, false);
            var rights = FileSystemRights.FullControl;
            var inherit = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            foreach (var sid in new[] { WindowsIdentity.GetCurrent().User!, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
                security.AddAccessRule(new FileSystemAccessRule(sid, rights, inherit, PropagationFlags.None, AccessControlType.Allow));
            Directory.CreateDirectory(directory, security);
            return directory;
        }

        internal static async Task DownloadInstallerAsync(string path)
        {
            using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) })
            using (var response = await client.GetAsync(InstallerRelease.Url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                using (var source = await response.Content.ReadAsStreamAsync())
                using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[65536];
                    long total = 0;
                    int count;
                    while ((count = await source.ReadAsync(buffer, 0, buffer.Length)) != 0)
                    {
                        total += count;
                        if (total > 150 * 1024 * 1024) throw new InvalidOperationException("The installer exceeded the expected download size.");
                        await output.WriteAsync(buffer, 0, count);
                    }
                }
            }
            using (var sha = SHA256.Create())
            using (var file = File.OpenRead(path))
            {
                var hash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
                if (!string.Equals(hash, InstallerRelease.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The available installer does not match this version. Return to the application and download it again after the server update.");
            }
        }
    }
}
