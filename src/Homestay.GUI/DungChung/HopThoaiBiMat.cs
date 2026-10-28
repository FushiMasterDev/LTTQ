namespace Homestay.GUI.Common;

/// <summary>Hộp thoại hiển thị thông tin bí mật một lần (ví dụ mật khẩu tạm) để người dùng sao chép.</summary>
public partial class SecretDialog : ScreenForm
{
    public SecretDialog()
    {
        InitializeComponent();
        closeButton.Click+=(_,_)=>Close();
        AcceptButton=closeButton;
    }
    public SecretDialog(string account,string secret) : this()
    {
        accountLabel.Text="Tài khoản: "+account;
        secretBox.Text=secret;
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    { secretBox.Clear();base.OnFormClosed(e); }
}
