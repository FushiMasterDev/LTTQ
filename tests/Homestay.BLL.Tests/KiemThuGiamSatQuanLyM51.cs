using Homestay.BLL.Infrastructure;
using Homestay.DAL.Infrastructure;
using Homestay.DTO.Bookings;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Tests;

/// <summary>Kiểm thử M5.1: danh sách đơn ngoại lệ và tổng quan quản lý khớp nhau.</summary>
[Collection("M23 SQL")]
public sealed class M51ManagerMonitoringTests
{
    [SqlFact]
    public async Task EveryExceptionHasCorrectKindServerAgeAndActionsAndDashboardMatchesLists()
    {
        var c = new M51TestClient(); var boss = await c.LoginAsync(); var d = await M21Data.CreateAsync(c.Base, 6);
        var confirm = await d.BookingAsync(c.Base, 0, BookingStatus.Deposited);
        var hold = await d.BookingAsync(c.Base, 1);
        var receipt = await d.BookingAsync(c.Base, 2); await d.PendingDepositAsync(c.Base, receipt);
        var unassigned = await d.BookingAsync(c.Base, 3, assigned: false);
        await c.Base.Login.ExecuteAsync("UPDATE dbo.Bookings SET ConfirmDeadlineAt=DATEADD(minute,-10,SYSUTCDATETIME()) WHERE Id=@id;", confirm);
        await c.Base.Login.ExecuteAsync("UPDATE dbo.Bookings SET HoldExpiresAt=DATEADD(minute,2,SYSUTCDATETIME()) WHERE Id=@id;", hold);
        await c.Base.Login.ExecuteAsync("UPDATE dbo.Bookings SET CreatedAt=DATEADD(minute,-30,SYSUTCDATETIME()) WHERE Id=@id;", unassigned);
        var now = await c.NowAsync(); var vn = now.AddHours(7);
        var today = DateOnly.FromDateTime(vn);
        var arrival = await d.BookingAsync(c.Base, 4, BookingStatus.Confirmed, checkIn: today, checkOut: today.AddDays(2));
        var departure = await d.BookingAsync(c.Base, 5, BookingStatus.CheckedIn, checkIn: today.AddDays(-2), checkOut: today);
        await c.Base.MutateAsync("""
            INSERT dbo.BookingChangeRequests(BookingId,RequestType,Reason,QuotedRefundAmount,Status,RequestedByUserId,RequestedAt)
            VALUES(@booking,'Cancel',N'Yêu cầu M51',0,'Pending',@user,SYSUTCDATETIME());
            """, cmd => { cmd.Parameters.AddInt32("@booking", confirm); cmd.Parameters.AddInt32("@user", boss.UserId); });
        var monday = vn.Date.AddDays(-(((int)vn.DayOfWeek + 6) % 7) - 7).AddHours(8);
        var recorded = DateTime.SpecifyKind(monday.AddHours(-7), DateTimeKind.Utc);
        await c.Base.MutateAsync("UPDATE dbo.Payments SET RecordedAt=@recorded WHERE BookingId=@id;", cmd =>
        { cmd.Parameters.AddInt32("@id", receipt); cmd.Parameters.AddUtcDateTime("@recorded", recorded); });
        var request = new BookingExceptionSearchRequest { HomestayId = d.HomestayId };
        var page = await c.Bookings.ListExceptionsAsync(request);
        Assert.Equal(4, page.TotalCount);
        var overdue = Assert.Single(page.Items, x => x.Kind == BookingExceptionKind.ConfirmationOverdue);
        Assert.Equal(confirm, overdue.BookingId); Assert.InRange(overdue.ElapsedSeconds, 600, 630); Assert.Equal(overdue.ElapsedSeconds, overdue.OverdueSeconds);
        var expiring = Assert.Single(page.Items, x => x.Kind == BookingExceptionKind.HoldExpiring);
        Assert.Equal(0, expiring.OverdueSeconds); Assert.Equal(hold, expiring.BookingId); Assert.InRange(expiring.RemainingSeconds!.Value, 90, 120); Assert.InRange(expiring.ElapsedSeconds, 180, 210);
        var waiting = Assert.Single(page.Items, x => x.Kind == BookingExceptionKind.ReceiptWaiting);
        Assert.Equal(receipt, waiting.BookingId); Assert.NotNull(waiting.PaymentId);
        var reviewHours = await new SettingsProvider(c.Base.Factory).GetPaymentReviewAlertWorkingHoursAsync(CancellationToken.None);
        Assert.Equal(waiting.WorkingElapsedSeconds!.Value - reviewHours * 3600L, waiting.OverdueSeconds);
        Assert.Equal((long)(waiting.ServerNowUtc - recorded).TotalSeconds, waiting.ElapsedSeconds);
        var workStart = await new Homestay.BLL.Infrastructure.SettingsProvider(c.Base.Factory).GetWorkingHoursStartAsync(CancellationToken.None);
        var workEnd = await new Homestay.BLL.Infrastructure.SettingsProvider(c.Base.Factory).GetWorkingHoursEndAsync(CancellationToken.None);
        Assert.InRange(Math.Abs(waiting.WorkingElapsedSeconds!.Value - WorkingSeconds(recorded, waiting.ServerNowUtc, workStart, workEnd)), 0, 1);
        var noAssignee = Assert.Single(page.Items, x => x.Kind == BookingExceptionKind.Unassigned);
        Assert.Equal(unassigned, noAssignee.BookingId); Assert.InRange(noAssignee.ElapsedSeconds, 1800, 1830);
        Assert.All(page.Items, row =>
        {
            Assert.Equal(DateTimeKind.Utc, row.ServerNowUtc.Kind); Assert.Equal(DateTimeKind.Utc, row.SinceUtc.Kind); Assert.Equal(8, row.RowVer.Length);
            Assert.Contains(BookingExceptionAction.OpenBooking, row.AllowedActions); Assert.Contains(BookingExceptionAction.Assign, row.AllowedActions); Assert.Contains(BookingExceptionAction.CreateTask, row.AllowedActions);
        });
        foreach (var kind in Enum.GetValues<BookingExceptionKind>()) Assert.Single((await c.Bookings.ListExceptionsAsync(request with { Kind = kind })).Items);
        var task = await c.Tasks.CreateAsync(M51TestClient.Values(boss.EmployeeId) with { DueAtVietnam = DateTime.SpecifyKind(now.AddHours(6), DateTimeKind.Unspecified) });
        var dashboard = await c.Bookings.GetManagerDashboardAsync();
        Assert.Contains((await c.Bookings.ListAsync(new() { Queue = BookingQueue.ArrivalsToday, HomestayId = d.HomestayId })).Items, x => x.Id == arrival);
        Assert.Contains((await c.Bookings.ListAsync(new() { Queue = BookingQueue.DeparturesToday, HomestayId = d.HomestayId })).Items, x => x.Id == departure);
        Assert.True(dashboard.PendingCancellations > 0);
        Assert.Equal((await c.Bookings.ListAsync(new() { Queue = BookingQueue.ArrivalsToday })).TotalCount, dashboard.ArrivalsToday);
        Assert.Equal((await c.Bookings.ListAsync(new() { Queue = BookingQueue.DeparturesToday })).TotalCount, dashboard.DeparturesToday);
        Assert.Equal((await c.Bookings.ListAsync(new() { Queue = BookingQueue.Unassigned })).TotalCount, dashboard.Unassigned);
        foreach (var count in dashboard.Exceptions)
            Assert.Equal((await c.Bookings.ListExceptionsAsync(new() { Kind = count.Kind })).TotalCount, count.Count);
        Assert.Equal((await c.Tasks.ListAssignedAsync(new() { OnlyOverdue = true })).TotalCount, dashboard.OverdueTasks);
        Assert.True((await c.Tasks.GetAsync(task.Id)).IsOverdue);
        Assert.Equal((await c.Base.Changes.ListAsync(new() { Status = ChangeRequestStatus.Pending })).TotalCount, dashboard.PendingCancellations);
        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant, allAreas: false);
        Assert.Equal((await accountant.Payments.ListPendingReceiptsAsync(new())).TotalCount, dashboard.PendingReceipts);
    }
    private static long WorkingSeconds(DateTime fromUtc, DateTime toUtc, TimeOnly start, TimeOnly end)
    {
        var from = fromUtc.AddHours(7); var to = toUtc.AddHours(7); double seconds = 0;
        for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            var a = day.Add(start.ToTimeSpan()); var b = day.Add(end.ToTimeSpan());
            if (a < from) a = from; if (b > to) b = to;
            if (b > a) seconds += (b - a).TotalSeconds;
        }
        return (long)seconds;
    }
    [SqlFact]
    public async Task ExceptionPagingSupportsMultipleIssuesPerBookingAndLiveAreaScope()
    {
        var setup = new M51TestClient(); await setup.LoginAsync(); var a = await M21Data.CreateAsync(setup.Base, 12); var b = await M21Data.CreateAsync(setup.Base);
        var ids = new List<int>();
        for (var i = 0; i < 12; i++) ids.Add(await a.BookingAsync(setup.Base, i, assigned: false));
        var outside = await b.BookingAsync(setup.Base, 0, assigned: false);
        var scoped = new M51TestClient(); var identity = await scoped.LoginAsync(Position.Manager, a.AreaId, false);
        var request = new BookingExceptionSearchRequest { HomestayId = a.HomestayId, Kind = BookingExceptionKind.Unassigned, Page = new() { PageSize = 10, SortColumn = "BookingCode" } };
        var page = await scoped.Bookings.ListExceptionsAsync(request);
        var next = await scoped.Bookings.ListExceptionsAsync(request with { Page = request.Page with { PageNumber = 2 } });
        Assert.Equal(12, page.TotalCount); Assert.Equal(10, page.Items.Count); Assert.Equal(2, next.Items.Count);
        Assert.Empty(page.Items.Select(x => x.BookingId).Intersect(next.Items.Select(x => x.BookingId)));
        await setup.Base.Login.ExecuteAsync("UPDATE dbo.Bookings SET HoldExpiresAt=DATEADD(minute,2,SYSUTCDATETIME()) WHERE Id=@id;", ids[0]);
        var issues = await scoped.Bookings.ListExceptionsAsync(new() { HomestayId = a.HomestayId, Page = new() { PageSize = 100 } });
        Assert.Equal(2, issues.Items.Count(x => x.BookingId == ids[0]));
        Assert.DoesNotContain((await scoped.Bookings.ListExceptionsAsync(new() { Page = new() { PageSize = 100 } })).Items, x => x.BookingId == outside);
        await setup.SetAreasAsync(identity.EmployeeId, [b.AreaId]);
        Assert.Empty((await scoped.Bookings.ListExceptionsAsync(request)).Items);
        Assert.Contains((await scoped.Bookings.ListExceptionsAsync(new() { HomestayId = b.HomestayId })).Items, x => x.BookingId == outside);
        var dashboard = await scoped.Bookings.GetManagerDashboardAsync();
        Assert.Equal((await scoped.Bookings.ListExceptionsAsync(new() { Kind = BookingExceptionKind.Unassigned })).TotalCount,
            Assert.Single(dashboard.Exceptions, x => x.Kind == BookingExceptionKind.Unassigned).Count);
    }
    [SqlFact]
    public async Task RecentPendingReceiptAndHoldWithPendingReceiptDoNotTriggerTimeAlerts()
    {
        var c = new M51TestClient(); await c.LoginAsync(); var d = await M21Data.CreateAsync(c.Base);
        var id = await d.BookingAsync(c.Base, 0); await d.PendingDepositAsync(c.Base, id);
        await c.Base.Login.ExecuteAsync("UPDATE dbo.Bookings SET HoldExpiresAt=DATEADD(minute,1,SYSUTCDATETIME()) WHERE Id=@id;", id);
        var page = await c.Bookings.ListExceptionsAsync(new() { HomestayId = d.HomestayId }); Assert.Empty(page.Items);
    }
    [SqlFact]
    public async Task PendingReceiptWorkingHourThresholdIsReadFromSettingsAndInvalidSettingFailsClearly()
    {
        var c = new M51TestClient(); await c.LoginAsync();
        var d = await M21Data.CreateAsync(c.Base); var booking = await d.BookingAsync(c.Base, 0); await d.PendingDepositAsync(c.Base, booking);
        var settings = new SettingsProvider(c.Base.Factory);
        var start = await settings.GetWorkingHoursStartAsync(CancellationToken.None); var end = await settings.GetWorkingHoursEndAsync(CancellationToken.None);
        var cursor = (await c.NowAsync()).AddHours(7); var remainingTicks = TimeSpan.FromMinutes(150).Ticks;
        while (remainingTicks > 0)
        {
            var begin = cursor.Date.Add(start.ToTimeSpan()); var finish = cursor.Date.Add(end.ToTimeSpan());
            if (cursor.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || cursor <= begin)
            { cursor = cursor.Date.AddDays(-1).Add(end.ToTimeSpan()); continue; }
            if (cursor > finish) cursor = finish;
            var take = Math.Min(remainingTicks, (cursor - begin).Ticks); cursor = cursor.AddTicks(-take); remainingTicks -= take;
        }
        await c.Base.MutateAsync("UPDATE dbo.Payments SET RecordedAt=@at WHERE BookingId=@id;", cmd =>
        { cmd.Parameters.AddInt32("@id", booking); cmd.Parameters.AddUtcDateTime("@at", DateTime.SpecifyKind(cursor.AddHours(-7), DateTimeKind.Utc)); });
        var old = await c.Base.ScalarAsync<string>("SELECT SettingValue FROM dbo.SystemSettings WHERE SettingKey='Payment.ReviewAlertWorkingHours';");
        try
        {
            await c.Base.MutateAsync("UPDATE dbo.SystemSettings SET SettingValue='2' WHERE SettingKey='Payment.ReviewAlertWorkingHours';");
            Assert.Single((await c.Bookings.ListExceptionsAsync(new() { Kind = BookingExceptionKind.ReceiptWaiting, HomestayId = d.HomestayId })).Items);
            await c.Base.MutateAsync("UPDATE dbo.SystemSettings SET SettingValue='3' WHERE SettingKey='Payment.ReviewAlertWorkingHours';");
            Assert.Empty((await c.Bookings.ListExceptionsAsync(new() { Kind = BookingExceptionKind.ReceiptWaiting, HomestayId = d.HomestayId })).Items);
            await c.Base.MutateAsync("UPDATE dbo.SystemSettings SET SettingValue='0' WHERE SettingKey='Payment.ReviewAlertWorkingHours';");
            var ex = await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.ListExceptionsAsync(new()));
            Assert.Contains("Payment.ReviewAlertWorkingHours", ex.Message);
        }
        finally
        {
            await c.Base.MutateAsync("UPDATE dbo.SystemSettings SET SettingValue=@old WHERE SettingKey='Payment.ReviewAlertWorkingHours';", cmd => cmd.Parameters.AddNVarChar("@old", 500, old));
        }
    }
}
