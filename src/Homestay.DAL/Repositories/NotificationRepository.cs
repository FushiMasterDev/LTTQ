using Homestay.DAL.Infrastructure;
using Homestay.DTO.Common;
using Homestay.DTO.Notifications;

namespace Homestay.DAL.Repositories;

public sealed class NotificationRepository
{
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
