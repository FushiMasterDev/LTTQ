using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;

namespace Homestay.GUI.Shell;

public partial class FrmLogin : ScreenForm
{
    private AppServices? services;
    public FrmLogin()
    {
        InitializeComponent();
        showPassword.CheckedChanged += (_, _) => passwordBox.UseSystemPasswordChar = !showPassword.Checked;
        loginButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(usernameBox.Text) || string.IsNullOrEmpty(passwordBox.Text))
            { MessageBox.Show(this, "Vui lòng nhập mã nhân viên và mật khẩu.", "Đăng nhập"); return; }
            ErrorHandler.Pending(this);
            passwordBox.Clear();
        };
        AcceptButton = loginButton;
        CancelButton = cancelButton;
        HelpText = "Dùng mã nhân viên và mật khẩu được cấp. Sau 5 lần nhập sai, tài khoản bị khóa tạm 15 phút. Chưa có dịch vụ AuthService ở nhánh dev nên bố cục này chưa đăng nhập được.";
    }
    public FrmLogin(AppServices services) : this()
    { this.services = services; loginButton.Enabled = false; }
    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (services is null) return;
        try
        {
            await services.CheckConnectionAsync(services.Connections.Load(), CancellationToken.None);
            noticeLabel.Text = "Đã kết nối máy chủ · Chờ dịch vụ đăng nhập M1.4.";
            loginButton.Enabled = true;
        }
        catch (Exception exception)
        {
            noticeLabel.Text = "Chưa xác thực được kết nối. Vui lòng kiểm tra cấu hình và bộ dịch vụ BLL.";
            ErrorHandler.Show(exception, this);
        }
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    { passwordBox.Clear(); base.OnFormClosed(e); }
}
