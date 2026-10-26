using Homestay.GUI.Common;

namespace Homestay.GUI.Shell;

public partial class FrmChangePassword : ScreenForm
{
    public FrmChangePassword()
    {
        InitializeComponent();
        showPassword.CheckedChanged += (_, _) =>
        {
            oldPassword.UseSystemPasswordChar = !showPassword.Checked;
            newPassword.UseSystemPasswordChar = !showPassword.Checked;
            confirmPassword.UseSystemPasswordChar = !showPassword.Checked;
        };
        HelpText = "Nhập mật khẩu hiện tại và mật khẩu mới hai lần. Quy tắc và việc thay đổi mật khẩu được kiểm tra bởi AuthService; chưa có ở M1.4.";
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        oldPassword.Clear(); newPassword.Clear(); confirmPassword.Clear();
        base.OnFormClosed(e);
    }
}
