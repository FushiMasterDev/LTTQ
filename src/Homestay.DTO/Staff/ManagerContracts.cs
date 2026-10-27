using Homestay.DTO.Common;
using Homestay.DTO.Enums;

namespace Homestay.DTO.Staff;

// Employees has no RowVer. Use EmployeeDetail.Employee.RowVer (the linked Users token), as in M1.4.
public sealed record SetEmployeeAreasRequest(int EmployeeId, IReadOnlyList<int> AreaIds, bool HasAllAreas, byte[] RowVer);
public sealed record EmployeeAreasDetail(int EmployeeId, Position Position, bool HasAllAreas, IReadOnlyList<Lookup> Areas, byte[] RowVer);
public sealed record EmployeesByAreaRequest
{
    public PageRequest Page { get; init; } = new();
    public int AreaId { get; init; }
}
public sealed record ChangePositionRequest(int EmployeeId, Position Position, string Reason, byte[] RowVer);
public sealed record TerminateEmploymentRequest(int EmployeeId, string Reason, byte[] RowVer)
{
    public EmploymentStatus Status { get; init; } = EmploymentStatus.Resigned;
}
public sealed record SessionSearchRequest
{
    public PageRequest Page { get; init; } = new();
    public int? EmployeeId { get; init; }
}
public sealed record EmployeeSessionItem(long SessionId, int EmployeeId, int UserId, string EmployeeCode, string EmployeeName,
    string MachineName, string AppVersion, DateTime LoginAtUtc, DateTime LastSeenAtUtc, DateTime? EndedAtUtc, SessionEndReason? EndReason);
public sealed record ForceLogoutRequest(long SessionId, string Reason);
