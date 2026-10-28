using Homestay.DTO.Common;
using Homestay.DTO.Enums;

namespace Homestay.DTO.Staff;

public sealed record EmployeeSearchRequest
{
    public PageRequest Page { get; init; } = new();
    public Position? Position { get; init; }
    public UserStatus? AccountStatus { get; init; }
}

public sealed record CreateEmployeeRequest
{
    public string FullName { get; init; } = "";
    public string Phone { get; init; } = "";
    public string? Email { get; init; }
    public Position Position { get; init; }
}

public sealed record CreateEmployeeResult(int EmployeeId, int UserId, string EmployeeCode, string TemporaryPassword);
public sealed record ResetPasswordResult(string TemporaryPassword);

public sealed record AccountActionRequest
{
    public int UserId { get; init; }
    public byte[] RowVer { get; init; } = [];
    public string? Reason { get; init; }
}
