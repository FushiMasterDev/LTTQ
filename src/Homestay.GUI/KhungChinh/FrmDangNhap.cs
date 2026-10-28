using Homestay.BLL.Infrastructure;
using Homestay.DTO.Auth;
using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;
using Homestay.GUI.Workflow;

namespace Homestay.GUI.Shell;

/// <summary>Màn hình đăng nhập.</summary>
public partial class FrmLogin : ScreenForm
{
    private IAccountGateway? gateway;
    private AppServices? startup;
    private bool unlockMode;
    private bool busy;
    private bool signedIn;
    public event EventHandler? Authenticated;
    public FrmLogin()
    {
        InitializeComponent();
        showPassword.CheckedChanged += (_, _) => passwordBox.UseSystemPasswordChar = !showPassword.Checked;
        loginButton.Click += async (_, _) => await ErrorHandler.RunAsync(loginButton, SignInAsync);
        AcceptButton = loginButton;
        CancelButton = cancelButton;
        HelpText = "Dùng mã nhân viên và mật khẩu được cấp. Tài khoản dùng mật khẩu tạm phải đổi trước khi vào phần mềm. Nếu bị khóa tạm, chờ thời gian thông báo rồi thử lại.";
    }
    public FrmLogin(IAccountGateway gateway, AppServices? startup = null, bool unlock = false) : this()
    {
        this.gateway = gateway; this.startup = startup; unlockMode = unlock;
        loginButton.Enabled = startup is null || startup.IsInitialized;
        if (unlock)
        {
            var session = gateway.Session ?? throw new SessionExpiredException();
            usernameBox.Text = session.EmployeeCode; usernameBox.ReadOnly = true;
            Text = "F01 · Khóa màn hình"; titleLabel.Text = "Khóa màn hình";
            loginButton.Text = "Mở khóa"; cancelButton.Text = "Đăng xuất";
            noticeLabel.Text = session.FullName + " · Nhập mật khẩu để tiếp tục.";
        }
    }
    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (startup is null || unlockMode) return;
        busy = true;
        try
        {
            if (!startup.IsInitialized) await startup.InitializeAsync();
            noticeLabel.Text = "Đã kết nối máy chủ · Vui lòng đăng nhập.";
            loginButton.Enabled = true;
        }
        catch (Exception exception)
        {
            noticeLabel.Text = "Chưa kiểm tra được kết nối và phiên bản dữ liệu. Vui lòng kiểm tra cấu hình.";
            ErrorHandler.Show(exception, this);
        }
        finally { busy = false; }
    }
    private async Task SignInAsync()
    {
        if (gateway is null) throw new NotImplementedException();
        if (string.IsNullOrWhiteSpace(usernameBox.Text) || string.IsNullOrEmpty(passwordBox.Text))
            throw new InvalidDataException("Vui lòng nhập mã nhân viên và mật khẩu.");
        busy = true; root.Enabled = false;
        try
        {
            if (unlockMode)
            {
                await gateway.UnlockAsync(passwordBox.Text);
                passwordBox.Clear(); signedIn = true; DialogResult = DialogResult.OK; Close();
                return;
            }
            var request = new LoginRequest
            {
                Username = usernameBox.Text.Trim(), Password = passwordBox.Text,
                MachineName = startup?.MachineName ?? Environment.MachineName,
                AppVersion = startup?.AppVersion ?? "0.2.0"
            };
            var result = await new LoginFlow(gateway).SignInAsync(request, () =>
            {
                passwordBox.Clear();
                using var dialog = new FrmChangePassword(gateway, required: true);
                return Task.FromResult(dialog.ShowDialog(this) == DialogResult.OK);
            });
            passwordBox.Clear();
            if (result is null)
            { noticeLabel.Text = "Đã đăng xuất. Cần đổi mật khẩu tạm trước khi tiếp tục."; return; }
            signedIn = true;
            Authenticated?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            busy = false;
            if (!IsDisposed) { root.Enabled = true; passwordBox.Clear(); }
        }
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (busy && !signedIn && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
        else if (!unlockMode && !signedIn && e.CloseReason == CloseReason.UserClosing &&
            MessageBox.Show(this, "Thoát ứng dụng?", "Fuji Homestay",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) e.Cancel = true;
        base.OnFormClosing(e);
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    { passwordBox.Clear(); base.OnFormClosed(e); }
}
