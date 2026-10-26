using Homestay.DAL.Infrastructure;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.DTO.Staff;

namespace Homestay.DAL.Repositories;

public sealed class StaffRepository
{
    private const string SelectColumns = """
        SELECT e.Id AS EmployeeId, u.Id AS UserId, e.EmployeeCode, e.FullName, e.Phone, e.Position,
            e.EmploymentStatus, u.Status AS AccountStatus, e.HasAllAreas, u.LastLoginAt, u.RowVer,
            COALESCE((SELECT STRING_AGG(CONVERT(nvarchar(max), a.Name), N', ') WITHIN GROUP (ORDER BY a.Name)
                FROM dbo.EmployeeAreas ea JOIN dbo.Areas a ON a.Id=ea.AreaId WHERE ea.EmployeeId=e.Id), N'') AS AreaNames,
            CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.UserSessions s WHERE s.UserId=u.Id AND s.EndedAt IS NULL) THEN 1 ELSE 0 END AS bit) AS HasOpenSession
        FROM dbo.Employees e JOIN dbo.Users u ON u.Id=e.UserId
        """;

    private static EmployeeListItem Map(Microsoft.Data.SqlClient.SqlDataReader r) => new()
    {
        EmployeeId = r.GetInt32("EmployeeId"), UserId = r.GetInt32("UserId"), EmployeeCode = r.GetString("EmployeeCode"),
        FullName = r.GetString("FullName"), Phone = r.GetString("Phone"), Position = r.GetEnum<Position>("Position"),
        EmploymentStatus = r.GetEnum<EmploymentStatus>("EmploymentStatus"), AccountStatus = r.GetEnum<UserStatus>("AccountStatus"),
        HasAllAreas = r.GetBoolean("HasAllAreas"), AreaNames = r.GetString("AreaNames"), LastLoginAtUtc = r.GetNullableUtcDateTime("LastLoginAt"),
        HasOpenSession = r.GetBoolean("HasOpenSession"), RowVer = r.GetRowVersion("RowVer")
    };

    public async Task<PagedResult<EmployeeListItem>> ListAsync(DbSession db, EmployeeSearchRequest request, CancellationToken ct)
    {
        const string where = """
             WHERE (@search=N'' OR e.EmployeeCode LIKE @search ESCAPE '~' OR e.FullName LIKE @search ESCAPE '~' OR e.Phone LIKE @search ESCAPE '~')
               AND (@position IS NULL OR e.Position=@position) AND (@status IS NULL OR u.Status=@status)
            """;
        var page = request.Page;
        var column = page.SortColumn switch
        {
            "FullName" => "e.FullName", "Phone" => "e.Phone", "Position" => "e.Position",
            "EmploymentStatus" => "e.EmploymentStatus", "AccountStatus" => "u.Status", "LastLoginAtUtc" => "u.LastLoginAt",
            _ => "e.EmployeeCode"
        };
        var search = page.SearchText?.Trim() ?? "";
        if (search.Length > 0) search = "%" + search.Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";
        void Parameters(Microsoft.Data.SqlClient.SqlCommand cmd)
        {
            cmd.Parameters.AddNVarChar("@search", 500, search);
            cmd.Parameters.AddEnum("@position", 20, request.Position);
            cmd.Parameters.AddEnum("@status", 20, request.AccountStatus);
        }
        using var count = db.CreateCommand("SELECT COUNT(*) FROM dbo.Employees e JOIN dbo.Users u ON u.Id=e.UserId" + where);
        Parameters(count);
        var total = (int)(await db.ScalarAsync(count, ct).ConfigureAwait(false))!;
        using var command = db.CreateCommand(SelectColumns + where + $" ORDER BY {column} {(page.SortDescending ? "DESC" : "ASC")}, e.Id OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY;");
        Parameters(command);
        command.Parameters.AddInt32("@offset", checked((page.PageNumber - 1) * page.PageSize));
        command.Parameters.AddInt32("@size", page.PageSize);
        return new() { Items = await db.QueryAsync(command, Map, ct).ConfigureAwait(false), TotalCount = total, PageNumber = page.PageNumber, PageSize = page.PageSize };
    }

    public async Task<EmployeeDetail?> GetAsync(DbSession db, int employeeId, CancellationToken ct)
    {
        using var command = db.CreateCommand(SelectColumns + " WHERE e.Id=@id;");
        command.Parameters.AddInt32("@id", employeeId);
        var item = (await db.QueryAsync(command, Map, ct).ConfigureAwait(false)).SingleOrDefault();
        if (item is null) return null;
        using var extra = db.CreateCommand("SELECT e.Email, u.LockReason, u.MustChangePassword FROM dbo.Employees e JOIN dbo.Users u ON u.Id=e.UserId WHERE e.Id=@id;");
        extra.Parameters.AddInt32("@id", employeeId);
        var data = (await db.QueryAsync(extra, r => (Email: r.GetNullableString("Email"), LockReason: r.GetNullableString("LockReason"), MustChangePassword: r.GetBoolean("MustChangePassword")), ct).ConfigureAwait(false)).Single();
        using var areas = db.CreateCommand("SELECT a.Id, a.Name, a.IsActive FROM dbo.EmployeeAreas ea JOIN dbo.Areas a ON a.Id=ea.AreaId WHERE ea.EmployeeId=@id ORDER BY a.Name;");
        areas.Parameters.AddInt32("@id", employeeId);
        return new() { Employee = item, Email = data.Email, LockReason = data.LockReason, MustChangePassword = data.MustChangePassword,
            Areas = await db.QueryAsync(areas, r => new Lookup { Id = r.GetInt32("Id"), Name = r.GetString("Name"), IsActive = r.GetBoolean("IsActive") }, ct).ConfigureAwait(false) };
    }

