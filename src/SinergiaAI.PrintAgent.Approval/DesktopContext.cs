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
    private readonly System.Windows.Forms.Timer _timer;
    private readonly EventWaitHandle _activation;
    private bool _polling, _changing, _exiting;
    public DesktopContext(int port,bool background,EventWaitHandle activation)
    {
        _port = port; _activation = activation;
        _window = new Form { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application, Text = "SinergiaAI PrintAgent", ClientSize = new Size(530,340), StartPosition = FormStartPosition.CenterScreen, BackColor = Color.White, Font = new Font("Segoe UI",10), AutoScaleMode = AutoScaleMode.Dpi, MaximizeBox = false };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(26) };
        panel.Controls.Add(new Label { Text = "SinergiaAI PrintAgent", Font = new Font("Segoe UI",21,FontStyle.Bold), AutoSize = true });
        panel.Controls.Add(new Label { Text = UiText.Get("Local printing · Version 1.1.0","Impressão local · Versão 1.1.0"), AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3,8,3,20) });
        _status = new Label { Text = UiText.Get("Checking Windows service…","Verificando serviço do Windows…"), AutoSize = true, MaximumSize = new Size(470,0), Margin = new Padding(3,0,3,18) }; panel.Controls.Add(_status);
        var actions = new FlowLayoutPanel { Width = 470, Height = 48 };
        Button Action(string text,Func<Task> task) { var b = new Button { Text = text, Width = 145, Height = 38 }; b.Click += async (_,_) => await task(); return b; }
        actions.Controls.Add(Action(UiText.Get("Start service","Iniciar serviço"),() => ChangeServiceAsync(true))); actions.Controls.Add(Action(UiText.Get("Stop service","Parar serviço"),() => ChangeServiceAsync(false))); actions.Controls.Add(Action(UiText.Get("Authorized websites","Sites autorizados"),ShowSitesAsync)); panel.Controls.Add(actions);
        panel.Controls.Add(new Label { Text = UiText.Get("Closing this window keeps the tray app running. Exit tray app removes only the icon; the Windows service continues printing.","Fechar esta janela mantém o ícone na bandeja. Sair do aplicativo remove apenas o ícone; o serviço Windows continua imprimindo."), MaximumSize = new Size(460,0), AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3,20,3,0) });
        _window.Controls.Add(panel); _window.FormClosing += (_,e) => { if (!_exiting) { e.Cancel = true; _window.Hide(); } };
        var menu = new ContextMenuStrip();
        menu.Items.Add(UiText.Get("Open SinergiaAI PrintAgent","Abrir SinergiaAI PrintAgent"),null,(_,_) => ShowWindow());
        menu.Items.Add(UiText.Get("Start Windows service","Iniciar serviço Windows"),null,async (_,_) => await ChangeServiceAsync(true));
        menu.Items.Add(UiText.Get("Stop Windows service…","Parar serviço Windows…"),null,async (_,_) => await ChangeServiceAsync(false));
        menu.Items.Add(UiText.Get("Stop service and exit…","Parar serviço e sair…"),null,async (_,_) => { await ChangeServiceAsync(false); if (ServiceStatus() == ServiceControllerStatus.Stopped) ExitThread(); });
        menu.Items.Add(UiText.Get("Authorized websites…","Sites autorizados…"),null,async (_,_) => await ShowSitesAsync());
        menu.Items.Add(new ToolStripSeparator()); menu.Items.Add(UiText.Get("Exit tray app","Sair do aplicativo da bandeja"),null,(_,_) => ExitThread());
        _tray = new NotifyIcon { Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application, Text = "SinergiaAI PrintAgent", ContextMenuStrip = menu, Visible = true };
        _tray.DoubleClick += async (_,_) => { ShowWindow(); await EnsureRunningAsync(); };
        _timer = new System.Windows.Forms.Timer { Interval = 1500 }; _timer.Tick += async (_,_) => await TickAsync(); _timer.Start();
        if (!background) { ShowWindow(); _ = EnsureRunningAsync(); }
    }
    private void ShowWindow() { _window.Show(); _window.WindowState = FormWindowState.Normal; _window.Activate(); }
    private static ServiceControllerStatus? ServiceStatus()
    { try { using var service = new ServiceController(ServiceName); return service.Status; } catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return null; } }
    private async Task EnsureRunningAsync() { if (ServiceStatus() == ServiceControllerStatus.Stopped) await ChangeServiceAsync(true); }
    private async Task ChangeServiceAsync(bool start)
    {
        if (_changing) return;
        if (!start && MessageBox.Show(UiText.Get("Stop printing on this computer? Pending jobs will be preserved. A delivery already in progress may become Unknown and will not be automatically resent.","Parar a impressão neste computador? Os trabalhos pendentes serão preservados. Um envio em andamento pode ficar com resultado incerto e não será reenviado automaticamente."),"SinergiaAI PrintAgent",MessageBoxButtons.YesNo,MessageBoxIcon.Question,MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        _changing = true;
        try
        {
            var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!,start ? "--start-service" : "--stop-service") { UseShellExecute = true, Verb = "runas" });
            if (process is not null) { using (process) { await process.WaitForExitAsync(); if (process.ExitCode != 0) throw new InvalidOperationException(); } }
        }
        catch (Exception) { MessageBox.Show(UiText.Get("The service could not be changed. Windows administrator approval is required. Verify that PrintAgent is installed.","Não foi possível alterar o serviço. É necessária a autorização de um administrador do Windows. Verifique se o PrintAgent está instalado."),"SinergiaAI PrintAgent",MessageBoxButtons.OK,MessageBoxIcon.Information); }
        finally { _changing = false; }
    }
    private async Task TickAsync()
    {
        if (_activation.WaitOne(0)) { ShowWindow(); _ = EnsureRunningAsync(); }
        if (_polling || _exiting || !DesktopSession.IsActive) return;
        _polling = true;
        try
        {
            var response = await DesktopPipe.CallAsync(_port,new { Operation = "poll" });
            if (_exiting) return;
            _status.Text = UiText.Get("Service running · Ready for local printing","Serviço ativo · Pronto para impressão local"); _tray.Text = UiText.Get("SinergiaAI PrintAgent · Running","SinergiaAI PrintAgent · Ativo");
            var request = JsonSerializer.Deserialize<DesktopPipe.Pending>(response);
            if (request is not null)
            {
                using var dialog = new PermissionForm(request.Origin);
                var approved = dialog.ShowDialog() == DialogResult.OK;
                await DesktopPipe.CallAsync(_port,new { request.Origin, Approve = approved, RequestId = request.Id });
            }
        }
        catch (Exception) { if (_exiting) return; _status.Text = ServiceStatus() is null ? UiText.Get("Service not installed or status unavailable","Serviço não instalado ou status indisponível") : UiText.Get("Service unavailable · Start the service to print","Serviço indisponível · Inicie o serviço para imprimir"); _tray.Text = UiText.Get("SinergiaAI PrintAgent · Offline","SinergiaAI PrintAgent · Desconectado"); }
        finally { _polling = false; }
    }
    private async Task ShowSitesAsync()
    {
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
        catch (Exception) { MessageBox.Show(UiText.Get("Start the Windows service before managing websites.","Inicie o serviço Windows antes de gerenciar os sites."),"SinergiaAI PrintAgent"); }
    }
    protected override void ExitThreadCore()
    {
        _exiting = true; _timer.Stop(); _timer.Dispose(); _tray.Visible = false; _tray.Dispose(); _window.Dispose(); base.ExitThreadCore();
    }
}
