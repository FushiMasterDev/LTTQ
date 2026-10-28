namespace Homestay.DTO.Auth;

/// <summary>Dữ liệu đăng nhập: tên đăng nhập, mật khẩu, tên máy, phiên bản ứng dụng.</summary>
public sealed record LoginRequest
{
    public string Username { get; init; } = "";
    public string Password { get; init; } = "";
    public string MachineName { get; init; } = "";
    public string AppVersion { get; init; } = "";
}
