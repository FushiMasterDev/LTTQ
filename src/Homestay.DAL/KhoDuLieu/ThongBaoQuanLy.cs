using Homestay.DAL.Infrastructure;

namespace Homestay.DAL.Repositories;

public sealed partial class NotificationRepository
{
    public async Task NotifyTaskAsync(UnitOfWork db, int userId, string message, int taskId, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("INSERT dbo.Notifications(UserId,Message,RefType,RefId,IsRead,CreatedAt) VALUES(@user,@message,'Task',@ref,0,@now);");
        command.Parameters.AddInt32("@user", userId); command.Parameters.AddNVarChar("@message", 500, message);
        command.Parameters.AddVarChar("@ref", 30, taskId.ToString(System.Globalization.CultureInfo.InvariantCulture)); command.Parameters.AddUtcDateTime("@now", now);
        await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }
    public async Task NotifyTaskEmployeeAsync(UnitOfWork db, int employeeId, string message, int taskId, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("INSERT dbo.Notifications(UserId,Message,RefType,RefId,IsRead,CreatedAt) SELECT UserId,@message,'Task',@ref,0,@now FROM dbo.Employees WHERE Id=@employee;");
        command.Parameters.AddInt32("@employee", employeeId); command.Parameters.AddNVarChar("@message", 500, message);
        command.Parameters.AddVarChar("@ref", 30, taskId.ToString(System.Globalization.CultureInfo.InvariantCulture)); command.Parameters.AddUtcDateTime("@now", now);
        await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }
}
