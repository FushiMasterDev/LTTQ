using Homestay.DAL.Infrastructure;
using Homestay.DTO.Bookings;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Microsoft.Data.SqlClient;

namespace Homestay.DAL.Repositories;

/// <summary>Các ngưỡng để xếp đơn vào danh sách ngoại lệ: phút giữ phòng cảnh báo, giờ làm việc, hạn xét duyệt.</summary>
public sealed record ManagerExceptionThresholds(int HoldAlertMinutes, int ReviewWorkingHours, TimeOnly WorkingStart, TimeOnly WorkingEnd);

/// <summary>Truy vấn đơn cho quản lý: danh sách đơn ngoại lệ và số liệu tổng quan.</summary>
public sealed partial class BookingRepository
{
    // Reused unchanged by exception list/counts. Workday arithmetic is independent of SET DATEFIRST/language.
    // Recursive days cover only the range of scoped pending receipts, never the whole booking population.
    private static string ExceptionsSql(string scope) => $"""
        ;WITH scoped AS (
            SELECT b.*,h.AreaId FROM dbo.Bookings b JOIN dbo.Homestays h ON h.Id=b.HomestayId WHERE ({scope})
        ), pending AS (
            SELECT p.Id,p.BookingId,p.RecordedAt FROM dbo.Payments p JOIN scoped b ON b.Id=p.BookingId
            WHERE {PaymentRepository.PendingReceiptCondition} AND p.RecordedAt<=@now
        ), reviewDays(d) AS (
            SELECT MIN(CAST(DATEADD(hour,7,RecordedAt) AS date)) FROM pending HAVING COUNT(*)>0
            UNION ALL SELECT DATEADD(day,1,d) FROM reviewDays WHERE d<@today
        ), reviewTime AS (
            SELECT p.Id,p.BookingId,p.RecordedAt,
                SUM(CASE WHEN clip.EndAt>clip.StartAt THEN DATEDIFF_BIG(second,clip.StartAt,clip.EndAt) ELSE CONVERT(bigint,0) END) AS WorkingSeconds
            FROM pending p JOIN reviewDays days ON days.d>=CAST(DATEADD(hour,7,p.RecordedAt) AS date)
            CROSS APPLY (SELECT DATEADD(second,@workStart,CAST(days.d AS datetime2)) AS StartAt,
                                DATEADD(second,@workEnd,CAST(days.d AS datetime2)) AS EndAt) workWindow
            CROSS APPLY (SELECT CASE WHEN DATEADD(hour,7,p.RecordedAt)>workWindow.StartAt THEN DATEADD(hour,7,p.RecordedAt) ELSE workWindow.StartAt END AS StartAt,
                                CASE WHEN DATEADD(hour,7,@now)<workWindow.EndAt THEN DATEADD(hour,7,@now) ELSE workWindow.EndAt END AS EndAt) clip
            WHERE ((DATEDIFF(day,CONVERT(date,'19000101'),days.d)%7)+7)%7<5
            GROUP BY p.Id,p.BookingId,p.RecordedAt
        ), exceptions AS (
            SELECT b.Id AS BookingId,b.Code AS BookingCode,b.AreaId,b.HomestayId,b.Status,b.RowVer,
                0 AS Kind,N'Quá hạn chủ xác nhận' AS Problem,b.ConfirmDeadlineAt AS SinceUtc,b.ConfirmDeadlineAt AS DeadlineAtUtc,
                CAST(NULL AS int) AS PaymentId,CAST(NULL AS bigint) AS WorkingSeconds
            FROM scoped b WHERE b.Status='Deposited' AND b.ConfirmDeadlineAt<@now
            UNION ALL
            SELECT b.Id,b.Code,b.AreaId,b.HomestayId,b.Status,b.RowVer,1,N'Giữ phòng sắp hết hạn',DATEADD(minute,-@holdAlert,b.HoldExpiresAt),b.HoldExpiresAt,NULL,NULL
            FROM scoped b WHERE b.Status='AwaitingPayment' AND b.HoldExpiresAt>=@now AND b.HoldExpiresAt<DATEADD(minute,@holdAlert,@now)
                AND NOT EXISTS(SELECT 1 FROM dbo.Payments p WHERE p.BookingId=b.Id AND p.Type='Deposit' AND p.Status='Pending')
            UNION ALL
            SELECT b.Id,b.Code,b.AreaId,b.HomestayId,b.Status,b.RowVer,2,N'Phiếu thu chờ xác minh lâu',r.RecordedAt,NULL,r.Id,r.WorkingSeconds
            FROM scoped b JOIN reviewTime r ON r.BookingId=b.Id WHERE r.WorkingSeconds>=CONVERT(bigint,@reviewHours)*3600
            UNION ALL
            SELECT b.Id,b.Code,b.AreaId,b.HomestayId,b.Status,b.RowVer,3,N'Đơn chưa giao',b.CreatedAt,NULL,NULL,NULL
            FROM scoped b WHERE {QueueCondition(BookingQueue.Unassigned)}
        )
        """;
    private static void ExceptionParameters(SqlCommand cmd, DateTime now, ManagerExceptionThresholds thresholds)
    {
        cmd.Parameters.AddUtcDateTime("@now", now); cmd.Parameters.AddDate("@today", DateOnly.FromDateTime(now.AddHours(7)));
        cmd.Parameters.AddInt32("@holdAlert", thresholds.HoldAlertMinutes); cmd.Parameters.AddInt32("@reviewHours", thresholds.ReviewWorkingHours);
        cmd.Parameters.AddInt32("@workStart", (int)thresholds.WorkingStart.ToTimeSpan().TotalSeconds);
        cmd.Parameters.AddInt32("@workEnd", (int)thresholds.WorkingEnd.ToTimeSpan().TotalSeconds);
    }
    public async Task<PagedResult<BookingExceptionItem>> ListExceptionsAsync(DbSession db, BookingExceptionSearchRequest request, string scope,
        Action<SqlCommand> addScope, DateTime now, ManagerExceptionThresholds thresholds, CancellationToken ct)
    {
        const string where = """
             WHERE (@kind IS NULL OR x.Kind=@kind) AND (@area IS NULL OR x.AreaId=@area) AND (@home IS NULL OR x.HomestayId=@home)
             AND (@search=N'' OR x.BookingCode LIKE @search ESCAPE '~' OR x.Problem LIKE @search ESCAPE '~')
            """;
        void Parameters(SqlCommand cmd)
        {
            addScope(cmd); ExceptionParameters(cmd, now, thresholds);
            cmd.Parameters.AddInt32("@kind", request.Kind is { } kind ? (int)kind : null); cmd.Parameters.AddInt32("@area", request.AreaId); cmd.Parameters.AddInt32("@home", request.HomestayId);
            cmd.Parameters.AddNVarChar("@search", 500, StaffReadSql.Like(request.Page.SearchText));
        }
        var cte = ExceptionsSql(scope);
        using var count = db.CreateCommand(cte + " SELECT COUNT(*) FROM exceptions x" + where + " OPTION(MAXRECURSION 0);");
        Parameters(count); var total = (int)(await db.ScalarAsync(count, ct).ConfigureAwait(false))!;
        var column = request.Page.SortColumn switch { "BookingCode" => "x.BookingCode", "Kind" => "x.Kind", "ElapsedSeconds" => "DATEDIFF_BIG(second,x.SinceUtc,@now)", "DeadlineAtUtc" => "x.DeadlineAtUtc", _ => "x.SinceUtc" };
        using var command = db.CreateCommand(cte + " SELECT x.*,DATEDIFF_BIG(second,x.SinceUtc,@now) AS ElapsedSeconds FROM exceptions x" + where +
            $" ORDER BY {column} {(request.Page.SortDescending ? "DESC" : "ASC")},x.BookingId,x.Kind,x.PaymentId OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY OPTION(MAXRECURSION 0);");
        Parameters(command); StaffReadSql.PageParameters(command, request.Page);
        return new() { Items = await db.QueryAsync(command, r =>
        {
            var deadline = r.GetNullableUtcDateTime("DeadlineAtUtc");
            var terminal = r.GetEnum<BookingStatus>("Status") is BookingStatus.Completed or BookingStatus.NoShow or BookingStatus.Cancelled or BookingStatus.Rejected or BookingStatus.Expired;
            return new BookingExceptionItem(r.GetInt32("BookingId"), r.GetString("BookingCode"), (BookingExceptionKind)r.GetInt32("Kind"), r.GetString("Problem"), now,
                r.GetUtcDateTime("SinceUtc"), Math.Max(0, r.GetInt64("ElapsedSeconds")), deadline, deadline is { } at ? Math.Max(0, (long)(at - now).TotalSeconds) : null,
                r.GetNullableInt32("PaymentId"), r.GetRowVersion("RowVer"), terminal ? [BookingExceptionAction.OpenBooking, BookingExceptionAction.CreateTask]
                    : [BookingExceptionAction.OpenBooking, BookingExceptionAction.Assign, BookingExceptionAction.CreateTask])
                {
                    WorkingElapsedSeconds = r.IsDBNull(r.GetOrdinal("WorkingSeconds")) ? null : r.GetInt64("WorkingSeconds"),
                    OverdueSeconds = (BookingExceptionKind)r.GetInt32("Kind") switch
                    {
                        BookingExceptionKind.ReceiptWaiting => Math.Max(0, r.GetInt64("WorkingSeconds") - (long)thresholds.ReviewWorkingHours * 3600),
                        BookingExceptionKind.HoldExpiring => 0,
                        _ => Math.Max(0, r.GetInt64("ElapsedSeconds"))
                    }
                };
        }, ct).ConfigureAwait(false), TotalCount = total, PageNumber = request.Page.PageNumber, PageSize = request.Page.PageSize };
    }
    public async Task<ManagerDashboard> ManagerDashboardAsync(DbSession db, string scope, Action<SqlCommand> addScope, string taskScope, Action<SqlCommand> addTaskScope,
        DateTime now, ManagerExceptionThresholds thresholds, CancellationToken ct)
    {
        // Booking queue predicates are exactly those used by ListAsync; no duplicate status/date condition.
        using var command = db.CreateCommand($"""
            SELECT
                COALESCE(SUM(CASE WHEN {QueueCondition(BookingQueue.ArrivalsToday)} THEN 1 ELSE 0 END),0) AS Arrivals,
                COALESCE(SUM(CASE WHEN {QueueCondition(BookingQueue.DeparturesToday)} THEN 1 ELSE 0 END),0) AS Departures,
                COALESCE(SUM(CASE WHEN {QueueCondition(BookingQueue.Unassigned)} THEN 1 ELSE 0 END),0) AS Unassigned
            FROM dbo.Bookings b JOIN dbo.Homestays h ON h.Id=b.HomestayId WHERE ({scope});
            """);
        addScope(command); command.Parameters.AddDate("@today", DateOnly.FromDateTime(now.AddHours(7)));
        var counts = (await db.QueryAsync(command, r => (Arrivals: r.GetInt32("Arrivals"), Departures: r.GetInt32("Departures"), Unassigned: r.GetInt32("Unassigned")), ct).ConfigureAwait(false)).Single();
        using var exceptions = db.CreateCommand(ExceptionsSql(scope) + " SELECT Kind,COUNT(*) AS Total FROM exceptions GROUP BY Kind OPTION(MAXRECURSION 0);");
        addScope(exceptions); ExceptionParameters(exceptions, now, thresholds);
        var exceptionCounts = await db.QueryAsync(exceptions, r => new BookingExceptionCount((BookingExceptionKind)r.GetInt32("Kind"), r.GetInt32("Total")), ct).ConfigureAwait(false);
        var tasks = await new TaskRepository().CountOverdueAsync(db, taskScope, addTaskScope, now, ct).ConfigureAwait(false);
        var receipts = await new PaymentRepository().ListPendingAsync(db, new() { Page = new() { PageSize = 10 } }, false, 0, scope, addScope, ct).ConfigureAwait(false);
        var cancellations = await new ChangeRequestRepository().ListAsync(db, new() { Status = ChangeRequestStatus.Pending, Page = new() { PageSize = 10 } }, scope, addScope, 0, false, ct).ConfigureAwait(false);
        return new(now, DateOnly.FromDateTime(now.AddHours(7)), counts.Arrivals, counts.Departures, counts.Unassigned,
            Enum.GetValues<BookingExceptionKind>().Select(k => new BookingExceptionCount(k, exceptionCounts.SingleOrDefault(c => c.Kind == k)?.Count ?? 0)).ToArray(), tasks, receipts.TotalCount, cancellations.TotalCount);
    }
}
