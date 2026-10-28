using Homestay.GUI.Common;
using Homestay.GUI.Configuration;
using Homestay.GUI.Infrastructure;

namespace Homestay.GUI.Shell;

public partial class FrmConnection : ScreenForm
{
    private AppServices? services;
    private bool tested;
    public FrmConnection()
    {
        InitializeComponent();
        saveButton.Enabled = false;
        showPassword.CheckedChanged += (_, _) => passwordBox.UseSystemPasswordChar = !showPassword.Checked;
        foreach (var control in new TextBox[] { serverBox, databaseBox, userBox, passwordBox })
            control.TextChanged += (_, _) => InvalidateTest();
        trustCertificate.CheckedChanged += (_, _) => InvalidateTest();
        testButton.Click += async (_, _) => await ErrorHandler.RunAsync(testButton, TestAsync);
        saveButton.Click += (_, _) => Save();
        previewButton.Click += (_, _) => { using var form = new FrmMain(); form.ShowDialog(this); };
        HelpText = "Nhập máy chủ, tên database và tài khoản SQL dành riêng cho ứng dụng. Kiểm tra kết nối trước khi lưu. Khi đã có cấu hình, chỉ quản trị Windows mở --configure mới được sửa.";
    }
    public FrmConnection(AppServices services) : this()
    {
        this.services = services;
        if (!services.Connections.MayConfigure) throw new UnauthorizedAccessException();
        serverBox.Text = services.Settings.Database.Server;
        databaseBox.Text = services.Settings.Database.Name;
        if (services.Connections.Exists)
        {
            var saved = services.Connections.Load();
            serverBox.Text = saved.Server; databaseBox.Text = saved.Database;
            userBox.Text = saved.Username; passwordBox.Text = saved.Password;
            trustCertificate.Checked = saved.TrustServerCertificate;
        }
    }
    private void InvalidateTest() { tested = false; saveButton.Enabled = false; }
    private ConnectionProfile ReadProfile()
    {
        if (new[] { serverBox.Text, databaseBox.Text, userBox.Text, passwordBox.Text }.Any(string.IsNullOrWhiteSpace))
            throw new InvalidDataException("Vui lòng nhập đủ máy chủ, database, tài khoản và mật khẩu SQL.");
        return new ConnectionProfile
        {
            Server = serverBox.Text.Trim(), Database = databaseBox.Text.Trim(),
            Username = userBox.Text.Trim(), Password = passwordBox.Text, Encrypt = true,
            TrustServerCertificate = trustCertificate.Checked
        };
    }
    private async Task TestAsync()
    {
        tested = false; saveButton.Enabled = false;
        var profile = ReadProfile();
        if (services is null) throw new NotImplementedException();
        root.Enabled = false;
        try
        {
            await services.CheckConnectionAsync(profile, CancellationToken.None);
            tested = true;
            noticeLabel.Text = "Kết nối và phiên bản dữ liệu hợp lệ. Có thể lưu cấu hình mã hóa.";
        }
        finally { profile.Password = ""; if (!IsDisposed) { root.Enabled = true; saveButton.Enabled = tested; } }
    }
    private void Save()
    {
        try
        {
            if (!tested || services is null) return;
            var profile = ReadProfile();
            try { services.Connections.Save(profile); }
            finally { profile.Password = ""; }
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception exception) { ErrorHandler.Show(exception, this); }
    }
    protected override void OnFormClosed(FormClosedEventArgs e)
    { passwordBox.Clear(); base.OnFormClosed(e); }
}
