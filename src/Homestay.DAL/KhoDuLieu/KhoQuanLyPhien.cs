using Homestay.DAL.Infrastructure;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.DTO.Staff;

namespace Homestay.DAL.Repositories;

/// <summary>Truy vấn phiên đăng nhập: danh sách phiên, lấy người dùng của một phiên.</summary>
public sealed class SessionManagementRepository
{
    public async Task<PagedResult<EmployeeSessionItem>> ListAsync(DbSession db, SessionSearchRequest request, bool openOnly, CancellationToken ct)
    {
        const string from = """
             FROM dbo.UserSessions s JOIN dbo.Employees e ON e.UserId=s.UserId
             WHERE (@open=0 OR s.EndedAt IS NULL) AND (@employee IS NULL OR e.Id=@employee)
             AND (@search=N'' OR e.EmployeeCode LIKE @search ESCAPE '~' OR e.FullName LIKE @search ESCAPE '~' OR s.MachineName LIKE @search ESCAPE '~')
            """;
        void Parameters(Microsoft.Data.SqlClient.SqlCommand cmd)
        {
            cmd.Parameters.AddBoolean("@open", openOnly); cmd.Parameters.AddInt32("@employee", request.EmployeeId);
            cmd.Parameters.AddNVarChar("@search", 500, StaffReadSql.Like(request.Page.SearchText));
        }
        using var count = db.CreateCommand("SELECT COUNT(*)" + from); Parameters(count);
        var total = (int)(await db.ScalarAsync(count, ct).ConfigureAwait(false))!;
        var column = request.Page.SortColumn switch { "EmployeeCode" => "e.EmployeeCode", "MachineName" => "s.MachineName", "LastSeenAtUtc" => "s.LastSeenAt", _ => "s.LoginAt" };
        using var command = db.CreateCommand("SELECT s.*,e.Id AS EmployeeId,e.EmployeeCode,e.FullName" + from + $" ORDER BY {column} {(request.Page.SortDescending ? "DESC" : "ASC")},s.Id OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY;");
        Parameters(command); StaffReadSql.PageParameters(command, request.Page);
        return new() { Items = await db.QueryAsync(command, r => new EmployeeSessionItem(r.GetInt64("Id"), r.GetInt32("EmployeeId"), r.GetInt32("UserId"),
            r.GetString("EmployeeCode"), r.GetString("FullName"), r.GetString("MachineName"), r.GetString("AppVersion"), r.GetUtcDateTime("LoginAt"),
            r.GetUtcDateTime("LastSeenAt"), r.GetNullableUtcDateTime("EndedAt"), r.GetNullableString("EndReason") is { } reason ? Enum.Parse<SessionEndReason>(reason) : null), ct).ConfigureAwait(false),
            TotalCount = total, PageNumber = request.Page.PageNumber, PageSize = request.Page.PageSize };
    }
    public async Task<int?> GetUserIdAsync(DbSession db, long sessionId, CancellationToken ct)
    {
        using var command = db.CreateCommand("SELECT UserId FROM dbo.UserSessions WHERE Id=@id;");
        command.Parameters.AddInt64("@id", sessionId);
        return await db.ScalarAsync(command, ct).ConfigureAwait(false) as int?;
    }
}
