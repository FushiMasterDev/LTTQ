using Homestay.DTO.Common;
using Homestay.DTO.Enums;

namespace Homestay.DTO.Staff;

// Employees has no RowVer. Use EmployeeDetail.Employee.RowVer (the linked Users token), as in M1.4.
/// <summary>Yêu cầu gán khu vực phụ trách cho nhân viên (hoặc cho phụ trách tất cả khu vực).</summary>
public sealed record SetEmployeeAreasRequest(int EmployeeId, IReadOnlyList<int> AreaIds, bool HasAllAreas, byte[] RowVer);
/// <summary>Các khu vực nhân viên đang phụ trách cùng chức vụ hiện tại.</summary>
public sealed record EmployeeAreasDetail(int EmployeeId, Position Position, bool HasAllAreas, IReadOnlyList<Lookup> Areas, byte[] RowVer);
/// <summary>Điều kiện tìm nhân viên theo khu vực.</summary>
public sealed record EmployeesByAreaRequest
{
    public PageRequest Page { get; init; } = new();
    public int AreaId { get; init; }
}
/// <summary>Yêu cầu đổi chức vụ nhân viên kèm lý do.</summary>
public sealed record ChangePositionRequest(int EmployeeId, Position Position, string Reason, byte[] RowVer);
/// <summary>Yêu cầu cho nhân viên nghỉ việc (hoặc tạm nghỉ) kèm lý do.</summary>
public sealed record TerminateEmploymentRequest(int EmployeeId, string Reason, byte[] RowVer)
{
    public EmploymentStatus Status { get; init; } = EmploymentStatus.Resigned;
}
/// <summary>Điều kiện tìm phiên đăng nhập của nhân viên.</summary>
public sealed record SessionSearchRequest
{
    public PageRequest Page { get; init; } = new();
    public int? EmployeeId { get; init; }
}
/// <summary>Một phiên đăng nhập của nhân viên (máy, giờ vào, lần hoạt động cuối, lý do kết thúc).</summary>
public sealed record EmployeeSessionItem(long SessionId, int EmployeeId, int UserId, string EmployeeCode, string EmployeeName,
    string MachineName, string AppVersion, DateTime LoginAtUtc, DateTime LastSeenAtUtc, DateTime? EndedAtUtc, SessionEndReason? EndReason);
/// <summary>Yêu cầu quản lý buộc đăng xuất một phiên kèm lý do.</summary>
public sealed record ForceLogoutRequest(long SessionId, string Reason);
