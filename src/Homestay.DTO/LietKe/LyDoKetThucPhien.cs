namespace Homestay.DTO.Enums;

/// <summary>Lý do phiên đăng nhập kết thúc: đăng xuất, bỏ máy lâu, bị phiên khác thay, quản lý buộc thoát, tài khoản bị đổi.</summary>
public enum SessionEndReason
{
    Logout,
    Idle,
    Replaced,
    ForcedByManager,
    AccountChanged,
}
