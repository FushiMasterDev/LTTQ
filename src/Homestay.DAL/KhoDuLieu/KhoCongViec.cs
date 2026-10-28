using Homestay.DAL.Infrastructure;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.DTO.Tasks;
using Microsoft.Data.SqlClient;

namespace Homestay.DAL.Repositories;

/// <summary>Truy vấn bảng công việc: danh sách, đếm việc quá hạn, chi tiết, tạo, sửa, đổi trạng thái.</summary>
public sealed class TaskRepository
{
    private const string From = """
         FROM dbo.WorkTasks t JOIN dbo.Employees e ON e.Id=t.AssigneeEmployeeId
         LEFT JOIN dbo.Bookings b ON b.Id=t.BookingId
        """;
    private const string Columns = """
        t.*,e.FullName AS AssigneeName,b.Code AS BookingCode,
        (SELECT MIN(a.CreatedAt) FROM dbo.AuditLogs a WHERE a.TableName='WorkTasks' AND a.RecordId=CONVERT(varchar(50),t.Id) AND a.ActionCode='Task.Start') AS StartedAt,
        CAST(CASE WHEN t.Status='Open' AND t.DueAt<@now THEN 1 ELSE 0 END AS bit) AS IsOverdue
        """;
    public const string OverdueCondition = "t.Status='Open' AND t.DueAt<@now";
    private static string Where(string scope) => $"""
         WHERE ({scope}) AND (@assignee IS NULL OR t.AssigneeEmployeeId=@assignee) AND (@status IS NULL OR t.Status=@status)
         AND (@overdue=0 OR ({OverdueCondition})) AND (@search=N'' OR t.Title LIKE @search ESCAPE '~' OR b.Code LIKE @search ESCAPE '~' OR e.FullName LIKE @search ESCAPE '~')
        """;
    private static WorkTaskItem Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32("Id"), Title = r.GetString("Title"), Description = r.GetNullableString("Description"),
        AssigneeEmployeeId = r.GetInt32("AssigneeEmployeeId"), AssigneeName = r.GetString("AssigneeName"), CreatedByUserId = r.GetInt32("CreatedByUserId"),
        BookingId = r.GetNullableInt32("BookingId"), BookingCode = r.GetNullableString("BookingCode"), HomestayId = r.GetNullableInt32("HomestayId"),
        Priority = (WorkTaskPriority)r.GetByte(r.GetOrdinal("Priority")), Status = r.GetEnum<WorkTaskStatus>("Status"),
        DueAtUtc = r.GetNullableUtcDateTime("DueAt"), CreatedAtUtc = r.GetUtcDateTime("CreatedAt"), StartedAtUtc = r.GetNullableUtcDateTime("StartedAt"),
        CompletedAtUtc = r.GetNullableUtcDateTime("CompletedAt"), ResultNote = r.GetNullableString("ResultNote"), IsOverdue = r.GetBoolean("IsOverdue"), RowVer = r.GetRowVersion("RowVer")
    };
    public async Task<PagedResult<WorkTaskItem>> ListAsync(DbSession db, WorkTaskSearchRequest request, string scope, Action<SqlCommand> addScope, DateTime now, CancellationToken ct)
    {
        void Parameters(SqlCommand cmd)
        {
            addScope(cmd); cmd.Parameters.AddInt32("@assignee", request.AssigneeEmployeeId); cmd.Parameters.AddEnum("@status", 20, request.Status);
            cmd.Parameters.AddBoolean("@overdue", request.OnlyOverdue); cmd.Parameters.AddUtcDateTime("@now", now); cmd.Parameters.AddNVarChar("@search", 500, StaffReadSql.Like(request.Page.SearchText));
        }
        using var count = db.CreateCommand("SELECT COUNT(*)" + From + Where(scope)); Parameters(count);
        var total = (int)(await db.ScalarAsync(count, ct).ConfigureAwait(false))!;
        var sort = request.Page.SortColumn switch { "Title" => "t.Title", "Priority" => "t.Priority", "Status" => "t.Status", "CreatedAtUtc" => "t.CreatedAt", _ => "t.DueAt" };
        using var command = db.CreateCommand($"SELECT {Columns}{From}{Where(scope)} ORDER BY {sort} {(request.Page.SortDescending ? "DESC" : "ASC")},t.Id OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY;");
        Parameters(command); StaffReadSql.PageParameters(command, request.Page);
        return new() { Items = await db.QueryAsync(command, Map, ct).ConfigureAwait(false), TotalCount = total, PageNumber = request.Page.PageNumber, PageSize = request.Page.PageSize };
    }
    public async Task<int> CountOverdueAsync(DbSession db, string scope, Action<SqlCommand> addScope, DateTime now, CancellationToken ct)
    {
        var page = await ListAsync(db, new() { OnlyOverdue = true, Page = new() { PageSize = 10 } }, scope, addScope, now, ct).ConfigureAwait(false);
        return page.TotalCount;
    }
    public async Task<WorkTaskItem?> GetAsync(DbSession db, int id, string scope, Action<SqlCommand> addScope, DateTime now, CancellationToken ct)
    {
        var from = db is UnitOfWork ? From.Replace("dbo.WorkTasks t", "dbo.WorkTasks t WITH (UPDLOCK)") : From;
        using var command = db.CreateCommand($"SELECT {Columns}{from} WHERE t.Id=@id AND ({scope});");
        command.Parameters.AddInt32("@id", id); command.Parameters.AddUtcDateTime("@now", now); addScope(command);
        return (await db.QueryAsync(command, Map, ct).ConfigureAwait(false)).SingleOrDefault();
    }
    public async Task<int?> GetLinkedBookingHomeAsync(DbSession db, int bookingId, string scope, Action<SqlCommand> addScope, CancellationToken ct)
    {
        var hints = db is UnitOfWork ? " WITH (HOLDLOCK)" : "";
        using var cmd = db.CreateCommand($"SELECT b.HomestayId FROM dbo.Bookings b{hints} JOIN dbo.Homestays h{hints} ON h.Id=b.HomestayId WHERE b.Id=@id AND ({scope});");
        cmd.Parameters.AddInt32("@id", bookingId); addScope(cmd);
        return await db.ScalarAsync(cmd, ct).ConfigureAwait(false) as int?;
    }
    public async Task<bool> LinkedHomeExistsAsync(DbSession db, int homeId, string scope, Action<SqlCommand> addScope, CancellationToken ct)
    {
        var hints = db is UnitOfWork ? " WITH (HOLDLOCK)" : "";
        using var cmd = db.CreateCommand($"SELECT h.Id FROM dbo.Homestays h{hints} WHERE h.Id=@id AND ({scope});");
        cmd.Parameters.AddInt32("@id", homeId); addScope(cmd);
        return await db.ScalarAsync(cmd, ct).ConfigureAwait(false) is not null;
    }
    public async Task<int> CreateAsync(UnitOfWork db, WorkTaskValues values, DateTime? dueUtc, int userId, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            INSERT dbo.WorkTasks(Title,Description,AssigneeEmployeeId,CreatedByUserId,BookingId,HomestayId,Priority,DueAt,Status,CreatedAt)
            OUTPUT INSERTED.Id VALUES(@title,@description,@assignee,@user,@booking,@home,@priority,@due,'Open',@now);
            """);
        Values(command, values, dueUtc); command.Parameters.AddInt32("@user", userId); command.Parameters.AddUtcDateTime("@now", now);
        return (int)(await db.ScalarAsync(command, ct).ConfigureAwait(false))!;
    }
    public async Task<int> UpdateAsync(UnitOfWork db, int id, WorkTaskValues values, DateTime? dueUtc, byte[] version, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            UPDATE dbo.WorkTasks SET Title=@title,Description=@description,AssigneeEmployeeId=@assignee,BookingId=@booking,HomestayId=@home,
                Priority=@priority,DueAt=@due,UpdatedAt=@now WHERE Id=@id AND RowVer=@version AND Status='Open';
            """);
        Values(command, values, dueUtc); command.Parameters.AddInt32("@id", id); command.Parameters.AddRowVersion("@version", version); command.Parameters.AddUtcDateTime("@now", now);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }
    private static void Values(SqlCommand cmd, WorkTaskValues values, DateTime? dueUtc)
    {
        cmd.Parameters.AddNVarChar("@title", 200, values.Title); cmd.Parameters.AddNVarChar("@description", 1000, values.Description);
        cmd.Parameters.AddInt32("@assignee", values.AssigneeEmployeeId); cmd.Parameters.AddInt32("@booking", values.BookingId); cmd.Parameters.AddInt32("@home", values.HomestayId);
        cmd.Parameters.AddInt32("@priority", (byte)values.Priority); cmd.Parameters.AddUtcDateTime("@due", dueUtc);
    }
    public async Task<int> SetStatusAsync(UnitOfWork db, int id, byte[] version, WorkTaskStatus next, string? result, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            UPDATE dbo.WorkTasks SET Status=@next,UpdatedAt=@now,
                CompletedAt=CASE WHEN @next='Done' THEN @now ELSE CompletedAt END,
                ResultNote=CASE WHEN @next='Done' THEN @result ELSE ResultNote END
            WHERE Id=@id AND RowVer=@version AND Status='Open';
            """);
        command.Parameters.AddInt32("@id", id); command.Parameters.AddRowVersion("@version", version); command.Parameters.AddEnum("@next", 20, (WorkTaskStatus?)next);
        command.Parameters.AddNVarChar("@result", 1000, result); command.Parameters.AddUtcDateTime("@now", now);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<WorkTaskItem>> ListEmployeeOpenTasksAsync(UnitOfWork db, int employeeId, DateTime now, CancellationToken ct)
    {
        using var cmd = db.CreateCommand($"SELECT {Columns}{From.Replace("dbo.WorkTasks t", "dbo.WorkTasks t WITH (UPDLOCK)")} WHERE t.AssigneeEmployeeId=@employee AND t.Status='Open' ORDER BY t.Id;");
        cmd.Parameters.AddInt32("@employee", employeeId); cmd.Parameters.AddUtcDateTime("@now", now);
        return await db.QueryAsync(cmd, Map, ct).ConfigureAwait(false);
    }
    // Lookup and transactional check share the exact same predicates.
    private const string Eligible = """
        e.EmploymentStatus='Active' AND u.Status='Active'
        AND (@booking IS NULL OR (e.Position IN ('Staff','Manager') AND EXISTS(SELECT 1 FROM dbo.Bookings ab JOIN dbo.Homestays ah ON ah.Id=ab.HomestayId WHERE ab.Id=@booking
            AND ((e.Position='Manager' AND e.HasAllAreas=1) OR EXISTS(SELECT 1 FROM dbo.EmployeeAreas ea WHERE ea.EmployeeId=e.Id AND ea.AreaId=ah.AreaId)))))
        AND (@home IS NULL OR (e.Position IN ('Staff','Manager') AND EXISTS(SELECT 1 FROM dbo.Homestays ah WHERE ah.Id=@home
            AND ((e.Position='Manager' AND e.HasAllAreas=1) OR EXISTS(SELECT 1 FROM dbo.EmployeeAreas ea WHERE ea.EmployeeId=e.Id AND ea.AreaId=ah.AreaId)))))
        AND (@booking IS NOT NULL OR @home IS NOT NULL OR e.Position='Accountant' OR e.Id=@actorEmployee
            OR EXISTS(SELECT 1 FROM dbo.Employees me WHERE me.Id=@actorEmployee AND me.Position='Manager' AND (me.HasAllAreas=1
                OR EXISTS(SELECT 1 FROM dbo.EmployeeAreas ma JOIN dbo.EmployeeAreas ra ON ra.AreaId=ma.AreaId WHERE ma.EmployeeId=me.Id AND ra.EmployeeId=e.Id))))
        """;
    public async Task<IReadOnlyList<Lookup>> ListAssigneesAsync(DbSession db, int? bookingId, int? homestayId, int actorId, int? employeeId, CancellationToken ct)
    {
        // HOLDLOCK on Employees/Users also excludes an assignee being terminated concurrently.
        var hints = db is UnitOfWork ? " WITH (UPDLOCK,HOLDLOCK)" : "";
        using var cmd = db.CreateCommand($"SELECT e.Id,e.EmployeeCode,e.FullName FROM dbo.Employees e{hints} JOIN dbo.Users u{hints} ON u.Id=e.UserId WHERE ({Eligible}) AND (@id IS NULL OR e.Id=@id) ORDER BY e.EmployeeCode,e.Id;");
        cmd.Parameters.AddInt32("@booking", bookingId); cmd.Parameters.AddInt32("@home", homestayId); cmd.Parameters.AddInt32("@actorEmployee", actorId); cmd.Parameters.AddInt32("@id", employeeId);
        return await db.QueryAsync(cmd, r => new Lookup { Id = r.GetInt32("Id"), Code = r.GetString("EmployeeCode"), Name = r.GetString("EmployeeCode") + " · " + r.GetString("FullName") }, ct).ConfigureAwait(false);
    }
}
