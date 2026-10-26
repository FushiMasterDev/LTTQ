using Homestay.BLL.Infrastructure;
using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;
using Homestay.GUI.Workflow;

namespace Homestay.GUI.Shell;

public partial class FrmMain : ScreenForm
{
    private readonly Dictionary<string, TabPage> tabs = [];
    private readonly System.Windows.Forms.Timer heartbeatTimer;
    private readonly System.Windows.Forms.Timer idleTimer;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ActivityTracker activity = new();
    private IAccountGateway? gateway;
    private AppServices? services;
    private int idleMinutes = 30;
    private string connectedStatus = "";
    private bool changingRole, ending, allowClose, heartbeatBusy, locked, runtimeDisposed;
    public event EventHandler? SignedOut;

    public FrmMain()
    {
        InitializeComponent();
        heartbeatTimer = new System.Windows.Forms.Timer(components!) { Interval = 60_000 };
        idleTimer = new System.Windows.Forms.Timer(components!) { Interval = 5_000 };
        navigation.DisplayMember = nameof(ScreenEntry.Title);
        navigation.SelectedIndexChanged += (_, _) =>
        {
            if (!changingRole && navigation.SelectedItem is ScreenEntry screen)
            {
                try { OpenScreen(screen.Id, screen.Create); }
                catch (Exception exception) { ErrorHandler.Show(exception, this); }
            }
        };
        roleBox.SelectedIndexChanged += (_, _) => { if (gateway is null) ChangePreviewRole(); };
        notificationsButton.Click += (_, _) => notificationSplit.Panel2Collapsed = !notificationSplit.Panel2Collapsed;
        closeTabButton.Click += (_, _) => CloseCurrentTab();
        exitButton.Click += async (_, _) => await EndSessionAsync(false);
        logoutButton.Click += async (_, _) => await EndSessionAsync(true);
        lockButton.Click += async (_, _) => await LockScreenAsync();
        generalHelpButton.Click += (_, _) => MessageBox.Show(this,
            "Chọn chức năng bên trái. F5 tải lại; Ctrl+F tìm; Esc đóng tab; Ctrl+Enter lưu hộp thoại.\n" +
            "Chức năng hiện theo vai trò. Không chia sẻ tài khoản đăng nhập.", "Hướng dẫn");
        quickSearch.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ErrorHandler.Pending(this); }
        };
        heartbeatTimer.Tick += async (_, _) => await HeartbeatAsync();
        idleTimer.Tick += async (_, _) =>
        {
            if (gateway is not null && !ending && !locked && activity.IdleFor >= TimeSpan.FromMinutes(idleMinutes))
                await LockScreenAsync();
        };
    }
    public FrmMain(IAccountGateway gateway, AppServices? services = null, int idleMinutes = 30) : this()
    {
        this.gateway = gateway; this.services = services; this.idleMinutes = idleMinutes;
        var session = gateway.Session ?? throw new SessionExpiredException();
        if (session.MustChangePassword) throw new SessionExpiredException("Cần đổi mật khẩu trước khi tiếp tục.");
        roleBox.Visible = false; quickSearch.Visible = false; notificationsButton.Visible = false;
        logoutButton.Visible = true; lockButton.Visible = true; userLabel.Visible = true;
        userLabel.Text = session.FullName + " · " + UiNames.Position(session.Position);
        banner.Visible = false;
        Text = "Fuji Homestay · " + UiNames.Position(session.Position);
        connectedStatus = sessionStatus.Text = $"{session.FullName} · {UiNames.Position(session.Position)} · Khu vực: " +
            (session.HasAllAreas ? "Toàn hệ thống" : session.AreaIds.Count == 0 ? "Chưa được gán" : string.Join(", ",session.AreaIds)) +
            $" · Đăng nhập: {UiFormat.VietnamTime(new DateTimeOffset(DateTime.SpecifyKind(session.LoginAtUtc,DateTimeKind.Utc))):HH:mm dd/MM/yyyy} · Đã kết nối · 0.2.0";
    }
    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (DesignMode || System.ComponentModel.LicenseManager.UsageMode == System.ComponentModel.LicenseUsageMode.Designtime) return;
        if (gateway is null) roleBox.SelectedIndex = 0;
        else
        {
            SetMenu(ScreenRegistry.Production(gateway, services));
            var welcome = new TabPage("Trang chủ");
            welcome.Controls.Add(new Label
            {
                Dock = DockStyle.Fill, AutoSize = false, Padding = new Padding(32),
                Text = "Chào " + gateway.Session!.FullName + ".\n\nChọn chức năng ở menu bên trái để làm việc.",
                Font = new Font("Segoe UI", 14), ForeColor = UiTheme.Ink
            });
            workspaceTabs.TabPages.Add(welcome); tabs["home"] = welcome;
            ErrorHandler.SessionExpired += HandleSessionExpired;
            System.Windows.Forms.Application.AddMessageFilter(activity);
            heartbeatTimer.Start(); idleTimer.Start();
        }
    }
    private void SetMenu(ScreenEntry[] entries)
    {
        changingRole = true;
        navigation.DataSource = entries;
        navigation.SelectedIndex = -1;
        changingRole = false;
    }
    private void ChangePreviewRole()
    {
        foreach(var page in tabs.Values.ToArray()) { workspaceTabs.TabPages.Remove(page); page.Dispose(); }
        tabs.Clear();
        SetMenu(ScreenRegistry.Layouts.Where(s => ScreenRegistry.IsPreviewAudience(s, roleBox.Text)).OrderBy(s=>s.Id).ToArray());
        var dashboard = ScreenRegistry.Layouts.First(s => s.Id == "F05");
        OpenScreen(dashboard.Id, dashboard.Create);
    }
    public void OpenScreen(string key, Func<Form> create)
    {
        if (gateway is not null && !ScreenRegistry.Production(gateway,services).Any(s=>s.Id==key.Split(':')[0]))
            throw new ForbiddenException();
        if (tabs.TryGetValue(key, out var existing)) { workspaceTabs.SelectedTab = existing; return; }
        var form = create();
        var page = new TabPage(form.Text);
        form.TopLevel = false; form.FormBorderStyle = FormBorderStyle.None;
        form.Dock = DockStyle.Fill; form.MinimumSize = Size.Empty;
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
        var page = workspaceTabs.SelectedTab;
        if (page?.Controls.OfType<Form>().FirstOrDefault() is Form form) form.Close();
        else if (page is not null) { tabs.Remove("home"); workspaceTabs.TabPages.Remove(page); page.Dispose(); }
    }
    private async Task HeartbeatAsync()
    {
        if (gateway is null || heartbeatBusy || ending) return;
        heartbeatBusy = true;
        try
        {
            if (!await gateway.HeartbeatAsync(lifetime.Token)) await EndSessionAsync(true, forced:true);
            else if (!IsDisposed) { sessionStatus.ForeColor = Color.DarkGreen; sessionStatus.Text = connectedStatus; }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            if (!IsDisposed && !ending)
            {
                sessionStatus.ForeColor = Color.Firebrick;
                sessionStatus.Text = "Chưa kiểm tra được kết nối. Các thao tác lưu vẫn được kiểm tra phiên tại máy chủ.";
                ErrorHandler.Logger?.Error(exception);
            }
        }
        finally { heartbeatBusy = false; }
    }
    private async Task LockScreenAsync()
    {
        if (gateway is null || ending || locked) return;
        locked = true; root.Enabled = false;
        try
        {
            using var dialog = new FrmLogin(gateway, unlock:true);
            if (dialog.ShowDialog(this) != DialogResult.OK)
                await EndSessionAsync(true, forced:true);
            else activity.Touch();
        }
        catch (SessionExpiredException) { await EndSessionAsync(true, forced:true); }
        catch (Exception exception) { ErrorHandler.Show(exception,this); }
        finally { locked = false; if (!IsDisposed && !ending) root.Enabled = true; }
    }
    private async void HandleSessionExpired()
    {
        if (!ending && !IsDisposed) await EndSessionAsync(true, forced:true);
    }
    private async Task EndSessionAsync(bool returnToLogin, bool forced = false)
    {
        if (ending || IsDisposed) return;
        if (!forced && MessageBox.Show(this,
            gateway is null ? "Kết thúc xem bố cục?" : returnToLogin ? "Đăng xuất tài khoản?" : "Thoát ứng dụng?",
            "Fuji Homestay",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes) return;
        ending = true; root.Enabled = false; heartbeatTimer.Stop(); idleTimer.Stop();
        try
        {
            if (gateway is not null)
            {
                try { await gateway.LogoutAsync(lifetime.Token); }
                catch (Exception exception)
                {
                    if (!forced) { ErrorHandler.Show(exception,this); ending=false; root.Enabled=true; heartbeatTimer.Start();idleTimer.Start();return; }
                    ErrorHandler.Logger?.Error(exception);
                }
            }
            allowClose=true;
            if (returnToLogin && gateway is not null) SignedOut?.Invoke(this,EventArgs.Empty);
            Close();
        }
        finally { if (!IsDisposed && !allowClose) { ending=false;root.Enabled=true; } }
    }
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == (Keys.Control | Keys.K) && gateway is null) { quickSearch.Focus(); return true; }
        if (keyData == (Keys.Control | Keys.N) && gateway is null && roleBox.Text != "Kế toán")
        { var screen=ScreenRegistry.Layouts.First(s=>s.Id=="F07");OpenScreen(screen.Id,screen.Create);return true; }
        if (keyData == Keys.Escape) { CloseCurrentTab();return true; }
        return base.ProcessCmdKey(ref msg,keyData);
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!allowClose && e.CloseReason == CloseReason.UserClosing)
        { e.Cancel=true; _=EndSessionAsync(false); }
        base.OnFormClosing(e);
    }
    private void DisposeRuntime()
    {
        if (runtimeDisposed) return;
        runtimeDisposed = true;
        ErrorHandler.SessionExpired -= HandleSessionExpired;
        System.Windows.Forms.Application.RemoveMessageFilter(activity);
        lifetime.Cancel(); lifetime.Dispose();
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    { DisposeRuntime(); base.OnFormClosed(e); }
}
