namespace Homestay.DTO.Auth;

public sealed record ChangePasswordRequest
{
    public string OldPassword { get; init; } = "";
    public string NewPassword { get; init; } = "";
    public string ConfirmPassword { get; init; } = "";
}
