using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;

namespace Homestay.GUI.Configuration;

// Only this encrypted file contains SQL credentials. Never serialize a profile to logs.
/// <summary>Thông tin kết nối SQL Server người dùng đã nhập (máy chủ, database, tài khoản).</summary>
public sealed class ConnectionProfile
{
    public string Server { get; set; } = "";
    public string Database { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool Encrypt { get; set; } = true;
    public bool TrustServerCertificate { get; set; }
}
/// <summary>Lưu/đọc thông tin kết nối CSDL ra tập tin được mã hóa theo máy (DPAPI); chỉ quản trị viên được cấu hình lại.</summary>
public sealed class ConnectionStore(string path)
{
    public string Path { get; } = System.IO.Path.GetFullPath(path);
    public bool Exists => File.Exists(Path);
    public bool MayConfigure => !Exists || IsAdministrator();
    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }
    public ConnectionProfile Load()
    {
        var bytes = ProtectedData.Unprotect(File.ReadAllBytes(Path), null, DataProtectionScope.LocalMachine);
        try { return JsonSerializer.Deserialize<ConnectionProfile>(bytes)
            ?? throw new InvalidDataException("Không đọc được cấu hình kết nối."); }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
    public void Save(ConnectionProfile profile)
    {
        if (!MayConfigure) throw new UnauthorizedAccessException("Cần quyền quản trị Windows để đổi kết nối.");
        var bytes = JsonSerializer.SerializeToUtf8Bytes(profile);
        try
        {
            var encrypted = ProtectedData.Protect(bytes, null, DataProtectionScope.LocalMachine);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            var temp = Path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temp, encrypted); File.Move(temp, Path, true); }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        finally { CryptographicOperations.ZeroMemory(bytes); }
    }
}
