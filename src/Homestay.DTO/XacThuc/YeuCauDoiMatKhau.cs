namespace Homestay.DTO.Auth;

/// <summary>Dữ liệu đổi mật khẩu: mật khẩu cũ, mật khẩu mới và nhập lại.</summary>
public sealed record ChangePasswordRequest
{
    public string OldPassword { get; init; } = "";
    public string NewPassword { get; init; } = "";
    public string ConfirmPassword { get; init; } = "";
}
