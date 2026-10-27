using Homestay.DAL.Infrastructure;
using Homestay.DTO.Common;
using Homestay.DTO.Notifications;

namespace Homestay.DAL.Repositories;

public sealed partial class NotificationRepository
{
    private const string ManagerBookingScope = """
        e.Position='Manager' AND (e.HasAllAreas=1 OR b.CreatedByEmployeeId=e.Id OR b.AssignedEmployeeId=e.Id
            OR EXISTS(SELECT 1 FROM dbo.EmployeeAreas ea WHERE ea.EmployeeId=e.Id AND ea.AreaId=h.AreaId))
        """;

    public async Task NotifyScopedManagersAsync(UnitOfWork db, int bookingId, string message, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand($"""
            INSERT dbo.Notifications(UserId,Message,RefType,RefId,IsRead,CreatedAt)
            SELECT e.UserId,@message,'Booking',CONVERT(varchar(30),b.Id),0,@now
            FROM dbo.Bookings b JOIN dbo.Homestays h ON h.Id=b.HomestayId
            JOIN dbo.Employees e ON {ManagerBookingScope} JOIN dbo.Users u ON u.Id=e.UserId
            WHERE b.Id=@booking AND e.EmploymentStatus='Active' AND u.Status='Active';
            """);
        command.Parameters.AddInt32("@booking", bookingId); command.Parameters.AddNVarChar("@message", 500, message);
        command.Parameters.AddUtcDateTime("@now", now);
        await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }
    public async Task NotifyAccountantsAsync(UnitOfWork db, string message, int paymentId, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            INSERT dbo.Notifications(UserId,Message,RefType,RefId,IsRead,CreatedAt)
            SELECT e.UserId,@message,'Payment',@ref,0,@now FROM dbo.Employees e JOIN dbo.Users u ON u.Id=e.UserId
            WHERE e.Position='Accountant' AND e.EmploymentStatus='Active' AND u.Status='Active';
            """);
        command.Parameters.AddNVarChar("@message", 500, message);
        command.Parameters.AddVarChar("@ref", 30, paymentId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddUtcDateTime("@now", now);
        await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }

    public async Task NotifyBookingAssigneeAsync(UnitOfWork db, int userId, string message, int bookingId, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("INSERT dbo.Notifications(UserId,Message,RefType,RefId,IsRead,CreatedAt) VALUES(@user,@message,'Booking',@ref,0,@now);");
        command.Parameters.AddInt32("@user", userId);
        command.Parameters.AddNVarChar("@message", 500, message);
        command.Parameters.AddVarChar("@ref", 30, bookingId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        command.Parameters.AddUtcDateTime("@now", now);
        await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }

    // Same booking scope as DataScope for Managers: all areas, area membership, creator or assignee.
    public async Task NotifyBookingOperatorsAsync(UnitOfWork db, int bookingId, string message, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand($"""
            INSERT dbo.Notifications(UserId,Message,RefType,RefId,IsRead,CreatedAt)
            SELECT e.UserId,@message,'Booking',CONVERT(varchar(30),b.Id),0,@now
            FROM dbo.Bookings b JOIN dbo.Homestays h ON h.Id=b.HomestayId
            JOIN dbo.Employees e ON (b.AssignedEmployeeId=e.Id OR (b.AssignedEmployeeId IS NULL AND {ManagerBookingScope}))
            JOIN dbo.Users u ON u.Id=e.UserId
            WHERE b.Id=@booking AND e.EmploymentStatus='Active' AND u.Status='Active';
            """);
        command.Parameters.AddInt32("@booking", bookingId);
        command.Parameters.AddNVarChar("@message", 500, message);
        command.Parameters.AddUtcDateTime("@now", now);
        await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<int> CountUnreadAsync(DbSession db, int userId, CancellationToken ct)
    {
        using var command = db.CreateCommand("SELECT COUNT(*) FROM dbo.Notifications WHERE UserId=@user AND IsRead=0;");
        command.Parameters.AddInt32("@user", userId);
        return (int)(await db.ScalarAsync(command, ct).ConfigureAwait(false))!;
    }
    public async Task<PagedResult<NotificationItem>> ListAsync(DbSession db, int userId, PageRequest page, CancellationToken ct)
    {
        using var count = db.CreateCommand("SELECT COUNT(*) FROM dbo.Notifications WHERE UserId=@user;");
        count.Parameters.AddInt32("@user", userId);
        var total = (int)(await db.ScalarAsync(count, ct).ConfigureAwait(false))!;
        using var command = db.CreateCommand("""
            SELECT Id,Message,RefType,RefId,IsRead,CreatedAt FROM dbo.Notifications WHERE UserId=@user
            ORDER BY IsRead,CreatedAt DESC,Id DESC OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY;
            """);
        command.Parameters.AddInt32("@user", userId);
        StaffReadSql.PageParameters(command, page);
        return new()
        {
            TotalCount = total,
            PageNumber = page.PageNumber,
            PageSize = page.PageSize,
            Items = await db.QueryAsync(command, r => new NotificationItem(r.GetInt64("Id"), r.GetString("Message"), r.GetNullableString("RefType"),
                r.GetNullableString("RefId"), r.GetBoolean("IsRead"), r.GetUtcDateTime("CreatedAt")), ct).ConfigureAwait(false)
        };
    }
    public async Task<int> MarkReadAsync(UnitOfWork db, int userId, long? notificationId, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand(notificationId.HasValue
            ? "UPDATE dbo.Notifications SET IsRead=1,UpdatedAt=@now WHERE UserId=@user AND Id=@id;"
            : "UPDATE dbo.Notifications SET IsRead=1,UpdatedAt=@now WHERE UserId=@user AND IsRead=0;");
        command.Parameters.AddInt32("@user", userId);
        command.Parameters.AddUtcDateTime("@now", now);
        if (notificationId.HasValue) command.Parameters.AddInt64("@id", notificationId.Value);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }
}
