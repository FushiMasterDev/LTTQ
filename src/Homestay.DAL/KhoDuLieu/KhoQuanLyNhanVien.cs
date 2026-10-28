using Homestay.DAL.Infrastructure;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.DTO.Staff;

namespace Homestay.DAL.Repositories;

public sealed partial class StaffRepository
{
    public async Task<PagedResult<EmployeeListItem>> ListByAreaAsync(DbSession db, EmployeesByAreaRequest request, CancellationToken ct)
    {
        const string where = """
             WHERE e.Position IN ('Staff','Manager') AND
             ((e.Position='Manager' AND e.HasAllAreas=1) OR EXISTS(SELECT 1 FROM dbo.EmployeeAreas ea WHERE ea.EmployeeId=e.Id AND ea.AreaId=@area))
             AND (@search=N'' OR e.EmployeeCode LIKE @search ESCAPE '~' OR e.FullName LIKE @search ESCAPE '~')
            """;
        void Parameters(Microsoft.Data.SqlClient.SqlCommand cmd)
        {
            cmd.Parameters.AddInt32("@area", request.AreaId);
            cmd.Parameters.AddNVarChar("@search", 500, StaffReadSql.Like(request.Page.SearchText));
        }
        using var count = db.CreateCommand("SELECT COUNT(*) FROM dbo.Employees e" + where);
        Parameters(count);
        var total = (int)(await db.ScalarAsync(count, ct).ConfigureAwait(false))!;
        var column = request.Page.SortColumn == "FullName" ? "e.FullName" : "e.EmployeeCode";
        using var command = db.CreateCommand(SelectColumns + where + $" ORDER BY {column} {(request.Page.SortDescending ? "DESC" : "ASC")},e.Id OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY;");
        Parameters(command); StaffReadSql.PageParameters(command, request.Page);
        return new() { Items = await db.QueryAsync(command, Map, ct).ConfigureAwait(false), TotalCount = total, PageNumber = request.Page.PageNumber, PageSize = request.Page.PageSize };
    }

    // Users.RowVer is the employee aggregate token: every M5 employee change bumps it atomically.
    public async Task<int> TouchEmployeeAsync(UnitOfWork db, int employeeId, byte[] version, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("UPDATE u SET UpdatedAt=@now FROM dbo.Users u JOIN dbo.Employees e ON e.UserId=u.Id WHERE e.Id=@id AND u.RowVer=@version;");
        command.Parameters.AddInt32("@id", employeeId); command.Parameters.AddRowVersion("@version", version); command.Parameters.AddUtcDateTime("@now", now);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }
    public async Task SetAreasAsync(UnitOfWork db, SetEmployeeAreasRequest request, int actorId, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            UPDATE dbo.Employees SET HasAllAreas=@all,UpdatedAt=@now WHERE Id=@id;
            DELETE dbo.EmployeeAreas WHERE EmployeeId=@id;
            """);
        command.Parameters.AddInt32("@id", request.EmployeeId); command.Parameters.AddBoolean("@all", request.HasAllAreas); command.Parameters.AddUtcDateTime("@now", now);
        await db.ExecuteAsync(command, ct).ConfigureAwait(false);
        foreach (var area in request.AreaIds.Order())
        {
            using var insert = db.CreateCommand("""
                INSERT dbo.EmployeeAreas(EmployeeId,AreaId,AssignedByEmployeeId,AssignedAt,CreatedAt)
                SELECT @id,Id,@actor,@now,@now FROM dbo.Areas WITH (HOLDLOCK) WHERE Id=@area AND IsActive=1;
                """);
            insert.Parameters.AddInt32("@id", request.EmployeeId); insert.Parameters.AddInt32("@area", area);
            insert.Parameters.AddInt32("@actor", actorId); insert.Parameters.AddUtcDateTime("@now", now);
            if (await db.ExecuteAsync(insert, ct).ConfigureAwait(false) != 1)
                throw new InvalidEmployeeAreaException();
        }
    }
    public async Task ChangeEmploymentAsync(UnitOfWork db, int employeeId, Position position, EmploymentStatus status, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            UPDATE dbo.Employees SET Position=@position,EmploymentStatus=@status,
                HasAllAreas=CASE WHEN @position='Manager' THEN HasAllAreas ELSE 0 END,UpdatedAt=@now WHERE Id=@id;
            DELETE dbo.EmployeeAreas WHERE EmployeeId=@id AND @position='Accountant';
            UPDATE u SET SecurityStamp=NEWID(),UpdatedAt=@now,
                Status=CASE WHEN @status='Active' THEN u.Status ELSE 'Disabled' END
            FROM dbo.Users u JOIN dbo.Employees e ON e.UserId=u.Id WHERE e.Id=@id;
            """);
        command.Parameters.AddInt32("@id", employeeId); command.Parameters.AddEnum("@position", 20, (Position?)position);
        command.Parameters.AddEnum("@status", 20, (EmploymentStatus?)status); command.Parameters.AddUtcDateTime("@now", now);
        await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }
}

// DAL signals a domain condition; BLL translates it inside the runner (ensuring rollback).
public sealed class InvalidEmployeeAreaException : Exception;
