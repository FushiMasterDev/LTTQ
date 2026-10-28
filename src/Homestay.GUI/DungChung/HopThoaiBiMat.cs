namespace Homestay.GUI.Common;

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
