using System.Text.Json;

namespace Homestay.GUI.Configuration;

/// <summary>Cấu hình ứng dụng đọc từ appsettings.json: kết nối CSDL và ghi log.</summary>
public sealed class AppSettings
{
    public DatabaseSettings Database { get; set; } = new();
    public LoggingSettings Logging { get; set; } = new();
    public static AppSettings Load(string path)
    {
        if (!File.Exists(path)) return new();
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("Tệp cấu hình rỗng.");
        if (settings.Database is null || settings.Logging is null ||
            settings.Database.CommandTimeoutSeconds is < 1 or > 300 ||
            settings.Logging.RetainDays is < 1 or > 365)
            throw new InvalidDataException("Tham số cấu hình không hợp lệ.");
        return settings;
    }
}
/// <summary>Phần cấu hình CSDL: máy chủ, tên database, mã hóa, thời gian chờ lệnh.</summary>
public sealed class DatabaseSettings
{
    public string Server { get; set; } = @".\SQLEXPRESS";
    public string Name { get; set; } = "HomestayDesktop";
    public bool Encrypt { get; set; } = true;
    public bool TrustServerCertificate { get; set; }
    public int CommandTimeoutSeconds { get; set; } = 30;
}
/// <summary>Phần cấu hình ghi log: thư mục và số ngày giữ log.</summary>
public sealed class LoggingSettings
{
    public string Folder { get; set; } = @"%LOCALAPPDATA%\HomestayDesktop\logs";
    public int RetainDays { get; set; } = 30;
}
