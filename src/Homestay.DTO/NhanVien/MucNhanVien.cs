using Homestay.DTO.Common;
using Homestay.DTO.Enums;

namespace Homestay.DTO.Staff;

/// <summary>Một nhân viên hiển thị trên danh sách (mã, tên, chức vụ, tình trạng, khu vực, lần đăng nhập cuối).</summary>
public sealed record EmployeeListItem
{
    public int EmployeeId { get; init; }
    public int UserId { get; init; }
    public string EmployeeCode { get; init; } = "";
    public string FullName { get; init; } = "";
    public string Phone { get; init; } = "";
    public Position Position { get; init; }
    public EmploymentStatus EmploymentStatus { get; init; }
    public UserStatus AccountStatus { get; init; }
    public bool HasAllAreas { get; init; }
    public string AreaNames { get; init; } = "";
    public DateTime? LastLoginAtUtc { get; init; }
    public bool HasOpenSession { get; init; }
    public byte[] RowVer { get; init; } = [];
}

/// <summary>Chi tiết một nhân viên: email, lý do khóa, phải đổi mật khẩu không, khu vực phụ trách.</summary>
public sealed record EmployeeDetail
{
    public EmployeeListItem Employee { get; init; } = new();
    public string? Email { get; init; }
    public string? LockReason { get; init; }
    public bool MustChangePassword { get; init; }
    public IReadOnlyList<Lookup> Areas { get; init; } = [];
}
