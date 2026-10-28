using Homestay.BLL.Infrastructure;
using Homestay.DAL.Infrastructure;
using Homestay.DTO.Bookings;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Tests;

/// <summary>Kiểm thử M5.1: đổi khu vực phụ trách có hiệu lực ngay và giao nhiều đơn.</summary>
[Collection("M23 SQL")]
public sealed class M51ScopeAssignmentTests
{
    [SqlFact]
    public async Task AreaChangeImmediatelyAffectsOpenSessionWritesWhileSearchAndCreateRemainGlobal()
    {
        var manager = new M51TestClient(); await manager.LoginAsync();
        var a = await M21Data.CreateAsync(manager.Base); var b = await M21Data.CreateAsync(manager.Base);
        var aId = await a.BookingAsync(manager.Base, 0); var bId = await b.BookingAsync(manager.Base, 0);
        var staff = new M51TestClient(); var identity = await staff.LoginAsync(Position.Staff, a.AreaId, false);
        var session = staff.Base.Current.RequireSession().SessionId;
        var first = await staff.Bookings.GetAsync(aId);
        Assert.Contains(BookingAction.CancelUnpaid, first.AllowedActions);
        var before = await manager.Staff.GetAreasAsync(identity.EmployeeId);
        await manager.Staff.SetAreasAsync(new(identity.EmployeeId, [b.AreaId], false, before.RowVer));
        Assert.Equal(session, staff.Base.Current.RequireSession().SessionId);
        Assert.True(await staff.Sessions.HeartbeatAsync());
        var denied = await Assert.ThrowsAsync<NotFoundException>(() => staff.Bookings.CancelUnpaidAsync(new(aId, "Ngoài khu vực mới", first.Booking.RowVer)));
        Assert.Equal("Không tìm thấy dữ liệu", denied.Message);
        var nowAllowed = await staff.Bookings.GetAsync(bId);
        await staff.Bookings.CancelUnpaidAsync(new(bId, "Trong khu vực mới", nowAllowed.Booking.RowVer));
        await Assert.ThrowsAsync<ConcurrencyException>(() => manager.Staff.SetAreasAsync(new(identity.EmployeeId, [a.AreaId], false, before.RowVer)));
        Assert.Single(await staff.Base.Availability.SearchAsync(new() { AreaId = a.AreaId, CheckIn = M21Data.CheckIn, CheckOut = M21Data.CheckOut, Guests = 2 }));
        var created = await staff.Bookings.CreateAsync(new() { CustomerId = a.CustomerId, RoomTypeId = a.RoomTypeId, Guests = 2,
            CheckIn = M21Data.CheckIn, CheckOut = M21Data.CheckOut, ContactName = "M51", ContactPhone = "0901234567", SourceChannel = SourceChannel.Phone });
        Assert.Equal(identity.EmployeeId, created.Booking.CreatedByEmployeeId);
        // Creator and assignee exceptions remain effective even after all areas have been removed.
        await manager.SetAreasAsync(identity.EmployeeId, []);
        await staff.Bookings.CancelUnpaidAsync(new(created.Booking.Id, "Đơn mình tạo ngoài khu vực", created.Booking.RowVer));
        Assert.Equal(2, (await manager.Bookings.ListAsync(new() { Queue = BookingQueue.All, HomestayId = a.HomestayId })).TotalCount);
    }
    [SqlFact]
    public async Task AllAreaFlagIsLiveAndAssigneeExceptionAndExactSearchRemainAvailable()
    {
        var manager = new M51TestClient(); var managerIdentity = await manager.LoginAsync();
        var d = await M21Data.CreateAsync(manager.Base); var id = await d.BookingAsync(manager.Base, 0, assigned: false);
        var scoped = new M51TestClient(); var scopedIdentity = await scoped.LoginAsync(Position.Manager, all: false);
        Assert.Empty((await scoped.Bookings.ListAsync(new() { Queue = BookingQueue.All, HomestayId = d.HomestayId })).Items);
        var raw = await scoped.Bookings.GetAsync(id); Assert.Empty(raw.AllowedActions);
        Assert.Single((await scoped.Bookings.ListAsync(new() { Code = raw.Booking.Code })).Items);
        Assert.Contains((await scoped.Bookings.ListAsync(new() { Phone = raw.Booking.CustomerPhone })).Items, b => b.Id == id);
        await Assert.ThrowsAsync<NotFoundException>(() => scoped.Bookings.ClaimAsync(new(id, raw.Booking.RowVer)));
        await manager.SetAreasAsync(scopedIdentity.EmployeeId, [], true);
        await scoped.Bookings.ClaimAsync(new(id, raw.Booking.RowVer));
        await manager.SetAreasAsync(scopedIdentity.EmployeeId, []);
        Assert.Contains(BookingAction.CancelUnpaid, (await scoped.Bookings.GetAsync(id)).AllowedActions);
        // Full manager sees both own and unrelated bookings without using session.HasAllAreas as a cached grant.
        Assert.Contains((await manager.Bookings.ListAsync(new() { Queue = BookingQueue.All, HomestayId = d.HomestayId })).Items, b => b.Id == id);
        Assert.Equal(managerIdentity.EmployeeId, manager.Base.Current.RequireSession().EmployeeId);
    }
    [SqlFact]
    public async Task AreasValidateActiveCatalogRoleAndWriteOldNewAudit()
    {
        var c = new M51TestClient(); await c.LoginAsync(); var d = await M21Data.CreateAsync(c.Base);
        var staff = await c.Base.Login.CreateIdentityAsync(Position.Staff);
        var accountant = await c.Base.Login.CreateIdentityAsync(Position.Accountant);
        var v = (await c.Staff.GetAreasAsync(staff.EmployeeId)).RowVer;
        await Assert.ThrowsAsync<BusinessException>(() => c.Staff.SetAreasAsync(new(staff.EmployeeId, [], true, v)));
        var av = (await c.Staff.GetAreasAsync(accountant.EmployeeId)).RowVer;
        await Assert.ThrowsAsync<BusinessException>(() => c.Staff.SetAreasAsync(new(accountant.EmployeeId, [d.AreaId], false, av)));
        await c.Base.Login.ExecuteAsync("UPDATE dbo.Areas SET IsActive=0 WHERE Id=@id;", d.AreaId);
        await Assert.ThrowsAsync<BusinessException>(() => c.Staff.SetAreasAsync(new(staff.EmployeeId, [d.AreaId], false, v)));
        Assert.Equal(v, (await c.Staff.GetAreasAsync(staff.EmployeeId)).RowVer); // Failed set rolls back aggregate token.
        await c.Base.Login.ExecuteAsync("UPDATE dbo.Areas SET IsActive=1 WHERE Id=@id;", d.AreaId);
        await c.Staff.SetAreasAsync(new(staff.EmployeeId, [d.AreaId], false, v));
        var detail = await c.Staff.GetAreasAsync(staff.EmployeeId); Assert.Equal(d.AreaId, Assert.Single(detail.Areas).Id);
        // Lọc theo mã: trang đầu còn có mọi QL toàn khu vực do các test chạy song song tạo ra.
        Assert.Contains((await c.Staff.ListByAreaAsync(new() { AreaId = d.AreaId, Page = new() { SearchText = staff.Code } })).Items, e => e.EmployeeId == staff.EmployeeId);
        Assert.Equal(1, await c.CountAsync("SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Employees' AND ActionCode='Staff.SetAreas' AND RecordId=CONVERT(varchar(50),@id) AND OldValues IS NOT NULL AND NewValues IS NOT NULL;", staff.EmployeeId));
    }
    private static async Task<BookingAssignmentItem> Item(M51TestClient c, int id)
    {
        var b = (await c.Bookings.GetAsync(id)).Booking; return new(id, b.RowVer) { BookingCode = b.Code };
    }
    [SqlFact]
    public async Task AssignManyCommitsEveryBookingAuditAndOneNotificationWithoutFakeStatusHistory()
    {
        var c = new M51TestClient(); await c.LoginAsync(); var d = await M21Data.CreateAsync(c.Base);
        var receiver = await c.Base.Login.CreateIdentityAsync(Position.Staff); await c.SetAreasAsync(receiver.EmployeeId, [d.AreaId]);
        var first = await d.BookingAsync(c.Base, 0); var second = await d.BookingAsync(c.Base, 1);
        var items = new[] { await Item(c, first), await Item(c, second) };
        var result = await c.Bookings.AssignManyAsync(new(items, receiver.EmployeeId));
        Assert.Equal(2, result.Count); Assert.All(result, b => Assert.Equal(receiver.EmployeeId, b.Booking.AssignedEmployeeId));
        foreach (var id in new[] { first, second })
        {
            Assert.Equal(1, await c.CountAsync("SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Bookings' AND ActionCode='Booking.Reassign' AND RecordId=CONVERT(varchar(50),@id);", id));
            Assert.Equal(1, await c.CountAsync("SELECT COUNT(*) FROM dbo.BookingStatusHistories WHERE BookingId=@id;", id));
        }
        Assert.Equal(1, await c.CountAsync("SELECT COUNT(*) FROM dbo.Notifications WHERE UserId=@id AND RefType='Booking';", receiver.UserId));
    }
    [SqlFact]
    public async Task AssignManyStaleSecondBookingRollsBackFirstAndNamesFailedCode()
    {
        var c = new M51TestClient(); await c.LoginAsync(); var d = await M21Data.CreateAsync(c.Base);
        var receiver = await c.Base.Login.CreateIdentityAsync(Position.Staff); await c.SetAreasAsync(receiver.EmployeeId, [d.AreaId]);
        var first = await d.BookingAsync(c.Base, 0); var second = await d.BookingAsync(c.Base, 1);
        var a = await Item(c, first); var b = await Item(c, second);
        await c.Base.Login.ExecuteAsync("UPDATE dbo.Bookings SET UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;", second);
        var ex = await Assert.ThrowsAsync<ConcurrencyException>(() => c.Bookings.AssignManyAsync(new([a, b], receiver.EmployeeId)));
        Assert.Contains(b.BookingCode!, ex.Message);
        Assert.Equal(a.RowVer, (await c.Bookings.GetAsync(first)).Booking.RowVer);
        Assert.Equal(0, await c.CountAsync("SELECT COUNT(*) FROM dbo.Notifications WHERE UserId=@id;", receiver.UserId));
        Assert.Equal(0, await c.CountAsync("SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Bookings' AND ActionCode='Booking.Reassign' AND RecordId=CONVERT(varchar(50),@id);", first));
    }
    [SqlFact]
    public async Task AssignManyRequiresReceiverAreaForEveryBookingAndRollsBackWhenOneOutsideActorScope()
    {
        var c = new M51TestClient(); await c.LoginAsync(); var a = await M21Data.CreateAsync(c.Base); var b = await M21Data.CreateAsync(c.Base);
        var receiver = await c.Base.Login.CreateIdentityAsync(Position.Staff); await c.SetAreasAsync(receiver.EmployeeId, [a.AreaId]);
        var first = await a.BookingAsync(c.Base, 0); var second = await b.BookingAsync(c.Base, 0);
        var items = new[] { await Item(c, first), await Item(c, second) };
        Assert.DoesNotContain(await c.Bookings.ListAssigneesAsync(second), e => e.Id == receiver.EmployeeId);
        var ex = await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.AssignManyAsync(new(items, receiver.EmployeeId)));
        Assert.Contains(items[1].BookingCode!, ex.Message); Assert.Equal(items[0].RowVer, (await c.Bookings.GetAsync(first)).Booking.RowVer);
        await c.SetAreasAsync(receiver.EmployeeId, [a.AreaId, b.AreaId]);
        var scoped = new M51TestClient(); await scoped.LoginAsync(Position.Manager, a.AreaId, false);
        var outside = await Assert.ThrowsAsync<NotFoundException>(() => scoped.Bookings.AssignManyAsync(new(items, receiver.EmployeeId)));
        Assert.Contains(items[1].BookingCode!, outside.Message); Assert.Equal(items[0].RowVer, (await c.Bookings.GetAsync(first)).Booking.RowVer);
        Assert.Equal(0, await c.CountAsync("SELECT COUNT(*) FROM dbo.Notifications WHERE UserId=@id;", receiver.UserId));
    }
    [SqlFact]
    public async Task TwoRealSessionsClaimingSameUnassignedBookingProduceExactlyOneSuccess()
    {
        var setup = new M51TestClient(); await setup.LoginAsync(); var d = await M21Data.CreateAsync(setup.Base); var id = await d.BookingAsync(setup.Base, 0, assigned: false);
        var a = new M51TestClient(); var ai = await a.LoginAsync(Position.Staff, d.AreaId, false);
        var b = new M51TestClient(); var bi = await b.LoginAsync(Position.Staff, d.AreaId, false);
        var version = (await setup.Bookings.GetAsync(id)).Booking.RowVer;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Claim(M51TestClient c) { await gate.Task; return await Record.ExceptionAsync(() => c.Bookings.ClaimAsync(new(id, version))); }
        var results = new[] { Claim(a), Claim(b) }; gate.SetResult(); var errors = await Task.WhenAll(results);
        Assert.Single(errors, e => e is null); Assert.IsType<ConcurrencyException>(Assert.Single(errors, e => e is not null));
        Assert.Contains((await setup.Bookings.GetAsync(id)).Booking.AssignedEmployeeId!.Value, new[] { ai.EmployeeId, bi.EmployeeId });
        Assert.Equal(1, await setup.CountAsync("SELECT COUNT(*) FROM dbo.AuditLogs WHERE ActionCode='Booking.Claim' AND TableName='Bookings' AND RecordId=CONVERT(varchar(50),@id);", id));
    }
}
