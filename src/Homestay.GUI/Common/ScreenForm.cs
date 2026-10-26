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
    protected override bool ProcessDialogKey(Keys keyData)
    {
        if (keyData == Keys.Enter && AcceptButton is null && ActiveControl is TextBox { Multiline: false })
        { SelectNextControl(ActiveControl, true, true, true, true); return true; }
        return base.ProcessDialogKey(keyData);
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { Close(); return true; }
        if (keyData is Keys.F5 or Keys.F2)
        { ErrorHandler.Pending(this); return true; }
        if (keyData == (Keys.Control | Keys.Enter))
        {
            var save = FindButton(Controls, "saveButton");
            if (save is not null && save.Enabled) { save.PerformClick(); return true; }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }
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
