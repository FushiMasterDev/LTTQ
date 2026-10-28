using System.ComponentModel;
using Homestay.GUI.Infrastructure;

namespace Homestay.GUI.Common;

// Only shared presentation behavior lives here; business rules remain in BLL.
public class ScreenForm : Form
{
    [Browsable(false), DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string HelpText { get; set; } = "Chọn bộ lọc, tải dữ liệu và mở dòng cần xem. Các thao tác ghi được thực hiện qua BLL.";
    public ScreenForm() { KeyPreview = true; }
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (!DesignMode && LicenseManager.UsageMode != LicenseUsageMode.Designtime) Wire(Controls);
    }
    private void Wire(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            if (control is Form) continue;
            if (control is Button button)
            {
                if (Equals(button.Tag, "pending")) button.Click += (_, _) => ErrorHandler.Pending(this);
                if (Equals(button.Tag, "help")) button.Click += (_, _) => MessageBox.Show(this, HelpText, "Hướng dẫn", MessageBoxButtons.OK, MessageBoxIcon.Information);
                if (Equals(button.Tag, "close")) button.Click += (_, _) => Close();
            }
            Wire(control.Controls);
        }
    }
    private bool mutationBusy, allowMutationClose;
    protected async Task RunMutationAsync(Button trigger, Func<Task> action, Func<Task>? reload = null,
        Func<bool>? enabledAfter = null)
    {
        if (mutationBusy) return;
        mutationBusy = true;
        try
        {
            await ErrorHandler.RunAsync(trigger, async () =>
            {
                var body = Controls.Count == 0 ? this : Controls[0];
                body.Enabled = false;
                try { await action(); }
                finally { if (!body.IsDisposed) body.Enabled = true; }
            }, reload, enabledAfter);
        }
        finally { mutationBusy = false; }
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Do not cancel a write while SQL may already have committed it.
        if (mutationBusy && !allowMutationClose && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
        base.OnFormClosing(e);
    }
    protected void CloseAfterMutation()
    {
        allowMutationClose = true;
        try { Close(); }
        finally { allowMutationClose = false; }
    }

    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Enter && AcceptButton is null && ActiveControl is TextBox { Multiline: false })
        { SelectNextControl(ActiveControl, true, true, true, true); return true; }
        return base.ProcessDialogKey(keyData);
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        if (keyData == (Keys.Control | Keys.F) && FindControl(Controls,"filterPanelField0") is TextBox filter && filter.Visible)
        { filter.Focus();filter.SelectAll();return true; }
        if (keyData == Keys.F2)
        {
            var grids=Descendants(Controls).OfType<PagedGrid>().Where(g=>g.Visible).ToArray();
            var grid=grids.FirstOrDefault(g=>g.ContainsFocus)??grids.FirstOrDefault();
            if(grid is not null){try{grid.OpenSelectedDetail();}catch(Exception e){ErrorHandler.Show(e,this);}return true;}
        }
        if (keyData == Keys.F5)
        { ErrorHandler.Pending(this); return true; }
        if (keyData == (Keys.Control | Keys.Enter))
        {
            var save = FindButton(Controls, "saveButton");
            if (save is not null && save.Enabled) { save.PerformClick(); return true; }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    private static IEnumerable<Control> Descendants(Control.ControlCollection controls)
    {
        foreach(Control control in controls)
        {
            if(control is Form)continue;
            yield return control;
            foreach(var child in Descendants(control.Controls))yield return child;
        }
    }
    private static Control? FindControl(Control.ControlCollection controls,string name)=>
        Descendants(controls).FirstOrDefault(c=>c.Name==name);
    private static Button? FindButton(Control.ControlCollection controls, string name)
    {
        foreach(Control control in controls)
        {
            if (control is Button b && b.Name == name) return b;
            var child = FindButton(control.Controls, name); if (child is not null) return child;
        }
        return null;
    }
}
