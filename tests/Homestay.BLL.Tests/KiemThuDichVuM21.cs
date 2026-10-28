using System.Diagnostics;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DTO.Availability;
using Homestay.DTO.Bookings;
using Homestay.DTO.Common;
using Homestay.DTO.Customers;
using Homestay.DTO.Enums;
using Homestay.DAL.Infrastructure;

namespace Homestay.BLL.Tests;

public sealed class M21ServiceTests
{
    private static AvailabilitySearchRequest Search(M21Data d) => new() { AreaId = d.AreaId, CheckIn = M21Data.CheckIn, CheckOut = M21Data.CheckOut, Guests = 2 };
    private static QuoteRequest Quote(M21Data d) => new(d.RoomTypeId, M21Data.CheckIn, M21Data.CheckOut, 2);

    [SqlFact]
    public async Task AvailabilityCountsOnlyEnabledRoomsAndIncludesSoldOutTypes()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        await d.BookingAsync(c, 0); await d.BlockAsync(c, 1, RoomBlockReason.Maintenance);
        await c.Login.ExecuteAsync("UPDATE dbo.Rooms SET IsActive=0 WHERE Id=@id;", d.Rooms[2]);
        var item = Assert.Single(await c.Availability.SearchAsync(Search(d)));
        Assert.Equal(3, item.TotalActiveRooms); Assert.Equal(1, item.AvailableRooms);
        Assert.Equal(3, item.CancellationPolicy.Rules.Count);
        Assert.Single(await c.Availability.SearchAsync(Search(d) with { CategoryId = d.CategoryId, AmenityIds = [d.AmenityId] }));
        Assert.Empty(await c.Availability.SearchAsync(Search(d) with { AmenityIds = [int.MaxValue] }));
        Assert.Empty(await c.Availability.SearchAsync(Search(d) with { MinAveragePrice = item.AveragePrice + 1 }));
        Assert.Empty(await c.Availability.SearchAsync(Search(d) with { MaxAveragePrice = item.AveragePrice - 1 }));
        await d.BookingAsync(c, 3);
        Assert.Equal(0, Assert.Single(await c.Availability.SearchAsync(Search(d))).AvailableRooms);
        Assert.Empty((await c.Availability.QuoteAsync(Quote(d))).AvailableRooms);
    }

    [SqlFact]
    public async Task SellingRequiresActiveHomestayTypeCapacityContractAndAllEffectiveRates()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        Assert.Single(await c.Availability.SearchAsync(Search(d)));
        Assert.Empty(await c.Availability.SearchAsync(Search(d) with { Guests = 5 }));
        await c.Login.ExecuteAsync("UPDATE dbo.Homestays SET Status='Suspended' WHERE Id=@id;", d.HomestayId);
        Assert.Empty(await c.Availability.SearchAsync(Search(d)));
        await c.Login.ExecuteAsync("UPDATE dbo.Homestays SET Status='Active' WHERE Id=@id;", d.HomestayId);
        await c.Login.ExecuteAsync("UPDATE dbo.RoomTypes SET IsActive=0 WHERE Id=@id;", d.RoomTypeId);
        Assert.Empty(await c.Availability.SearchAsync(Search(d)));
        await c.Login.ExecuteAsync("UPDATE dbo.RoomTypes SET IsActive=1 WHERE Id=@id;", d.RoomTypeId);
        await c.Login.ExecuteAsync("UPDATE dbo.Contracts SET EndDate='2099-01-02' WHERE HomestayId=@id;", d.HomestayId);
        Assert.Empty(await c.Availability.SearchAsync(Search(d)));
        await c.Login.ExecuteAsync("UPDATE dbo.Contracts SET EndDate='2099-01-03' WHERE HomestayId=@id;", d.HomestayId);
        Assert.Single(await c.Availability.SearchAsync(Search(d))); // EndDate = CheckOut hợp lệ.
        await c.Login.ExecuteAsync("UPDATE dbo.RoomTypePrices SET EffectiveFrom='2099-02-01' WHERE RoomTypeId=@id AND DayType='Holiday';", d.RoomTypeId);
        Assert.Empty(await c.Availability.SearchAsync(Search(d)));
        var error = await Assert.ThrowsAsync<MissingRoomPriceException>(() => c.Availability.QuoteAsync(Quote(d)));
        Assert.Equal(DayType.Holiday, error.DayType); Assert.Equal(M21Data.CheckIn, error.Night);
    }

    [SqlFact]
    public async Task ExpiredHoldsAreReadAsAvailableExceptPendingDepositsWithoutWritingAnything()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c, 2);
        var expired = await d.BookingAsync(c, 0, expiredHold: true);
        var pending = await d.BookingAsync(c, 1, expiredHold: true); await d.PendingDepositAsync(c, pending);
        Assert.Equal(1, Assert.Single(await c.Availability.SearchAsync(Search(d))).AvailableRooms);
        Assert.Single((await c.Availability.QuoteAsync(Quote(d))).AvailableRooms);
        var calendar = await c.Availability.GetCalendarAsync(new(d.HomestayId, null, M21Data.CheckIn));
        var free = calendar.Rooms.Single(r => r.RoomId == d.Rooms[0]).Cells[0];
        Assert.Equal(RoomCalendarState.Available, free.State); Assert.Null(free.BookingId); Assert.Null(free.CustomerName); Assert.Null(free.HoldExpiresAtUtc);
        Assert.Equal(RoomCalendarState.Held, calendar.Rooms.Single(r => r.RoomId == d.Rooms[1]).Cells[0].State);
        Assert.Equal("AwaitingPayment", await c.ScalarAsync<string>("SELECT Status FROM dbo.Bookings WHERE Id=@id;", cmd => cmd.Parameters.AddInt32("@id", expired)));
        Assert.Equal(2, await c.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", cmd => cmd.Parameters.AddInt32("@id", expired)));
    }

    [SqlFact]
    public async Task CalendarMapsEveryStateAndOccupiedCellsTakePriorityOverOffSale()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c, 13);
        var today = DateOnly.FromDateTime((await new DbClock(c.Factory).GetUtcNowAsync(CancellationToken.None)).AddHours(7));
        var date = today.AddDays(-2);
        var statuses = new[] { BookingStatus.AwaitingPayment, BookingStatus.PaymentSubmitted, BookingStatus.Deposited, BookingStatus.Confirmed, BookingStatus.CheckedIn, BookingStatus.Completed };
        var ids = new List<int>();
        for (var i = 0; i < statuses.Length; i++) ids.Add(await d.BookingAsync(c, i + 1, statuses[i], checkIn: date, checkOut: date.AddDays(1)));
        await d.BlockAsync(c, 7, RoomBlockReason.OtherChannel, date); await d.BlockAsync(c, 8, RoomBlockReason.Maintenance, date);
        await c.Login.ExecuteAsync("UPDATE dbo.Rooms SET IsActive=0 WHERE Id=@id;", d.Rooms[10]);
        await c.Login.ExecuteAsync("UPDATE dbo.Rooms SET IsActive=0 WHERE Id=@id;", d.Rooms[4]);
        await d.BookingAsync(c, 11, expiredHold: true, checkIn: date, checkOut: date.AddDays(1));
        var pending = await d.BookingAsync(c, 12, expiredHold: true, checkIn: date, checkOut: date.AddDays(1)); await d.PendingDepositAsync(c, pending);
        await c.MutateAsync("""
            INSERT dbo.BookingChangeRequests(BookingId,RequestType,NewCheckIn,NewCheckOut,Reason,Status,RequestedByUserId,RequestedAt)
            VALUES(@booking,'ChangeDates',@date,DATEADD(day,1,@date),N'Test đổi','Pending',@user,SYSUTCDATETIME());
            DECLARE @change int=CONVERT(int,SCOPE_IDENTITY()); INSERT dbo.RoomNights(RoomId,[Date],ChangeRequestId) VALUES(@room,@date,@change);
            """, cmd => { cmd.Parameters.AddInt32("@booking", ids[3]); cmd.Parameters.AddDate("@date", date); cmd.Parameters.AddInt32("@user", c.Current.RequireSession().UserId); cmd.Parameters.AddInt32("@room", d.Rooms[9]); });
        var calendar = await c.Availability.GetCalendarAsync(new(null, d.AreaId, date, 31));
        var expected = new[] { RoomCalendarState.Available,RoomCalendarState.Held,RoomCalendarState.Held,RoomCalendarState.Booked,RoomCalendarState.Booked,
            RoomCalendarState.Occupied,RoomCalendarState.Stayed,RoomCalendarState.OtherChannelBlock,RoomCalendarState.MaintenanceBlock,RoomCalendarState.ChangeRequestHold,
            RoomCalendarState.OffSale,RoomCalendarState.Available,RoomCalendarState.Held };
        for (var i = 0; i < expected.Length; i++)
        {
            var row = calendar.Rooms.Single(r => r.RoomId == d.Rooms[i]); Assert.Equal(31, row.Cells.Count); Assert.Equal(expected[i], row.Cells[0].State);
        }
        Assert.NotNull(calendar.Rooms.Single(r => r.RoomId == d.Rooms[9]).Cells[0].BookingCode);
        Assert.Equal(RoomCalendarState.OffSale, calendar.Rooms.Single(r => r.RoomId == d.Rooms[4]).Cells[1].State);
    }

    [SqlFact]
    public async Task QuoteCallsSharedFormulaReturnsRoomsAndBankAndMasksStaffCommission()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var manager = await c.Availability.QuoteAsync(Quote(d)); Assert.Equal(.12m, manager.CommissionRate);
        await c.LoginNewAsync(Position.Staff, allAreas: false); // Không có khu vực vẫn tra toàn hệ thống.
        var quote = await c.Bookings.QuoteAsync(Quote(d)); Assert.Null(quote.CommissionRate);
        var holidays = quote.Pricing.Nights.Where(n => n.DayType == DayType.Holiday).Select(n => n.Date).ToHashSet();
        var formula = PricingCalculator.Calculate(M21Data.CheckIn, M21Data.CheckOut, holidays,
            [new(DayType.Weekday, new(2098, 1, 1), 1_000_000), new(DayType.Weekend, new(2098, 1, 1), 1_200_000), new(DayType.Holiday, new(2098, 1, 1), 1_500_000)], quote.Pricing.DepositRate);
        Assert.Equal(formula.TotalAmount, quote.Pricing.TotalAmount); Assert.Equal(formula.DepositAmount, quote.AmountDue);
        Assert.Equal(4, quote.AvailableRooms.Count); Assert.False(string.IsNullOrWhiteSpace(quote.Bank.AccountNo));
        Assert.Equal(quote.Pricing.TotalAmount, Assert.Single(await c.Availability.SearchAsync(Search(d))).TotalAmount);
    }

    [SqlFact]
    public async Task BookingTabsAreScopedButExactCodePhoneAndDetailCanReadOutsideWithoutActions()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var a = await M21Data.CreateAsync(c); var b = await M21Data.CreateAsync(c);
        var inside = await a.BookingAsync(c, 0); var outside = await b.BookingAsync(c, 0, assigned: false);
        await c.LoginNewAsync(Position.Staff, a.AreaId, false);
        foreach (var queue in new[] { BookingQueue.All, BookingQueue.Unassigned, BookingQueue.AwaitingPayment })
            Assert.Empty((await c.Bookings.ListAsync(new() { Queue = queue, HomestayId = b.HomestayId })).Items);
        Assert.Contains((await c.Bookings.ListAsync(new() { Queue = BookingQueue.All, HomestayId = a.HomestayId })).Items, x => x.Id == inside);
        var detail = await c.Bookings.GetAsync(outside); Assert.Empty(detail.AllowedActions); Assert.Equal("••••••••9012", detail.Customer.IdNumber); Assert.Null(detail.CommissionRate);
        Assert.Equal(outside, (await c.Bookings.GetByCodeAsync(detail.Booking.Code)).Booking.Id);
        Assert.Equal(outside, Assert.Single((await c.Bookings.ListAsync(new() { Code = detail.Booking.Code })).Items).Id);
        Assert.Contains((await c.Bookings.ListAsync(new() { Phone = detail.Customer.Phone })).Items, x => x.Id == outside);
        var own = await b.BookingAsync(c, 1);
        Assert.Contains((await c.Bookings.ListAsync(new() { Queue = BookingQueue.Mine, HomestayId = b.HomestayId })).Items, x => x.Id == own);
        Assert.Contains(BookingAction.RecordDeposit, (await c.Bookings.GetAsync(own)).AllowedActions);
        Assert.Contains(BookingAction.Reassign, (await c.Bookings.GetAsync(own)).AllowedActions);
        // Phạm vi còn bao gồm đơn mình tạo dù đã giao người khác.
        await c.Login.ExecuteAsync("UPDATE dbo.Bookings SET AssignedEmployeeId=NULL WHERE Id=@id;", own);
        Assert.Contains((await c.Bookings.ListAsync(new() { Queue = BookingQueue.Unassigned, HomestayId = b.HomestayId })).Items, x => x.Id == own);
    }

    [SqlFact]
    public async Task AccountantReadsAllBookingPaymentStatesFromViewAndHasNoBookingActions()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var id = await d.BookingAsync(c, 0); await d.PendingDepositAsync(c, id);
        await c.LoginNewAsync(Position.Accountant, allAreas: false);
        var row = Assert.Single((await c.Bookings.ListAsync(new() { Queue = BookingQueue.All, HomestayId = d.HomestayId })).Items);
        var view = await c.ScalarAsync<string>("SELECT PaymentState FROM dbo.vw_BookingPaymentState WHERE BookingId=@id;", cmd => cmd.Parameters.AddInt32("@id", id));
        Assert.Equal(view, row.PaymentState.ToString()); Assert.Equal(BookingStatus.AwaitingPayment, row.Status); Assert.Null(row.DeadlineAtUtc);
        var detail = await c.Bookings.GetAsync(id); Assert.Empty(detail.AllowedActions); Assert.Equal(.12m, detail.CommissionRate); Assert.True(detail.Customer.IsIdentityMasked);
        Assert.Single(detail.Payments); Assert.Single(detail.History); Assert.Empty(detail.Audit);
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Customers.CreateAsync(new() { FullName = "Test", Phone = M14TestClient.Phone() }));
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Availability.SearchAsync(Search(d)));
    }

    [SqlFact]
    public async Task CustomerIdentityMaskUpdateRowVersionAndPhonePermissionsAreEnforced()
    {
        var c = new M21TestClient(); var manager = await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var full = await c.Customers.GetAsync(d.CustomerId); Assert.Equal("123456789012", full.Customer.IdNumber); Assert.False(full.Customer.IsIdentityMasked);
        await c.LoginNewAsync(Position.Staff);
        var hidden = await c.Customers.GetAsync(d.CustomerId); Assert.Equal("••••••••9012", hidden.Customer.IdNumber); Assert.Empty(hidden.UsableVouchers);
        var changed = await c.Customers.UpdateAsync(new() { CustomerId = d.CustomerId, FullName = "Tên đã sửa", Email = " edit@example.test ", Note = "Ghi chú", RowVer = hidden.Customer.RowVer });
        Assert.Equal("Tên đã sửa", changed.Customer.FullName);
        await Assert.ThrowsAsync<ConcurrencyException>(() => c.Customers.UpdateAsync(new() { CustomerId = d.CustomerId, FullName = "Ghi đè", RowVer = hidden.Customer.RowVer }));
        var phone = await c.Customers.ChangePhoneAsync(new(d.CustomerId, M14TestClient.Phone(), changed.Customer.RowVer)); // Chưa đơn: NV được đổi.
        var booking = await d.BookingAsync(c, 0);
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Customers.ChangePhoneAsync(new(d.CustomerId, M14TestClient.Phone(), phone.Customer.RowVer)));
        await c.Login.LoginAsync(manager);
        var detail = await c.Customers.GetAsync(d.CustomerId); Assert.Equal("123456789012", detail.Customer.IdNumber); Assert.Single(detail.RecentBookings);
        var newPhone = await c.Customers.ChangePhoneAsync(new(d.CustomerId, M14TestClient.Phone(), detail.Customer.RowVer)); Assert.NotEqual(detail.Customer.Phone, newPhone.Customer.Phone);
        var audit = await c.ScalarAsync<string>("SELECT STRING_AGG(CONVERT(nvarchar(max),COALESCE(OldValues,N'')+COALESCE(NewValues,N'')),N'') FROM dbo.AuditLogs WHERE TableName='Customers' AND RecordId=@id;", cmd => cmd.Parameters.AddVarChar("@id", 50, d.CustomerId.ToString()));
        Assert.DoesNotContain("123456789012", audit); Assert.Contains("9012", audit);
        Assert.Equal(booking, Assert.Single(newPhone.RecentBookings).Id);
    }

    [SqlFact]
    public async Task ConcurrentCustomerCreatesReturnExistingPhoneAndPagingIsLiteral()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var other = new M21TestClient(); await other.LoginNewAsync();
        var phone = M14TestClient.Phone(); var request = new CreateCustomerRequest { FullName = "Khách [M21]% " + Guid.NewGuid().ToString("N"), Phone = phone, IdNumber = "123456789012" };
        var results = await Task.WhenAll(c.Customers.CreateAsync(request), other.Customers.CreateAsync(request with { FullName = request.FullName + " B" }));
        Assert.Equal(results[0].Customer.Id, results[1].Customer.Id);
        var found = await c.Customers.FindByPhoneAsync("+84 " + phone[1..]); Assert.Equal(results[0].Customer.Id, found!.Id);
        var same = await c.Customers.CreateAsync(request with { FullName = "Không ghi đè" }); Assert.Equal(results[0].Customer.FullName, same.Customer.FullName);
        var literal = await c.Customers.SearchAsync(new() { Page = new() { SearchText = results[0].Customer.FullName, PageSize = 10 } }); Assert.Single(literal.Items);
        Assert.Empty((await c.Customers.SearchAsync(new() { Page = new() { SearchText = results[0].Customer.FullName, PageSize = 10, PageNumber = 2 } })).Items);
        await Assert.ThrowsAsync<BusinessException>(() => c.Customers.SearchAsync(new() { Page = new() { SortColumn = "Phone; DROP TABLE Customers" } }));
    }

    [SqlFact]
    public async Task NotificationsOnlyExposeAndUpdateCurrentUserAndUnreadComeFirst()
    {
        var c = new M21TestClient(); var first = await c.LoginNewAsync(); var other = new M21TestClient(); var second = await other.LoginNewAsync(Position.Accountant);
        await c.MutateAsync("""
            INSERT dbo.Notifications(UserId,Message,RefType,RefId,IsRead,CreatedAt) VALUES
                (@first,N'M21 read','Booking','1',1,DATEADD(minute,1,SYSUTCDATETIME())),
                (@first,N'M21 unread','Booking','2',0,SYSUTCDATETIME()),(@second,N'M21 private','Customer','3',0,SYSUTCDATETIME());
            """, cmd => { cmd.Parameters.AddInt32("@first", first.UserId); cmd.Parameters.AddInt32("@second", second.UserId); });
        Assert.Equal(1, await c.Notifications.CountUnreadAsync()); var list = await c.Notifications.ListAsync(new() { PageSize = 10 }); Assert.Equal(2, list.TotalCount);
        Assert.False(list.Items[0].IsRead); Assert.Equal("Booking", list.Items[0].RefType); Assert.Equal("2", list.Items[0].RefId);
        var privateItem = Assert.Single((await other.Notifications.ListAsync(new())).Items);
        await Assert.ThrowsAsync<NotFoundException>(() => c.Notifications.MarkReadAsync(privateItem.Id));
        await c.Notifications.MarkReadAsync(list.Items[0].Id); Assert.Equal(0, await c.Notifications.CountUnreadAsync());
        await c.Notifications.MarkAllReadAsync(); Assert.Equal(1, await other.Notifications.CountUnreadAsync());
        await other.Notifications.MarkAllReadAsync(); Assert.Equal(0, await other.Notifications.CountUnreadAsync());
    }

    [SqlFact]
    public async Task DashboardQueuesUrgentDeadlinesAndTransferInstructionsUseDatabaseClock()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var hold = await d.BookingAsync(c, 0); var deposited = await d.BookingAsync(c, 1, BookingStatus.Deposited);
        await c.Login.ExecuteAsync("UPDATE dbo.Bookings SET HoldExpiresAt=DATEADD(minute,3,SYSUTCDATETIME()) WHERE Id=@id;", hold);
        var now = await new DbClock(c.Factory).GetUtcNowAsync(CancellationToken.None); var today = DateOnly.FromDateTime(now.AddHours(7));
        await d.BookingAsync(c, 2, BookingStatus.Confirmed, checkIn: today, checkOut: today.AddDays(1));
        await d.BookingAsync(c, 3, BookingStatus.CheckedIn, checkIn: today.AddDays(-1), checkOut: today);
        await c.MutateAsync("""
            INSERT dbo.WorkTasks(Title,AssigneeEmployeeId,CreatedByUserId,BookingId,Priority,DueAt,Status)
            VALUES(N'M21 gọi chủ',@employee,@user,@booking,1,DATEADD(minute,-5,SYSUTCDATETIME()),'Open');
            INSERT dbo.Notifications(UserId,Message,IsRead) VALUES(@user,N'M21 mới',0);
            """, cmd => { cmd.Parameters.AddInt32("@employee", c.Current.RequireSession().EmployeeId); cmd.Parameters.AddInt32("@user", c.Current.RequireSession().UserId); cmd.Parameters.AddInt32("@booking", deposited); });
        var dashboard = await c.Bookings.GetDashboardAsync(); Assert.Equal(today, dashboard.Today); Assert.Equal(1, dashboard.ExpiringHolds); Assert.Equal(1, dashboard.OverdueConfirmations);
        Assert.Equal(1, dashboard.DueTasks); Assert.Equal(1, dashboard.UnreadNotifications); Assert.True(dashboard.ArrivalsToday >= 1); Assert.True(dashboard.DeparturesToday >= 1);
        Assert.Equal(4, dashboard.MyQueues.Single(q => q.Queue == BookingQueue.All).Count);
        Assert.Equal(dashboard.UrgentItems.OrderBy(i => i.DeadlineAtUtc).Select(i => i.DeadlineAtUtc), dashboard.UrgentItems.Select(i => i.DeadlineAtUtc));
        Assert.Single((await c.Bookings.ListAsync(new() { Queue = BookingQueue.ArrivalsToday, HomestayId = d.HomestayId })).Items);
        Assert.Single((await c.Bookings.ListAsync(new() { Queue = BookingQueue.DeparturesToday, HomestayId = d.HomestayId })).Items);
        var instruction = await c.Bookings.GetTransferInstructionsAsync(hold); Assert.Equal(instruction.Code, instruction.TransferContent); Assert.Equal(600000, instruction.AmountDue);
        Assert.NotEmpty(instruction.Bank.BankName); Assert.True(instruction.HoldExpiresAtUtc > dashboard.ServerNowUtc);
    }

    [SqlFact]
    public async Task QuickSearchAcceptsBookingCodeWithoutHyphenAndEvidenceIsBoundToBooking()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c); var id = await d.BookingAsync(c, 0);
        var code = "FJ" + Random.Shared.NextInt64(100000, 999999).ToString() + "-" + Random.Shared.Next(100, 1000).ToString();
        await c.MutateAsync("UPDATE dbo.Bookings SET Code=@code WHERE Id=@id;", cmd => { cmd.Parameters.AddVarChar("@code", 20, code); cmd.Parameters.AddInt32("@id", id); });
        Assert.Equal(id, Assert.Single((await c.Bookings.QuickSearchAsync(code.Replace("-", ""))).Bookings).Id);
        var customer = await c.Customers.GetAsync(d.CustomerId); Assert.Equal(d.CustomerId, Assert.Single((await c.Bookings.QuickSearchAsync(customer.Customer.Phone)).Customers).Id);
        var file = Guid.NewGuid(); var content = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x2d };
        await d.PendingDepositAsync(c, id);
        await c.MutateAsync("""
            INSERT dbo.StoredFiles(Id,Category,FileName,ContentType,SizeBytes,Sha256,Content,UploadedByUserId)
            VALUES(@file,'Evidence',N'test.pdf','application/pdf',5,HASHBYTES('SHA2_256',@bytes),@bytes,@user);
            UPDATE dbo.Payments SET EvidenceFileId=@file WHERE BookingId=@id AND Type='Deposit';
            """, cmd => { cmd.Parameters.AddGuid("@file", file); cmd.Parameters.AddVarBinary("@bytes", content); cmd.Parameters.AddInt32("@user", c.Current.RequireSession().UserId); cmd.Parameters.AddInt32("@id", id); });
        Assert.Equal(content, (await c.Files.GetEvidenceAsync(id, file)).Content);
        await Assert.ThrowsAsync<NotFoundException>(() => c.Files.GetEvidenceAsync(int.MaxValue, file));
    }

    [SqlFact]
    public async Task SeedSearchFor31NightsCompletesUnderFiveSeconds()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(Position.Staff);
        var now = await new DbClock(c.Factory).GetUtcNowAsync(CancellationToken.None); var today = DateOnly.FromDateTime(now.AddHours(7));
        var watch = Stopwatch.StartNew();
        var rows = await c.Availability.SearchAsync(new() { CheckIn = today, CheckOut = today.AddDays(31), Guests = 1 });
        watch.Stop(); Assert.NotEmpty(rows); Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"Tra 31 đêm mất {watch.Elapsed.TotalSeconds:F3}s");
    }

    [SqlFact]
    public async Task BookingPagingQueuesFiltersAndManagerAreaScopeWorkTogether()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var a = await M21Data.CreateAsync(c, 12); var b = await M21Data.CreateAsync(c, 1);
        for (var i = 0; i < 12; i++) await a.BookingAsync(c, i, i % 3 == 0 ? BookingStatus.PaymentSubmitted : i % 3 == 1 ? BookingStatus.Deposited : BookingStatus.AwaitingPayment, assigned: i != 0);
        var outside = await b.BookingAsync(c, 0, assigned: false);
        var page = await c.Bookings.ListAsync(new() { Queue = BookingQueue.All, HomestayId = a.HomestayId, Page = new() { PageSize = 10, SortColumn = "Code", SortDescending = true } });
        Assert.Equal(12, page.TotalCount); Assert.Equal(10, page.Items.Count);
        var next = await c.Bookings.ListAsync(new() { Queue = BookingQueue.All, HomestayId = a.HomestayId, Page = new() { PageSize = 10, PageNumber = 2, SortColumn = "Code", SortDescending = true } });
        Assert.Equal(12, next.TotalCount); Assert.Equal(2, next.Items.Count); Assert.Empty(page.Items.Select(x => x.Id).Intersect(next.Items.Select(x => x.Id)));
        foreach (var queue in new[] { BookingQueue.DepositVerification, BookingQueue.OwnerConfirmation, BookingQueue.AwaitingPayment })
            Assert.Equal(4, (await c.Bookings.ListAsync(new() { Queue = queue, HomestayId = a.HomestayId })).TotalCount);
        Assert.Equal(4, (await c.Bookings.ListAsync(new() { Queue = BookingQueue.All, HomestayId = a.HomestayId, Status = BookingStatus.Deposited, CheckInFrom = M21Data.CheckIn, CheckInTo = M21Data.CheckIn, OnlyOverdueConfirmation = true })).TotalCount);
        var first = page.Items[0];
        await c.MutateAsync("""
            INSERT dbo.BookingChangeRequests(BookingId,RequestType,Reason,Status,RequestedByUserId,RequestedAt)
            VALUES(@id,'Cancel',N'Khách yêu cầu','Pending',@user,SYSUTCDATETIME());
            """, cmd => { cmd.Parameters.AddInt32("@id", first.Id); cmd.Parameters.AddInt32("@user", c.Current.RequireSession().UserId); });
        Assert.Single((await c.Bookings.ListAsync(new() { Queue = BookingQueue.OpenChangeRequests, HomestayId = a.HomestayId })).Items);
        await c.LoginNewAsync(Position.Manager, a.AreaId, false);
        Assert.Empty((await c.Bookings.ListAsync(new() { Queue = BookingQueue.All, HomestayId = b.HomestayId })).Items);
        Assert.Empty((await c.Bookings.GetAsync(outside)).AllowedActions);
        await c.LoginNewAsync(Position.Manager, allAreas: true);
        Assert.Single((await c.Bookings.ListAsync(new() { Queue = BookingQueue.Unassigned, HomestayId = b.HomestayId })).Items);
        Assert.Contains(BookingAction.Claim, (await c.Bookings.GetAsync(outside)).AllowedActions);
    }

    [SqlFact]
    public async Task StayValidationUsesServerVietnamDateAndCustomerWritesRevalidateRealSession()
    {
        var c = new M21TestClient(); var identity = await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var today = DateOnly.FromDateTime((await new DbClock(c.Factory).GetUtcNowAsync(CancellationToken.None)).AddHours(7));
        await Assert.ThrowsAsync<BusinessException>(() => c.Availability.SearchAsync(Search(d) with { CheckIn = today.AddDays(-1), CheckOut = today.AddDays(1) }));
        await Assert.ThrowsAsync<BusinessException>(() => c.Availability.SearchAsync(Search(d) with { CheckOut = M21Data.CheckIn }));
        await Assert.ThrowsAsync<BusinessException>(() => c.Availability.SearchAsync(Search(d) with { CheckOut = M21Data.CheckIn.AddDays(32) }));
        await Assert.ThrowsAsync<BusinessException>(() => c.Availability.QuoteAsync(Quote(d) with { Guests = 0 }));
        await c.Login.ExecuteAsync("UPDATE dbo.Users SET SecurityStamp=NEWID() WHERE Id=@id;", identity.UserId);
        await Assert.ThrowsAsync<SessionExpiredException>(() => c.Customers.CreateAsync(new() { FullName = "Khách", Phone = M14TestClient.Phone() }));
        await Assert.ThrowsAsync<SessionExpiredException>(() => c.Notifications.MarkAllReadAsync());
    }
}
