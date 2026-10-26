using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;

namespace Homestay.GUI.Shell;

// Review shell only. Production does not enter here before authenticating through BLL.
public partial class FrmMain : ScreenForm
{
    private readonly Dictionary<string, TabPage> tabs = [];
    private bool changingRole;
    public FrmMain()
    {
        InitializeComponent();
        navigation.DisplayMember = nameof(ScreenEntry.Title);
        navigation.SelectedIndexChanged += (_, _) =>
        {
            if (!changingRole && navigation.SelectedItem is ScreenEntry screen) OpenScreen(screen.Id, screen.Create);
        };
        roleBox.SelectedIndexChanged += (_, _) => ChangePreviewRole();
        notificationsButton.Click += (_, _) => notificationSplit.Panel2Collapsed = !notificationSplit.Panel2Collapsed;
        closeTabButton.Click += (_, _) => CloseCurrentTab();
        exitButton.Click += (_, _) => Close();
        quickSearch.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ErrorHandler.Pending(this); }
        };
    }
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (!DesignMode && System.ComponentModel.LicenseManager.UsageMode != System.ComponentModel.LicenseUsageMode.Designtime)
            roleBox.SelectedIndex = 0;
    }
    private void ChangePreviewRole()
    {
        // Dispose all pages when changing the preview audience; no cross-role tabs linger.
        foreach(var page in tabs.Values.ToArray()) { workspaceTabs.TabPages.Remove(page); page.Dispose(); }
        tabs.Clear();
        changingRole = true;
        navigation.DataSource = ScreenRegistry.Layouts
            .Where(s => ScreenRegistry.IsPreviewAudience(s, roleBox.Text)).OrderBy(s => s.Id).ToArray();
        navigation.SelectedIndex = -1;
        changingRole = false;
        var dashboard = ScreenRegistry.Layouts.First(s => s.Id == "F05");
        OpenScreen(dashboard.Id, dashboard.Create);
    }
    public void OpenScreen(string key, Func<Form> create)
    {
        if (tabs.TryGetValue(key, out var existing)) { workspaceTabs.SelectedTab = existing; return; }
        var form = create();
        var page = new TabPage(form.Text);
        form.TopLevel = false; form.FormBorderStyle = FormBorderStyle.None;
        form.Dock = DockStyle.Fill;
        // Embedded forms should not force their outer minimum window size on a tab.
        form.MinimumSize = Size.Empty;
        page.Controls.Add(form);
        tabs.Add(key, page); workspaceTabs.TabPages.Add(page);
        form.FormClosed += (_, _) =>
        {
            tabs.Remove(key); workspaceTabs.TabPages.Remove(page);
            if (!page.IsDisposed) page.Dispose();
        };
        form.Show(); workspaceTabs.SelectedTab = page;
    }
    private void CloseCurrentTab()
    {
        if(workspaceTabs.SelectedTab?.Controls.OfType<Form>().FirstOrDefault() is Form form) form.Close();
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.K)) { quickSearch.Focus(); return true; }
        if (keyData == (Keys.Control | Keys.N))
        {
            if(roleBox.Text != "Kế toán")
            {
                var screen = ScreenRegistry.Layouts.First(s => s.Id == "F07");
                OpenScreen(screen.Id, screen.Create);
            }
            return true;
        }
        if(keyData == Keys.Escape) { CloseCurrentTab(); return true; }
        return base.ProcessCmdKey(ref msg, keyData);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if(e.CloseReason == CloseReason.UserClosing &&
            MessageBox.Show(this, "Kết thúc xem bố cục?", "Fuji Homestay",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) e.Cancel = true;
        base.OnFormClosing(e);
    }
}
