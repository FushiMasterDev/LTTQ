using Homestay.BLL.Infrastructure;
using Homestay.DTO.Auth;
using Homestay.GUI.Common;
using Homestay.GUI.Infrastructure;
using Homestay.GUI.Workflow;

namespace Homestay.GUI.Shell;

public partial class FrmChangePassword : ScreenForm
{
    private IAccountGateway? gateway;
    private bool busy;
    public FrmChangePassword()
    {
        InitializeComponent();
        saveButton.Tag = null;
        showPassword.CheckedChanged += (_, _) =>
        {
            oldPassword.UseSystemPasswordChar = !showPassword.Checked;
            newPassword.UseSystemPasswordChar = !showPassword.Checked;
            confirmPassword.UseSystemPasswordChar = !showPassword.Checked;
        };
        saveButton.Click += async (_, _) => await ErrorHandler.RunAsync(saveButton, SaveAsync);
        AcceptButton = saveButton; CancelButton = cancelButton;
        HelpText = "Nhập mật khẩu hiện tại và mật khẩu mới hai lần. Mật khẩu mới cần ít nhất 8 ký tự, có chữ và số, khác mật khẩu cũ và mã nhân viên.";
    }
    public FrmChangePassword(IAccountGateway gateway, bool required = false) : this()
    {
        this.gateway = gateway;
        if (required)
        {
            noticeLabel.Text = "Bạn đang dùng mật khẩu tạm. Hãy đổi mật khẩu trước khi tiếp tục.";
            cancelButton.Text = "Đăng xuất";
        }
    }
    private async Task SaveAsync()
    {
        if (gateway is null) throw new NotImplementedException();
        busy = true; root.Enabled = false;
        try
        {
            await gateway.ChangePasswordAsync(new ChangePasswordRequest
            {
                OldPassword = oldPassword.Text, NewPassword = newPassword.Text,
                ConfirmPassword = confirmPassword.Text
            });
            busy = false; DialogResult = DialogResult.OK; Close();
        }
        catch (SessionExpiredException)
        { busy = false; DialogResult = DialogResult.Cancel; Close(); throw; }
        finally { busy = false; if (!IsDisposed) root.Enabled = true; }
    }
    protected override void OnFormClosing(FormClosingEventArgs e)
    { if (busy && e.CloseReason == CloseReason.UserClosing) e.Cancel = true; base.OnFormClosing(e); }
    protected override void OnFormClosed(FormClosedEventArgs e)
    { oldPassword.Clear(); newPassword.Clear(); confirmPassword.Clear(); base.OnFormClosed(e); }
}
