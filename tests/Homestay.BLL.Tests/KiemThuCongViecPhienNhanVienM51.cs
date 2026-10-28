using Homestay.BLL.Infrastructure;
using Homestay.DAL.Infrastructure;
using Homestay.DTO.Bookings;
using Homestay.DTO.Enums;
using Homestay.DTO.Tasks;

namespace Homestay.BLL.Tests;

/// <summary>Kiểm thử M5.1: công việc, phiên đăng nhập và quản lý nhân viên với CSDL thật.</summary>
[Collection("M23 SQL")]
public sealed class M51TaskSessionStaffTests
{
    [SqlFact]
    public async Task TaskCreateStartCompletePersistsResultOverdueFlagAuditAndNotifiesCreator()
    {
        var manager = new M51TestClient(); var boss = await manager.LoginAsync(); var d = await M21Data.CreateAsync(manager.Base);
        var staff = new M51TestClient(); var person = await staff.LoginAsync(Position.Staff, d.AreaId, false);
        var booking = await d.BookingAsync(manager.Base, 0);
        var now = await manager.NowAsync();
        var task = await manager.Tasks.CreateAsync(M51TestClient.Values(person.EmployeeId, booking, d.HomestayId) with
            { Title = "  Việc quá hạn  ", DueAtVietnam = DateTime.SpecifyKind(now.AddHours(6), DateTimeKind.Unspecified) });
        Assert.Equal("Việc quá hạn", task.Title); Assert.True(task.IsOverdue); Assert.Equal(WorkTaskStatus.Open, task.Status);
        Assert.Equal(DateTimeKind.Utc, task.DueAtUtc!.Value.Kind);
        Assert.Contains((await staff.Tasks.ListMineAsync(new() { OnlyOverdue = true })).Items, t => t.Id == task.Id);
        var started = await staff.Tasks.StartAsync(new(task.Id, task.RowVer));
        Assert.NotNull(started.StartedAtUtc); Assert.Equal(WorkTaskStatus.Open, started.Status); Assert.NotEqual(task.RowVer, started.RowVer);
        await Assert.ThrowsAsync<BusinessException>(() => staff.Tasks.StartAsync(new(task.Id, started.RowVer)));
        await Assert.ThrowsAsync<BusinessException>(() => staff.Tasks.CompleteAsync(new(task.Id, " ", started.RowVer)));
        var complete = await staff.Tasks.CompleteAsync(new(task.Id, "  Đã liên hệ và xử lý  ", started.RowVer));
        Assert.Equal(WorkTaskStatus.Done, complete.Status); Assert.Equal("Đã liên hệ và xử lý", complete.ResultNote); Assert.NotNull(complete.CompletedAtUtc); Assert.False(complete.IsOverdue);
        Assert.Equal(1, await manager.CountAsync("SELECT COUNT(*) FROM dbo.Notifications WHERE UserId=@id AND RefType='Task' AND Message LIKE N'%đã hoàn thành%';", boss.UserId));
        Assert.Equal(3, await manager.CountAsync("SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='WorkTasks' AND RecordId=CONVERT(varchar(50),@id) AND ActionCode IN ('Task.Create','Task.Start','Task.Complete');", task.Id));
        await Assert.ThrowsAsync<BusinessException>(() => staff.Tasks.CompleteAsync(new(task.Id, "Lặp lại", complete.RowVer)));
    }
    [SqlFact]
    public async Task OtherEmployeeCannotReadStartOrCompleteAndManagerCannotCancelAnothersTask()
    {
        var manager = new M51TestClient(); await manager.LoginAsync(); var d = await M21Data.CreateAsync(manager.Base);
        var owner = new M51TestClient(); var identity = await owner.LoginAsync(Position.Staff, d.AreaId, false);
        var task = await manager.Tasks.CreateAsync(M51TestClient.Values(identity.EmployeeId, home: d.HomestayId));
        var other = new M51TestClient(); await other.LoginAsync(Position.Staff, d.AreaId, false);
        await Assert.ThrowsAsync<NotFoundException>(() => other.Tasks.GetAsync(task.Id));
        await Assert.ThrowsAsync<NotFoundException>(() => other.Tasks.StartAsync(new(task.Id, task.RowVer)));
        await Assert.ThrowsAsync<NotFoundException>(() => other.Tasks.CompleteAsync(new(task.Id, "Kết quả", task.RowVer)));
        Assert.DoesNotContain((await other.Tasks.ListMineAsync(new() { AssigneeEmployeeId = identity.EmployeeId })).Items, t => t.Id == task.Id);
        var secondManager = new M51TestClient(); await secondManager.LoginAsync();
        await Assert.ThrowsAsync<NotFoundException>(() => secondManager.Tasks.CancelAsync(new(task.Id, "Không phải người giao", task.RowVer)));
    }
    [SqlFact]
    public async Task UpdateReassignAndCancelNotifyBothRecipientsAndRequireFreshVersionAndReason()
    {
        var manager = new M51TestClient(); await manager.LoginAsync(); var d = await M21Data.CreateAsync(manager.Base);
        var a = await manager.Base.Login.CreateIdentityAsync(Position.Staff); var b = await manager.Base.Login.CreateIdentityAsync(Position.Staff);
        await manager.SetAreasAsync(a.EmployeeId, [d.AreaId]); await manager.SetAreasAsync(b.EmployeeId, [d.AreaId]);
        var values = M51TestClient.Values(a.EmployeeId, home: d.HomestayId);
        var task = await manager.Tasks.CreateAsync(values);
        var updated = await manager.Tasks.UpdateAsync(new(task.Id, values with { AssigneeEmployeeId = b.EmployeeId, Title = "Đã sửa" }, task.RowVer));
        await Assert.ThrowsAsync<ConcurrencyException>(() => manager.Tasks.CancelAsync(new(task.Id, "Phiên bản cũ", task.RowVer)));
        await Assert.ThrowsAsync<BusinessException>(() => manager.Tasks.CancelAsync(new(task.Id, " ", updated.RowVer)));
        var cancelled = await manager.Tasks.CancelAsync(new(task.Id, "  Không còn cần  ", updated.RowVer));
        Assert.Equal(WorkTaskStatus.Cancelled, cancelled.Status);
        Assert.Equal(2, await manager.CountAsync("SELECT COUNT(*) FROM dbo.Notifications WHERE UserId=@id AND RefType='Task';", a.UserId));
        Assert.Equal(2, await manager.CountAsync("SELECT COUNT(*) FROM dbo.Notifications WHERE UserId=@id AND RefType='Task';", b.UserId));
        await Assert.ThrowsAsync<BusinessException>(() => manager.Tasks.UpdateAsync(new(task.Id, values, cancelled.RowVer)));
    }
    [SqlFact]
    public async Task TaskLinksAndAssigneeEligibilityAreCheckedInsideTransactionAndScopesRefresh()
    {
        var manager = new M51TestClient(); await manager.LoginAsync(); var a = await M21Data.CreateAsync(manager.Base); var b = await M21Data.CreateAsync(manager.Base);
        var staff = await manager.Base.Login.CreateIdentityAsync(Position.Staff); await manager.SetAreasAsync(staff.EmployeeId, [a.AreaId]);
        var booking = await a.BookingAsync(manager.Base, 0);
        Assert.Contains(await manager.Tasks.ListAssigneesAsync(booking, a.HomestayId), x => x.Id == staff.EmployeeId);
        await Assert.ThrowsAsync<BusinessException>(() => manager.Tasks.CreateAsync(M51TestClient.Values(staff.EmployeeId, booking, b.HomestayId)));
        await manager.SetAreasAsync(staff.EmployeeId, [b.AreaId]);
        Assert.DoesNotContain(await manager.Tasks.ListAssigneesAsync(booking), x => x.Id == staff.EmployeeId);
        await Assert.ThrowsAsync<BusinessException>(() => manager.Tasks.CreateAsync(M51TestClient.Values(staff.EmployeeId, booking)));
        var scoped = new M51TestClient(); await scoped.LoginAsync(Position.Manager, b.AreaId, false);
        await Assert.ThrowsAsync<NotFoundException>(() => scoped.Tasks.CreateAsync(M51TestClient.Values(staff.EmployeeId, booking)));
        Assert.Equal(0, await manager.CountAsync("SELECT COUNT(*) FROM dbo.WorkTasks WHERE AssigneeEmployeeId=@id;", staff.EmployeeId));
    }
    [SqlFact]
    public async Task TaskListsPageAndFilterInSqlAndAccountantCanCompleteUnlinkedAssignedWork()
    {
        var manager = new M51TestClient(); await manager.LoginAsync();
        var accountant = new M51TestClient(); var person = await accountant.LoginAsync(Position.Accountant, all: false);
        var prefix = "M51[%]_" + Guid.NewGuid().ToString("N");
        for (var i = 0; i < 12; i++) await manager.Tasks.CreateAsync(M51TestClient.Values(person.EmployeeId) with { Title = prefix + i.ToString("D2") });
        var request = new WorkTaskSearchRequest { Status = WorkTaskStatus.Open, Page = new() { PageSize = 10, SearchText = prefix, SortColumn = "Title" } };
        var first = await accountant.Tasks.ListMineAsync(request);
        var second = await accountant.Tasks.ListMineAsync(request with { Page = request.Page with { PageNumber = 2 } });
        Assert.Equal(12, first.TotalCount); Assert.Equal(10, first.Items.Count); Assert.Equal(2, second.Items.Count);
        Assert.Empty(first.Items.Select(t => t.Id).Intersect(second.Items.Select(t => t.Id)));
        Assert.All(first.Items, t => Assert.False(t.IsOverdue));
        var target = first.Items[0];
        await accountant.Tasks.CompleteAsync(new(target.Id, "Đã xong", target.RowVer)); // Start is optional in spec 7.5.
        Assert.Equal(11, (await accountant.Tasks.ListMineAsync(request)).TotalCount);
        await Assert.ThrowsAsync<ForbiddenException>(() => accountant.Tasks.ListAssignedAsync(new()));
    }
    [SqlFact]
    public async Task ForceLogoutEndsExactlyTargetSessionHeartbeatFalseAndNextWriteIsBlocked()
    {
        var manager = new M51TestClient(); await manager.LoginAsync();
        var staff = new M51TestClient(); var identity = await staff.LoginAsync(Position.Staff, all: false);
        var sessionId = staff.Base.Current.RequireSession().SessionId;
        Assert.Contains((await manager.Sessions.ListOpenSessionsAsync(new() { EmployeeId = identity.EmployeeId })).Items, s => s.SessionId == sessionId);
        await Assert.ThrowsAsync<BusinessException>(() => manager.Sessions.ForceLogoutAsync(new(manager.Base.Current.RequireSession().SessionId, "Tự buộc")));
        await Assert.ThrowsAsync<ForbiddenException>(() => staff.Sessions.ForceLogoutAsync(new(sessionId, "Tự gọi")));
        await Assert.ThrowsAsync<ForbiddenException>(() => staff.Sessions.ListOpenSessionsAsync(new()));
        await manager.Sessions.ForceLogoutAsync(new(sessionId, "  Thu hồi phiên máy cũ  "));
        Assert.False(await staff.Sessions.HeartbeatAsync());
        await Assert.ThrowsAsync<SessionExpiredException>(() => staff.Tasks.StartAsync(new(1, new byte[8])));
        Assert.Empty((await manager.Sessions.ListOpenSessionsAsync(new() { EmployeeId = identity.EmployeeId })).Items);
        var history = await manager.Sessions.ListSessionHistoryAsync(new() { EmployeeId = identity.EmployeeId, Page = new() { PageSize = 10 } });
        var ended = Assert.Single(history.Items); Assert.Equal(SessionEndReason.ForcedByManager, ended.EndReason); Assert.NotNull(ended.EndedAtUtc);
        await Assert.ThrowsAsync<ConcurrencyException>(() => manager.Sessions.ForceLogoutAsync(new(sessionId, "Lặp lại")));
        Assert.Equal(1, await manager.CountAsync("SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='UserSessions' AND ActionCode='Session.ForceLogout' AND RecordId=CONVERT(varchar(50),@id);", checked((int)sessionId)));
    }
    [SqlFact]
    public async Task PositionChangeEndsOpenSessionsClearsAccountantAreasAndEnforcesVersionsAndSelfGuard()
    {
        var manager = new M51TestClient(); var boss = await manager.LoginAsync(); var d = await M21Data.CreateAsync(manager.Base);
        var target = new M51TestClient(); var person = await target.LoginAsync(Position.Staff, d.AreaId, false);
        var own = await manager.Staff.GetAsync(boss.EmployeeId);
        await Assert.ThrowsAsync<BusinessException>(() => manager.Staff.ChangePositionAsync(new(boss.EmployeeId, Position.Staff, "Tự hạ", own.Employee.RowVer)));
        await Assert.ThrowsAsync<BusinessException>(() => manager.Staff.TerminateEmploymentAsync(new(boss.EmployeeId, "Tự nghỉ", own.Employee.RowVer)));
        var old = await manager.Staff.GetAsync(person.EmployeeId);
        await manager.Staff.ChangePositionAsync(new(person.EmployeeId, Position.Accountant, "Chuyển bộ phận", old.Employee.RowVer));
        Assert.False(await target.Sessions.HeartbeatAsync());
        await Assert.ThrowsAsync<SessionExpiredException>(() => target.Tasks.StartAsync(new(1, new byte[8])));
        var next = await manager.Staff.GetAreasAsync(person.EmployeeId); Assert.Equal(Position.Accountant, next.Position); Assert.False(next.HasAllAreas); Assert.Empty(next.Areas);
        await Assert.ThrowsAsync<ConcurrencyException>(() => manager.Staff.ChangePositionAsync(new(person.EmployeeId, Position.Staff, "Token cũ", old.Employee.RowVer)));
        Assert.Equal(Position.Accountant, (await target.Base.Login.LoginAsync(person)).Position);
    }
    [SqlFact]
    public async Task TerminationDisablesLoginEndsSessionsReleasesBookingsAndCancelsOpenTasksWithNotifications()
    {
        var manager = new M51TestClient(); var boss = await manager.LoginAsync(); var d = await M21Data.CreateAsync(manager.Base);
        var staff = new M51TestClient(); var person = await staff.LoginAsync(Position.Staff, d.AreaId, false);
        var id = await d.BookingAsync(manager.Base, 0); var booking = (await manager.Bookings.GetAsync(id)).Booking;
        await manager.Bookings.AssignManyAsync(new([new(id, booking.RowVer)], person.EmployeeId));
        var task = await manager.Tasks.CreateAsync(M51TestClient.Values(person.EmployeeId, id));
        var old = await manager.Staff.GetAsync(person.EmployeeId);
        await manager.Staff.TerminateEmploymentAsync(new(person.EmployeeId, "  Nghỉ việc theo đề nghị  ", old.Employee.RowVer));
        var detail = await manager.Staff.GetAsync(person.EmployeeId);
        Assert.Equal(EmploymentStatus.Resigned, detail.Employee.EmploymentStatus); Assert.Equal(UserStatus.Disabled, detail.Employee.AccountStatus);
        Assert.False(await staff.Sessions.HeartbeatAsync());
        await Assert.ThrowsAsync<SessionExpiredException>(() => staff.Tasks.StartAsync(new(task.Id, task.RowVer)));
        await Assert.ThrowsAsync<BusinessException>(() => staff.Base.Login.LoginAsync(person));
        Assert.Null((await manager.Bookings.GetAsync(id)).Booking.AssignedEmployeeId);
        Assert.Equal(WorkTaskStatus.Cancelled, (await manager.Tasks.GetAsync(task.Id)).Status);
        Assert.Equal(1, await manager.CountAsync("SELECT COUNT(*) FROM dbo.Notifications WHERE UserId=@id AND RefType='Task' AND Message LIKE N'%thay đổi công tác%';", boss.UserId));
        Assert.True(await manager.CountAsync("SELECT COUNT(*) FROM dbo.Notifications WHERE UserId=@id AND RefType='Booking' AND Message LIKE N'%cần giao lại%';", boss.UserId) > 0);
        Assert.Equal(1, await manager.CountAsync("SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Bookings' AND ActionCode='Booking.ReleaseAssignee' AND RecordId=CONVERT(varchar(50),@id);", id));
        Assert.Equal(1, await manager.CountAsync("SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='WorkTasks' AND ActionCode='Task.CancelEmployment' AND RecordId=CONVERT(varchar(50),@id);", task.Id));
    }
    [SqlFact]
    public async Task TwoManagersDemotingEachOtherLeaveOneActiveManager()
    {
        var a = new M51TestClient(); var ai = await a.LoginAsync(); var b = new M51TestClient(); var bi = await b.LoginAsync();
        // The invariant is global. Temporarily suspend previous active managers and restore exact IDs in finally.
        var previous = await a.Base.Factory.ReadAsync(async (db, ct) =>
        {
            using var cmd = db.CreateCommand("SELECT Id FROM dbo.Employees WHERE Position='Manager' AND EmploymentStatus='Active' AND Id<>@a AND Id<>@b;");
            cmd.Parameters.AddInt32("@a", ai.EmployeeId); cmd.Parameters.AddInt32("@b", bi.EmployeeId);
            return await db.QueryAsync(cmd, r => r.GetInt32(0), ct);
        }, CancellationToken.None);
        try
        {
            foreach (var id in previous) await a.Base.Login.ExecuteAsync("UPDATE dbo.Employees SET EmploymentStatus='Suspended' WHERE Id=@id;", id);
            var av = (await b.Staff.GetAsync(ai.EmployeeId)).Employee.RowVer; var bv = (await a.Staff.GetAsync(bi.EmployeeId)).Employee.RowVer;
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task<Exception?> Demote(M51TestClient c, int employee, byte[] version)
            { await gate.Task; return await Record.ExceptionAsync(() => c.Staff.ChangePositionAsync(new(employee, Position.Staff, "Sắp xếp lại", version))); }
            var pending = new[] { Demote(a, bi.EmployeeId, bv), Demote(b, ai.EmployeeId, av) }; gate.SetResult();
            var errors = await Task.WhenAll(pending);
            Assert.Single(errors, e => e is null); Assert.IsAssignableFrom<BusinessException>(Assert.Single(errors, e => e is not null));
            Assert.Equal(1, await a.Base.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Employees e JOIN dbo.Users u ON u.Id=e.UserId WHERE e.Position='Manager' AND e.EmploymentStatus='Active' AND u.Status='Active';"));
            var winner = errors[0] is null ? a : b; var remaining = errors[0] is null ? ai : bi;
            var last = await winner.Staff.GetAsync(remaining.EmployeeId);
            // The last active manager cannot be terminated by another inactive/invalid manager session either.
            var loser = errors[0] is null ? b : a;
            await Assert.ThrowsAsync<SessionExpiredException>(() => loser.Staff.TerminateEmploymentAsync(new(remaining.EmployeeId, "Thử bỏ QL cuối", last.Employee.RowVer)));
        }
        finally { foreach (var id in previous) await a.Base.Login.ExecuteAsync("UPDATE dbo.Employees SET EmploymentStatus='Active' WHERE Id=@id;", id); }
    }
}
