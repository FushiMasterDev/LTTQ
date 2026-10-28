using Homestay.DTO.Enums;

namespace Homestay.BLL.Security;

/// <summary>Tập hợp các câu báo lỗi đăng nhập (sai thông tin, tài khoản ngưng, bị khóa đến lúc nào) để hiển thị thống nhất.</summary>
public static class LoginErrors
{
    public const string InvalidCredentials = "Tên đăng nhập hoặc mật khẩu không đúng";
    public const string InactiveAccount = "Tài khoản đã bị khóa hoặc ngừng hoạt động. Vui lòng liên hệ Quản lý.";
    public static string? ForCredentials(bool accountExists, bool passwordIsValid, UserStatus status, EmploymentStatus employmentStatus)
    {
        if (!accountExists || !passwordIsValid) return InvalidCredentials;
        return status != UserStatus.Active || employmentStatus != EmploymentStatus.Active ? InactiveAccount : null;
    }
    public static string LockedUntil(DateTime endUtc, DateTime nowUtc) =>
        $"Tài khoản tạm khóa do nhập sai nhiều lần. Vui lòng thử lại sau {Math.Max(1, Math.Ceiling((endUtc - nowUtc).TotalMinutes))} phút.";
}