    public async Task<int> NextEmployeeNumberAsync(UnitOfWork db, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            IF NOT EXISTS (SELECT 1 FROM dbo.Counters WITH (UPDLOCK, HOLDLOCK) WHERE CounterKey='Employee')
                INSERT dbo.Counters (CounterKey, LastValue)
                SELECT 'Employee', COALESCE(MAX(TRY_CONVERT(int, SUBSTRING(EmployeeCode, 3, 18))), 0)
                FROM dbo.Employees WHERE EmployeeCode LIKE 'NV%';
            UPDATE dbo.Counters SET LastValue=LastValue+1 OUTPUT INSERTED.LastValue WHERE CounterKey='Employee';
            """);
        return (int)(await db.ScalarAsync(command, ct).ConfigureAwait(false))!;
    }

    public async Task<(int EmployeeId, int UserId)> CreateAsync(UnitOfWork db, string code, CreateEmployeeRequest request, string hash, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            INSERT dbo.Users (Username, PasswordHash, Status, MustChangePassword, CreatedAt)
            VALUES (@code, @hash, 'Active', 1, @now);
            DECLARE @user int=CONVERT(int, SCOPE_IDENTITY());
            INSERT dbo.Employees (UserId, EmployeeCode, FullName, Phone, Email, Position, EmploymentStatus, HasAllAreas, CreatedAt)
            VALUES (@user, @code, @name, @phone, @email, @position, 'Active', 0, @now);
            SELECT CONVERT(int, SCOPE_IDENTITY()) AS EmployeeId, @user AS UserId;
            """);
        command.Parameters.AddVarChar("@code", 20, code);
        command.Parameters.AddVarChar("@hash", 255, hash);
        command.Parameters.AddNVarChar("@name", 100, request.FullName);
        command.Parameters.AddVarChar("@phone", 16, request.Phone);
        command.Parameters.AddNVarChar("@email", 256, request.Email);
        command.Parameters.AddEnum("@position", 20, (Position?)request.Position);
        command.Parameters.AddUtcDateTime("@now", now);
        return (await db.QueryAsync(command, r => (r.GetInt32("EmployeeId"), r.GetInt32("UserId")), ct).ConfigureAwait(false)).Single();
    }

    public async Task<int> CountOtherActiveManagersAsync(UnitOfWork db, int excludedUserId, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            SELECT COUNT(*) FROM dbo.Users u JOIN dbo.Employees e ON e.UserId=u.Id
            WHERE e.Position='Manager' AND e.EmploymentStatus='Active' AND u.Status='Active' AND u.Id<>@id;
            """);
        command.Parameters.AddInt32("@id", excludedUserId);
        return (int)(await db.ScalarAsync(command, ct).ConfigureAwait(false))!;
    }

    public async Task<int> ChangeStatusAsync(UnitOfWork db, AccountActionRequest request, UserStatus oldStatus, UserStatus newStatus, Guid stamp, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            UPDATE dbo.Users SET Status=@status, LockReason=@reason, SecurityStamp=@stamp, UpdatedAt=@now,
                FailedLoginCount=CASE WHEN @status='Active' THEN 0 ELSE FailedLoginCount END,
                LockoutEndUtc=CASE WHEN @status='Active' THEN NULL ELSE LockoutEndUtc END
            WHERE Id=@id AND Status=@oldStatus AND RowVer=@version;
            """);
        command.Parameters.AddInt32("@id", request.UserId);
        command.Parameters.AddEnum("@status", 20, (UserStatus?)newStatus);
        command.Parameters.AddEnum("@oldStatus", 20, (UserStatus?)oldStatus);
        command.Parameters.AddNVarChar("@reason", 500, request.Reason);
        command.Parameters.AddGuid("@stamp", stamp);
        command.Parameters.AddRowVersion("@version", request.RowVer);
        command.Parameters.AddUtcDateTime("@now", now);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<int> ResetPasswordAsync(UnitOfWork db, AccountActionRequest request, UserStatus oldStatus, string hash, Guid stamp, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            UPDATE dbo.Users SET PasswordHash=@hash, PasswordChangedAt=@now, MustChangePassword=1,
                SecurityStamp=@stamp, FailedLoginCount=0, LockoutEndUtc=NULL, UpdatedAt=@now
            WHERE Id=@id AND Status=@status AND RowVer=@version;
            """);
        command.Parameters.AddInt32("@id", request.UserId);
        command.Parameters.AddEnum("@status", 20, (UserStatus?)oldStatus);
        command.Parameters.AddVarChar("@hash", 255, hash);
        command.Parameters.AddGuid("@stamp", stamp);
        command.Parameters.AddRowVersion("@version", request.RowVer);
        command.Parameters.AddUtcDateTime("@now", now);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }
}
