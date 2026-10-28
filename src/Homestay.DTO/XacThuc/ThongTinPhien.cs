using Homestay.DTO.Enums;

namespace Homestay.DTO.Auth;

/// <summary>Thông tin phiên đăng nhập: người dùng, chức vụ, khu vực phụ trách, thời điểm đăng nhập.</summary>
public sealed record SessionInfo
{
    public long SessionId { get; init; }
    public int UserId { get; init; }
    public int EmployeeId { get; init; }
    public string EmployeeCode { get; init; } = "";
    public string FullName { get; init; } = "";
    public Position Position { get; init; }
    public bool HasAllAreas { get; init; }
    public IReadOnlyList<int> AreaIds { get; init; } = [];
    public Guid SecurityStamp { get; init; }

    // DAL/BLL cung cấp thời điểm theo UTC.
    public DateTime LoginAtUtc { get; init; }

    public bool MustChangePassword { get; init; }
}
