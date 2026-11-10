using System.Diagnostics;
using System.Drawing;
using System.ServiceProcess;
using System.Text.Json;
using System.Windows.Forms;
namespace SinergiaAI.PrintAgent.Approval;

internal sealed class DesktopContext : ApplicationContext
{
    private const string ServiceName = "SinergiaPrint Agent";
    private readonly int _port;
    private readonly NotifyIcon _tray;
    private readonly Form _window;
    private readonly Label _status;
    private readonly Label _communication;
    private readonly Button _start, _stop, _sites;
    private readonly ToolStripItem _startMenu, _stopMenu, _stopExitMenu, _sitesMenu;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly EventWaitHandle _activation;
    private bool _polling, _changing, _exiting;
    public DesktopContext(int port,bool background,EventWaitHandle activation)
    {
        _port = port; _activation = activation;
        _window = new Form { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application, Text = "SinergiaAI PrintAgent · " + Version, ClientSize = new Size(560,260), StartPosition = FormStartPosition.CenterScreen, BackColor = Color.White, Font = new Font("Segoe UI",10), AutoScaleMode = AutoScaleMode.Dpi, MaximizeBox = false };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(26) };
        var heading = new FlowLayoutPanel { Width = 500, Height = 54, WrapContents = false };
        heading.Controls.Add(new Label { Text = "SinergiaAI PrintAgent", Font = new Font("Segoe UI",20,FontStyle.Bold), AutoSize = true });
        heading.Controls.Add(new Label { Text = Version, Font = new Font("Segoe UI",11), ForeColor = Color.DimGray, AutoSize = true, Margin = new Padding(8,13,0,0) });
        panel.Controls.Add(heading);
        _status = new Label { Text = UiText.Get("Checking Windows service…","Verificando serviço do Windows…"), AutoSize = true, Padding = new Padding(12,8,12,8), Margin = new Padding(3,0,3,10), Font = new Font("Segoe UI",11,FontStyle.Bold) }; panel.Controls.Add(_status);
        _communication = new Label { AutoSize = true, MaximumSize = new Size(500,0), ForeColor = Color.FromArgb(153,89,0), Visible = false };
        panel.Controls.Add(_communication);
        var actions = new FlowLayoutPanel { Width = 500, Height = 48 };
        Button Action(string text,Func<Task> task) { var b = new Button { Text = text, Width = 155, Height = 38 }; b.Click += async (_,_) => await task(); return b; }
        _start = Action(UiText.Get("Start service","Iniciar serviço"),() => ChangeServiceAsync(true));
        _stop = Action(UiText.Get("Stop service","Parar serviço"),() => ChangeServiceAsync(false));
        _sites = Action(UiText.Get("Authorized websites","Sites autorizados"),ShowSitesAsync);
        actions.Controls.AddRange(new Control[] { _start, _stop, _sites }); panel.Controls.Add(actions);
        _window.Controls.Add(panel); _window.FormClosing += (_,e) => { if (!_exiting) { e.Cancel = true; _window.Hide(); } };
        var menu = new ContextMenuStrip();
        menu.Items.Add(UiText.Get("Open SinergiaAI PrintAgent","Abrir SinergiaAI PrintAgent"),null,(_,_) => ShowWindow());
        _startMenu = menu.Items.Add(UiText.Get("Start Windows service","Iniciar serviço Windows"),null,async (_,_) => await ChangeServiceAsync(true));
        _stopMenu = menu.Items.Add(UiText.Get("Stop Windows service…","Parar serviço Windows…"),null,async (_,_) => await ChangeServiceAsync(false));
        _stopExitMenu = menu.Items.Add(UiText.Get("Stop service and exit…","Parar serviço e sair…"),null,async (_,_) => { await ChangeServiceAsync(false); if (ServiceStatus() == ServiceControllerStatus.Stopped) ExitThread(); });
        _sitesMenu = menu.Items.Add(UiText.Get("Authorized websites…","Sites autorizados…"),null,async (_,_) => await ShowSitesAsync());
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add(UiText.Get("Exit tray app","Sair do aplicativo da bandeja"),null,(_,_) => ExitThread());
        _tray = new NotifyIcon { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application, Text = "SinergiaAI PrintAgent", ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += async (_,_) => { ShowWindow(); await EnsureRunningAsync(); };
        _timer = new System.Windows.Forms.Timer { Interval = 1500 }; _timer.Tick += async (_,_) => await TickAsync(); _timer.Start();
        RefreshServiceState();
        if (!background) { ShowWindow(); _ = EnsureRunningAsync(); }
    }
    private static string Version => typeof(DesktopContext).Assembly.GetName().Version?.ToString(3) ?? "1.1.0";
    private DesktopServiceState RefreshServiceState()
    {
        var state = new DesktopServiceState(ServiceStatus(),_changing);
        if (_exiting) return state;
        _start.Enabled = _startMenu.Enabled = state.CanStart;
        _stop.Enabled = _stopMenu.Enabled = _stopExitMenu.Enabled = state.CanStop;
        _sites.Enabled = _sitesMenu.Enabled = state.CanManageSites;
        _status.Text = "●  " + state.Caption;
        var running = state.Status == ServiceControllerStatus.Running;
        var stopped = state.Status == ServiceControllerStatus.Stopped;
        _status.ForeColor = running ? Color.FromArgb(17,112,73) : stopped ? Color.FromArgb(85,95,110) : Color.FromArgb(153,89,0);
        _status.BackColor = running ? Color.FromArgb(229,247,238) : stopped ? Color.FromArgb(239,242,247) : Color.FromArgb(255,245,224);
        _tray.Text = "SinergiaAI PrintAgent · " + state.Caption;
        if (!running) _communication.Visible = false;
        return state;
    }
    private void ShowWindow() { _window.Show(); _window.WindowState = FormWindowState.Normal; _window.Activate(); }
    private static ServiceControllerStatus? ServiceStatus()
    { try { using var service = new ServiceController(ServiceName); return service.Status; } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return null; } }
    private async Task EnsureRunningAsync() { if (ServiceStatus() == ServiceControllerStatus.Stopped) await ChangeServiceAsync(true); }
    private async Task ChangeServiceAsync(bool start)
    {
        var state = RefreshServiceState();
        if (start ? !state.CanStart : !state.CanStop) return;
        _changing = true; RefreshServiceState();
        try
        {
            if (!start && MessageBox.Show(UiText.Get("Stop printing on this computer? Pending jobs will be preserved. A delivery already in progress may become Unknown and will not be automatically resent.","Parar a impressão neste computador? Os trabalhos pendentes serão preservados. Um envio em andamento pode ficar com resultado incerto e não será reenviado automaticamente."),"SinergiaAI PrintAgent",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!,start ? "--start-service" : "--stop-service") { UseShellExecute = true, Verb = "runas" });
            if (process is not null) { using (process) { await process.WaitForExitAsync(); if (process.ExitCode != 0) throw new InvalidOperationException(); } }
        }
        catch (Exception) { MessageBox.Show(UiText.Get("The service could not be changed. Windows administrator approval is required. Verify that PrintAgent is installed.","Não foi possível alterar o serviço. É necessária a autorização de um administrador do Windows. Verifique se o PrintAgent está instalado."),"SinergiaAI PrintAgent",MessageBoxButtons.OK,MessageBoxIcon.Information); }
        finally { _changing = false; RefreshServiceState(); }
    }
    private async Task TickAsync()
    {
        if (_activation.WaitOne(0)) { ShowWindow(); _ = EnsureRunningAsync(); }
        if (_exiting) return;
        var state = RefreshServiceState();
        if (_polling || _changing || state.Status != ServiceControllerStatus.Running || !DesktopSession.IsActive) return;
        _polling = true;
        try
        {
            var response = await DesktopPipe.CallAsync(_port,new { Operation = "poll" });
            if (_exiting) return;
            _communication.Visible = false;
            var request = JsonSerializer.Deserialize<DesktopPipe.Pending>(response);
            if (request is not null)
            {
                using var dialog = new PermissionForm(request.Origin);
                var approved = dialog.ShowDialog() == DialogResult.OK;
                await DesktopPipe.CallAsync(_port,new { request.Origin, Approve = approved, RequestId = request.Id });
            }
        }
        catch (Exception)
        {
            if (_exiting) return;
            var current = RefreshServiceState();
            if (current.Status == ServiceControllerStatus.Running)
            {
                _communication.Text = UiText.Get("Local control connection unavailable. Try again shortly.","Comunicação com o controle local indisponível. Tente novamente em instantes.");
                _communication.Visible = true;
            }
        }
        finally { _polling = false; }
    }
    private async Task ShowSitesAsync()
    {
        if (!RefreshServiceState().CanManageSites) return;
        try
        {
            var origins = JsonSerializer.Deserialize<string[]>(await DesktopPipe.CallAsync(_port,new { Operation = "origins" })) ?? [];
            using var form = new Form { Text = UiText.Get("Authorized websites · SinergiaAI PrintAgent","Sites autorizados · SinergiaAI PrintAgent"), ClientSize = new Size(570,350), StartPosition = FormStartPosition.CenterScreen, Font = new Font("Segoe UI",10) };
            var list = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true }; list.Items.AddRange(origins);
            var remove = new Button { Text = UiText.Get("Remove selected website access","Remover acesso do site selecionado"), Dock = DockStyle.Bottom, Height = 45 };
            remove.Click += async (_,_) =>
            {
                if (list.SelectedItem is not string origin) return;
                using var permission = new PermissionForm(origin,true);
                if (permission.ShowDialog(form) != DialogResult.OK) return;
                try { await DesktopPipe.CallAsync(_port,new { Origin = origin, Approve = false }); list.Items.Remove(origin); }
                catch (Exception) { MessageBox.Show(UiText.Get("Could not update website access. Check the Windows service.","Não foi possível atualizar o acesso do site. Verifique o serviço Windows."),"SinergiaAI PrintAgent"); }
            };
            form.Controls.Add(list); form.Controls.Add(remove); form.ShowDialog(_window.Visible ? _window : null);
        }
        catch (Exception) { MessageBox.Show(UiText.Get("Could not read the authorized websites from the local service. Check the local control connection and try again.","Não foi possível consultar os sites autorizados no serviço local. Verifique a comunicação com o controle local e tente novamente."),"SinergiaAI PrintAgent"); }
    }
    protected override void ExitThreadCore()
    {
        _exiting = true; _timer.Stop(); _timer.Dispose(); _tray.Visible = false; _tray.Dispose(); _window.Dispose(); base.ExitThreadCore();
    }
}
