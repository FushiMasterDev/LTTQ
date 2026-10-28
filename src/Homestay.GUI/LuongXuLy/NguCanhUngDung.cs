using Homestay.GUI.Shell;

namespace Homestay.GUI.Workflow;

/// <summary>Điều khiển vòng đời ứng dụng: cấu hình kết nối → đăng nhập → màn hình chính → đăng xuất/khóa màn hình.</summary>
internal sealed class DesktopContext : ApplicationContext
{
    private readonly AppServices services;
    public DesktopContext(AppServices services) { this.services = services; ShowLogin(); }
    private void ShowLogin()
    {
        services.ResetForSignIn();
        var login = new FrmLogin(services, services);
        MainForm = login;
        login.Authenticated += (_, _) =>
        {
            var main = new FrmMain(services, services, services.IdleMinutes);
            main.SignedOut += (_, _) => ShowLogin();
            MainForm = main;
            main.Show();
            login.Close();
        };
        login.Show();
    }
}
