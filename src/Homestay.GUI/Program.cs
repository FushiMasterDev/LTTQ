using Homestay.GUI.Configuration;
using Homestay.GUI.Infrastructure;
using Homestay.GUI.Shell;
using Homestay.GUI.Workflow;

namespace Homestay.GUI;

/// <summary>Điểm khởi chạy ứng dụng: cấu hình WinForms, bắt lỗi toàn cục và mở luồng chính.</summary>
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ErrorHandler.Show(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception exception) ErrorHandler.Logger?.Error(exception);
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        { ErrorHandler.Logger?.Error(e.Exception); e.SetObserved(); };
        try
        {
            var settings = AppSettings.Load(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
            var profileIndex = Array.FindIndex(args, x => x.Equals("--connection-file", StringComparison.OrdinalIgnoreCase));
            if (profileIndex >= 0 && profileIndex + 1 >= args.Length)
                throw new InvalidDataException("Thiếu đường dẫn sau --connection-file.");
            var profilePath = profileIndex >= 0 ? Path.GetFullPath(args[profileIndex + 1]) : Path.Combine(AppContext.BaseDirectory, "connection.dat");
            var services = new AppServices(settings, profilePath);
            ErrorHandler.Logger = services.Logger;
            if (args.Contains("--preview", StringComparer.OrdinalIgnoreCase))
            { Application.Run(new FrmMain()); return; }
            var configure = args.Contains("--configure", StringComparer.OrdinalIgnoreCase);
            if (configure && !ConnectionStore.IsAdministrator())
                throw new UnauthorizedAccessException();
            if (configure || !services.Connections.Exists)
            {
                using var form = new FrmConnection(services);
                if (form.ShowDialog() != DialogResult.OK) return;
                if (configure) return;
            }
            // The schema check is awaited on a visible startup form, never skipped.
            Application.Run(new DesktopContext(services));
        }
        catch (Exception exception) { ErrorHandler.Show(exception); }
    }
}
