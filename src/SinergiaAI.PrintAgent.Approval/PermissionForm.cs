using System.Drawing;
using System.Windows.Forms;
namespace SinergiaAI.PrintAgent.Approval;

internal sealed class PermissionForm : Form
{
    public PermissionForm(string origin,bool revoke = false)
    {
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application;
        Text = "SinergiaAI PrintAgent"; ClientSize = new Size(550,330); StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        BackColor = Color.White; Font = new Font("Segoe UI",10); AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26), RowCount = 5, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,32)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,45));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,65)); layout.RowStyles.Add(new RowStyle(SizeType.Percent,100)); layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
        layout.Controls.Add(new Label { Text = "SINERGIAAI  /  PRINTAGENT", ForeColor = Color.FromArgb(65,92,190), AutoSize = true });
        layout.Controls.Add(new Label { Text = revoke ? UiText.Get("Remove this website's access?","Remover o acesso deste site?") : UiText.Get("Allow this website to print?","Permitir que este site imprima?"), Font = new Font("Segoe UI",18,FontStyle.Bold), Dock = DockStyle.Fill });
        layout.Controls.Add(new Label { Text = origin, Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = Color.FromArgb(242,245,252), AutoEllipsis = true, Font = new Font("Segoe UI",12,FontStyle.Bold), AccessibleName = UiText.Get("Website origin","Endereço do site") });
        layout.Controls.Add(new Label { Text = UiText.Get("This permission applies to all users of this computer. Only approve websites you trust. Already accepted print jobs are not cancelled by removing access.","A permissão vale para todos os usuários deste computador. Autorize apenas sites confiáveis. Remover o acesso não cancela trabalhos já aceitos."), Dock = DockStyle.Fill, Padding = new Padding(0,14,0,0), ForeColor = Color.DimGray });
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var deny = new Button { Text = UiText.Get("Cancel","Cancelar"), Width = 100, Height = 38, DialogResult = DialogResult.Cancel };
        var allow = new Button { Text = revoke ? UiText.Get("Remove access","Remover acesso") : UiText.Get("Allow website","Permitir site"), Width = 150, Height = 38, DialogResult = DialogResult.OK, BackColor = Color.FromArgb(52,89,230), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        buttons.Controls.Add(allow); buttons.Controls.Add(deny); layout.Controls.Add(buttons); Controls.Add(layout);
        AcceptButton = deny; CancelButton = deny;
        var expiry = new System.Windows.Forms.Timer { Interval = 65000 }; expiry.Tick += (_,_) => { DialogResult = DialogResult.Cancel; Close(); }; expiry.Start(); FormClosed += (_,_) => expiry.Dispose();
    }
}
