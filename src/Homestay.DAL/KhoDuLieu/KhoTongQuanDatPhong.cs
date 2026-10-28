using Homestay.DAL.Infrastructure;
using Homestay.DTO.Bookings;
using Microsoft.Data.SqlClient;

namespace Homestay.DAL.Repositories;

/// <summary>Truy vấn số liệu bảng điều khiển của nhân viên (đếm đơn theo hàng đợi, việc gấp).</summary>
public sealed partial class BookingRepository
{
    public async Task<EmployeeDashboard> DashboardAsync(DbSession db, string scopeCondition, Action<SqlCommand> addScope,
        int employeeId, int userId, DateTime now, CancellationToken ct)
    {
        var queueCounts = string.Join(",\n", Enum.GetValues<BookingQueue>().Select(q =>
            $"COALESCE(SUM(CASE WHEN b.AssignedEmployeeId=@employee AND ({(q == BookingQueue.OpenChangeRequests ? "b.HasOpenChange=1" : QueueCondition(q).Replace("ps.PaymentState", "b.PaymentState"))}) THEN 1 ELSE 0 END),0) AS [{q}]"));
        using var command = db.CreateCommand($"""
            SELECT b.*,c.FullName AS CustomerName,ps.PaymentState,
                DATEADD(hour,-7,DATEADD(second,DATEDIFF(second,CAST('00:00' AS time),h.CheckinTime),CAST(b.CheckIn AS datetime2))) AS ArrivalAt,
                DATEADD(hour,-7,DATEADD(second,DATEDIFF(second,CAST('00:00' AS time),h.CheckoutTime),CAST(b.CheckOut AS datetime2))) AS DepartureAt,
                CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.Payments p WHERE p.BookingId=b.Id AND p.Type='Deposit' AND p.Status='Pending') THEN 1 ELSE 0 END AS bit) AS PendingDeposit,
                CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.BookingChangeRequests cr WHERE cr.BookingId=b.Id AND cr.Status IN ('Pending','AwaitingPayment')) THEN 1 ELSE 0 END AS bit) AS HasOpenChange
            INTO #dashboard FROM dbo.Bookings b JOIN dbo.Homestays h ON h.Id=b.HomestayId JOIN dbo.Customers c ON c.Id=b.CustomerId
            JOIN dbo.vw_BookingPaymentState ps ON ps.BookingId=b.Id WHERE {scopeCondition};
            SELECT {queueCounts} FROM #dashboard b;
            SELECT
                (SELECT COUNT(*) FROM #dashboard WHERE AssignedEmployeeId=@employee AND Status='AwaitingPayment' AND PendingDeposit=0 AND HoldExpiresAt>=@now AND HoldExpiresAt<DATEADD(minute,5,@now)) AS Expiring,
                (SELECT COUNT(*) FROM #dashboard WHERE AssignedEmployeeId=@employee AND Status='Deposited' AND ConfirmDeadlineAt<@now) AS Overdue,
                (SELECT COUNT(*) FROM #dashboard WHERE Status='Confirmed' AND CheckIn=@today) AS Arrivals,
                (SELECT COUNT(*) FROM #dashboard WHERE Status='CheckedIn' AND CheckOut=@today) AS Departures,
                (SELECT COUNT(*) FROM dbo.WorkTasks WHERE AssigneeEmployeeId=@employee AND Status='Open' AND DueAt<@endToday) AS DueTasks,
                (SELECT COUNT(*) FROM #dashboard WHERE AssignedEmployeeId IS NULL) AS Unassigned,
                (SELECT COUNT(*) FROM dbo.Notifications WHERE UserId=@user AND IsRead=0) AS Unread;
            SELECT BookingId,BookingCode,CustomerName,Work,DeadlineAt,WorkTaskId FROM (
                SELECT Id AS BookingId,Code AS BookingCode,CustomerName,N'Giữ phòng sắp hết hạn' AS Work,HoldExpiresAt AS DeadlineAt,CAST(NULL AS int) AS WorkTaskId
                FROM #dashboard WHERE AssignedEmployeeId=@employee AND Status='AwaitingPayment' AND PendingDeposit=0 AND HoldExpiresAt>=@now AND HoldExpiresAt<DATEADD(minute,5,@now)
                UNION ALL
                SELECT Id,Code,CustomerName,N'Chủ chưa xác nhận',ConfirmDeadlineAt,NULL FROM #dashboard
                WHERE AssignedEmployeeId=@employee AND Status='Deposited' AND ConfirmDeadlineAt<@now
                UNION ALL
                SELECT Id,Code,CustomerName,N'Nhận phòng hôm nay',ArrivalAt,NULL FROM #dashboard WHERE Status='Confirmed' AND CheckIn=@today
                UNION ALL
                SELECT Id,Code,CustomerName,N'Trả phòng hôm nay',DepartureAt,NULL FROM #dashboard WHERE Status='CheckedIn' AND CheckOut=@today
                UNION ALL
                SELECT t.BookingId,b.Code,c.FullName,t.Title,t.DueAt,t.Id FROM dbo.WorkTasks t
                LEFT JOIN dbo.Bookings b ON b.Id=t.BookingId LEFT JOIN dbo.Customers c ON c.Id=b.CustomerId
                WHERE t.AssigneeEmployeeId=@employee AND t.Status='Open' AND t.DueAt<@endToday
            ) x ORDER BY DeadlineAt,BookingId,WorkTaskId;
            """);
        addScope(command);
        var today = DateOnly.FromDateTime(now.AddHours(7));
        command.Parameters.AddInt32("@employee", employeeId);
        command.Parameters.AddInt32("@user", userId);
        command.Parameters.AddUtcDateTime("@now", now);
        command.Parameters.AddDate("@today", today);
        command.Parameters.AddUtcDateTime("@endToday", DateTime.SpecifyKind(today.AddDays(1).ToDateTime(TimeOnly.MinValue).AddHours(-7), DateTimeKind.Utc));
        return await db.RunAsync(command, async (cmd, token) =>
        {
            var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            await using var scope = reader.ConfigureAwait(false);
            await reader.ReadAsync(token).ConfigureAwait(false);
            var queues = Enum.GetValues<BookingQueue>().Select(q => new DashboardQueueCount(q, reader.GetInt32(q.ToString()))).ToArray();
            await reader.NextResultAsync(token).ConfigureAwait(false);
            await reader.ReadAsync(token).ConfigureAwait(false);
            var expiring = reader.GetInt32("Expiring"); var overdue = reader.GetInt32("Overdue"); var arrivals = reader.GetInt32("Arrivals"); var departures = reader.GetInt32("Departures");
            var tasks = reader.GetInt32("DueTasks"); var unassigned = reader.GetInt32("Unassigned"); var unread = reader.GetInt32("Unread");
            await reader.NextResultAsync(token).ConfigureAwait(false);
            var urgent = await StaffReadSql.ReadSetAsync(reader, r => new DashboardUrgentItem(r.GetNullableInt32("BookingId"), r.GetNullableString("BookingCode"), r.GetNullableString("CustomerName"),
                r.GetString("Work"), r.GetUtcDateTime("DeadlineAt"), r.GetNullableInt32("WorkTaskId")), token).ConfigureAwait(false);
            return new EmployeeDashboard(now, today, queues, expiring, overdue, arrivals, departures, tasks, unassigned, unread, urgent);
        }, ct).ConfigureAwait(false);
    }
}
