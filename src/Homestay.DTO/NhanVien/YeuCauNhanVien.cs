using Homestay.DTO.Common;
using Homestay.DTO.Enums;

namespace Homestay.DTO.Staff;

/// <summary>Điều kiện tìm nhân viên: chức vụ, trạng thái tài khoản.</summary>
public sealed record EmployeeSearchRequest
{
    public PageRequest Page { get; init; } = new();
    public Position? Position { get; init; }
    public UserStatus? AccountStatus { get; init; }
}

/// <summary>Dữ liệu nhập để tạo nhân viên và tài khoản đăng nhập mới.</summary>
public sealed record CreateEmployeeRequest
{
    public string FullName { get; init; } = "";
    public string Phone { get; init; } = "";
    public string? Email { get; init; }
    public Position Position { get; init; }
}

/// <summary>Kết quả tạo nhân viên: mã nhân viên và mật khẩu tạm để giao cho người đó.</summary>
public sealed record CreateEmployeeResult(int EmployeeId, int UserId, string EmployeeCode, string TemporaryPassword);
/// <summary>Kết quả đặt lại mật khẩu: mật khẩu tạm mới.</summary>
public sealed record ResetPasswordResult(string TemporaryPassword);

/// <summary>Yêu cầu thao tác trên tài khoản (khóa, mở khóa, đặt lại mật khẩu) kèm lý do.</summary>
public sealed record AccountActionRequest
{
    public int UserId { get; init; }
    public byte[] RowVer { get; init; } = [];
    public string? Reason { get; init; }
}
