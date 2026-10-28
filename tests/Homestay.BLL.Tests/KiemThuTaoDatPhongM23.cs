using System.Globalization;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Jobs;
using Homestay.BLL.Rules;
using Homestay.BLL.Security;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Availability;
using Homestay.DTO.Bookings;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Tests;

// Existing M1.4 tests temporarily edit global settings. Jobs also scans all types.
// Serialize this collection against other classes, while individual tests explicitly launch concurrent clients.
/// <summary>Nhóm các kiểm thử M2.3 để chạy tuần tự (tránh tranh chấp dữ liệu).</summary>
[CollectionDefinition("M23 SQL", DisableParallelization = true)]
public sealed class M23SqlCollection;

/// <summary>Kiểm thử M2.3: tạo đơn khi nhiều nhân viên tranh cùng phòng, khớp báo giá, sinh mã đơn.</summary>
[Collection("M23 SQL")]
public sealed class M23BookingCreateTests
{
    private static CreateBookingRequest Request(M21Data d) => new()
    {
        CustomerId = d.CustomerId, RoomTypeId = d.RoomTypeId, CheckIn = M21Data.CheckIn, CheckOut = M21Data.CheckOut,
        Guests = 2, ContactName = "Người ở M23", ContactPhone = "0901234567", SourceChannel = SourceChannel.Phone
    };
    private static JobRunner Jobs(M21TestClient c)
    {
        var repo = new HoldExpiryRepository();
        return new(c.Factory, new DbClock(c.Factory), repo, new HoldExpiry(repo, new AuditWriter(), c.Login.Machine));
    }
    private static Task<int> Count(M21TestClient c, string sql, int id) => c.ScalarAsync<int>(sql, cmd => cmd.Parameters.AddInt32("@id", id));
    private static Task Past(M21TestClient c, int id) => c.Login.ExecuteAsync("UPDATE dbo.Bookings SET HoldExpiresAt=DATEADD(minute,-20,SYSUTCDATETIME()) WHERE Id=@id;", id);
    private static async Task<(BookingDetail? Booking, BusinessException? Error)> TryCreate(M21TestClient c, CreateBookingRequest request)
    {
        try { return (await c.Bookings.CreateAsync(request), null); }
        catch (BusinessException ex) { return (null, ex); }
    }

    [SqlFact]
    public async Task TenConcurrentEmployeesCompeteForThreeRoomsExactlyThreeSucceed()
    {
        var setup = new M21TestClient(); await setup.LoginNewAsync(); var d = await M21Data.CreateAsync(setup, 3);
        var clients = Enumerable.Range(0, 10).Select(_ => new M21TestClient()).ToArray();
        await Task.WhenAll(clients.Select(c => c.LoginNewAsync(Position.Staff, d.AreaId, false)));
        Assert.Equal(10, clients.Select(c => c.Current.RequireSession().UserId).Distinct().Count());
        Assert.Equal(10, clients.Select(c => c.Current.RequireSession().SessionId).Distinct().Count());
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = clients.Select(async c =>
        {
            var request = Request(d) with { CustomerId = null, QuickCustomer = new() { FullName = "Khách cạnh tranh", Phone = M14TestClient.Phone() } };
            await start.Task;
            return await TryCreate(c, request);
        }).ToArray();
        start.SetResult();
        var result = await Task.WhenAll(tasks);
        var success = result.Where(r => r.Booking is not null).Select(r => r.Booking!).ToArray();
        Assert.Equal(3, success.Length);
        var failures = result.Where(r => r.Error is not null).ToArray(); Assert.Equal(7, failures.Length);
        Assert.All(failures, r => Assert.Equal(BookingCreationRules.SoldOutMessage, r.Error!.Message));
        Assert.Equal(3, success.Select(b => b.Booking.RoomId).Distinct().Count());
        Assert.Equal(3, await Count(setup, "SELECT COUNT(*) FROM dbo.Bookings WHERE RoomTypeId=@id;", d.RoomTypeId));
        Assert.Equal(6, await Count(setup, "SELECT COUNT(*) FROM dbo.RoomNights rn JOIN dbo.Rooms r ON r.Id=rn.RoomId WHERE r.RoomTypeId=@id;", d.RoomTypeId));
        Assert.Equal(0, await Count(setup, "SELECT COUNT(*) FROM (SELECT rn.RoomId,rn.[Date] FROM dbo.RoomNights rn JOIN dbo.Rooms r ON r.Id=rn.RoomId WHERE r.RoomTypeId=@id GROUP BY rn.RoomId,rn.[Date] HAVING COUNT(*)>1) x;", d.RoomTypeId));
    }

    [SqlFact]
    public async Task CreationMatchesQuoteSnapshotsCounterHistoryAuditAndTransferInstructions()
    {
        var c = new M21TestClient(); var employee = await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var quote = await c.Bookings.QuoteAsync(new(d.RoomTypeId, M21Data.CheckIn, M21Data.CheckOut, 2));
        var a = await c.Bookings.CreateAsync(Request(d));
        var b = await c.Bookings.CreateAsync(Request(d));
        Assert.Equal(d.Rooms[0], a.Booking.RoomId); Assert.Equal(d.Rooms[1], b.Booking.RoomId);
        Assert.Equal(employee.EmployeeId, a.Booking.CreatedByEmployeeId); Assert.Equal(employee.EmployeeId, a.Booking.AssignedEmployeeId);
        Assert.Equal(BookingStatus.AwaitingPayment, a.Booking.Status); Assert.Equal("+84901234567", a.Booking.ContactPhone);
        Assert.Equal(quote.Pricing.Nights.ToArray(), a.Nights.ToArray());
        Assert.Equal(quote.Pricing.TotalAmount, a.TotalAmount); Assert.Equal(quote.Pricing.DepositRate, a.DepositRate);
        Assert.Equal(quote.Pricing.DepositAmount, a.DepositAmount); Assert.Equal(quote.CommissionRate, a.CommissionRate);
        Assert.Equal(quote.RoomType.CancellationPolicy.Id, a.CancellationPolicy.Id); Assert.Equal(0, a.DiscountAmount); Assert.Null(a.VoucherId);
        Assert.Equal(2, await Count(c, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", a.Booking.Id));
        Assert.Equal(2, await Count(c, "SELECT COUNT(*) FROM dbo.BookingNights WHERE BookingId=@id;", a.Booking.Id));
        var created = DateTime.SpecifyKind(await c.ScalarAsync<DateTime>("SELECT CreatedAt FROM dbo.Bookings WHERE Id=@id;", cmd => cmd.Parameters.AddInt32("@id", a.Booking.Id)), DateTimeKind.Utc);
        var minutes = await new SettingsProvider(c.Factory).GetBookingHoldMinutesAsync(CancellationToken.None);
        Assert.Equal(created.AddMinutes(minutes), a.HoldExpiresAtUtc); Assert.Equal(DateTimeKind.Utc, a.HoldExpiresAtUtc.Kind);
        Assert.Matches("^FJ[0-9]{6}-[0-9]{3,}$", a.Booking.Code);
        Assert.StartsWith(BookingCreationRules.CounterKey(created) + "-", a.Booking.Code);
        if (a.Booking.Code[..8] == b.Booking.Code[..8])
            Assert.Equal(int.Parse(a.Booking.Code[9..], CultureInfo.InvariantCulture) + 1, int.Parse(b.Booking.Code[9..], CultureInfo.InvariantCulture));
        var history = Assert.Single(a.History); Assert.Null(history.FromStatus); Assert.Equal(ActorType.Employee, history.ActorType);
        Assert.Equal(BookingStatus.AwaitingPayment, history.ToStatus); Assert.Equal("Booking.Create", Assert.Single(a.Audit).ActionCode);
        Assert.DoesNotContain("123456789012", Assert.Single(a.Audit).NewValues!);
        var transfer = Assert.IsType<BookingTransferInstructions>(a.TransferInstructions);
        Assert.Equal(a.Booking.Code, transfer.TransferContent); Assert.Equal(a.DepositAmount, transfer.AmountDue);
        Assert.Equal(await c.Bookings.GetTransferInstructionsAsync(a.Booking.Id), transfer);
        await c.Login.ExecuteAsync("UPDATE dbo.RoomTypePrices SET Price=Price+500000 WHERE RoomTypeId=@id;", d.RoomTypeId);
        var persisted = await c.Bookings.GetAsync(a.Booking.Id);
        Assert.Equal(a.TotalAmount, persisted.TotalAmount); Assert.Equal(a.Nights.ToArray(), persisted.Nights.ToArray());
    }

    [SqlFact]
    public async Task CreateReleasesExpiredHoldWithoutJobAndReadCalendarRemainsCorrect()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c, 1);
        var a = await c.Bookings.CreateAsync(Request(d)); await Past(c, a.Booking.Id);
        Assert.Equal(1, Assert.Single(await c.Availability.SearchAsync(new() { AreaId = d.AreaId, CheckIn = M21Data.CheckIn, CheckOut = M21Data.CheckOut })).AvailableRooms);
        var b = await c.Bookings.CreateAsync(Request(d) with { RoomId = d.Rooms[0] });
        Assert.Equal(a.Booking.RoomId, b.Booking.RoomId);
        var old = await c.Bookings.GetAsync(a.Booking.Id); Assert.Equal(BookingStatus.Expired, old.Booking.Status);
        Assert.Equal(ActorType.System, old.History.Last().ActorType);
        Assert.Equal(0, await Count(c, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", a.Booking.Id));
        Assert.Equal(1, await Count(c, "SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Bookings' AND RecordId=CONVERT(varchar(50),@id) AND ActionCode='Booking.Expire' AND UserId IS NULL;", a.Booking.Id));
        Assert.Equal(0, Assert.Single(await c.Availability.SearchAsync(new() { AreaId = d.AreaId, CheckIn = M21Data.CheckIn, CheckOut = M21Data.CheckOut })).AvailableRooms);
        var calendar = await c.Availability.GetCalendarAsync(new(d.HomestayId, null, M21Data.CheckIn));
        Assert.All(calendar.Rooms.Single().Cells.Take(2), cell => { Assert.Equal(RoomCalendarState.Held, cell.State); Assert.Equal(b.Booking.Id, cell.BookingId); });
    }

    [SqlFact]
    public async Task AutomaticRoomSelectionUsesRoomCodeDatabaseCollationRatherThanRoomId()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c, 2);
        await c.Login.ExecuteAsync("UPDATE dbo.Rooms SET RoomCode=N'Z001' WHERE Id=@id;", d.Rooms[0]);
        await c.Login.ExecuteAsync("UPDATE dbo.Rooms SET RoomCode=N'a001' WHERE Id=@id;", d.Rooms[1]);
        var booking = await c.Bookings.CreateAsync(Request(d));
        Assert.Equal(d.Rooms[1], booking.Booking.RoomId);
    }

    [SqlFact]
    public async Task CreationAndJobRacingToExpireSameHoldProduceOnlyOneSystemHistoryAndAudit()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c, 1);
        var a = await c.Bookings.CreateAsync(Request(d)); await Past(c, a.Booking.Id);
        var other = new M21TestClient(); await other.LoginNewAsync(Position.Staff);
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var create = Task.Run(async () => { await start.Task; return await other.Bookings.CreateAsync(Request(d)); });
        var job = Task.Run(async () => { await start.Task; return await Jobs(c).RunOnceAsync(); });
        start.SetResult(); await Task.WhenAll((Task)create, job);
        var created = await create;
        Assert.Equal(d.Rooms[0], created.Booking.RoomId);
        Assert.Equal(1, await Count(c, "SELECT COUNT(*) FROM dbo.BookingStatusHistories WHERE BookingId=@id AND ToStatus='Expired' AND ActorType='System';", a.Booking.Id));
        Assert.Equal(1, await Count(c, "SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Bookings' AND RecordId=CONVERT(varchar(50),@id) AND ActionCode='Booking.Expire' AND UserId IS NULL;", a.Booking.Id));
        Assert.Equal(2, await Count(c, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", created.Booking.Id));
    }

    [SqlFact]
    public async Task ExpiredHoldWithPendingDepositIsKeptDuringCreateAndJob()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c, 1);
        var a = await c.Bookings.CreateAsync(Request(d)); await Past(c, a.Booking.Id); await d.PendingDepositAsync(c, a.Booking.Id);
        var error = await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.CreateAsync(Request(d)));
        Assert.Equal(BookingCreationRules.SoldOutMessage, error.Message);
        await Jobs(c).RunOnceAsync();
        Assert.Equal(BookingStatus.AwaitingPayment, (await c.Bookings.GetAsync(a.Booking.Id)).Booking.Status);
        Assert.Equal(2, await Count(c, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", a.Booking.Id));
        Assert.Equal(1, await Count(c, "SELECT COUNT(*) FROM dbo.BookingStatusHistories WHERE BookingId=@id;", a.Booking.Id));
    }

    [SqlFact]
    public async Task FourthAwaitingOrderIsBlockedAndPendingCountsButUnprotectedExpiredDoesNot()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c, 4);
        var max = await new SettingsProvider(c.Factory).GetBookingMaxAwaitingPerCustomerAsync(CancellationToken.None);
        Assert.Equal(3, max); // Seed reference / G2 acceptance.
        var a = await c.Bookings.CreateAsync(Request(d));
        await c.Bookings.CreateAsync(Request(d)); await c.Bookings.CreateAsync(Request(d));
        var blocked = await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.CreateAsync(Request(d)));
        Assert.Contains("3 đơn", blocked.Message);
        await Past(c, a.Booking.Id); await d.PendingDepositAsync(c, a.Booking.Id);
        await c.Login.ExecuteAsync("UPDATE dbo.Bookings SET Status='PaymentSubmitted',PaymentSubmittedAt=SYSUTCDATETIME() WHERE Id=@id;", a.Booking.Id);
        Assert.Contains("3 đơn", (await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.CreateAsync(Request(d)))).Message);
        await c.Login.ExecuteAsync("UPDATE dbo.Payments SET Status='Rejected',RejectReason=N'Test' WHERE BookingId=@id; UPDATE dbo.Bookings SET Status='AwaitingPayment',PaymentSubmittedAt=NULL WHERE Id=@id;", a.Booking.Id);
        var next = await c.Bookings.CreateAsync(Request(d)); Assert.Equal(a.Booking.RoomId, next.Booking.RoomId);
    }

    [SqlFact]
    public async Task CustomerLimitIsSerializedAcrossTypesAndDifferentEmployeeSessions()
    {
        var setup = new M21TestClient(); await setup.LoginNewAsync(); var common = await M21Data.CreateAsync(setup);
        var types = new List<M21Data>(); for (var i = 0; i < 5; i++) types.Add(await M21Data.CreateAsync(setup, 1));
        var clients = Enumerable.Range(0, 5).Select(_ => new M21TestClient()).ToArray();
        await Task.WhenAll(clients.Select(c => c.LoginNewAsync(Position.Staff)));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = clients.Select(async (c, i) => { await start.Task; return await TryCreate(c, Request(types[i]) with { CustomerId = common.CustomerId }); }).ToArray();
        start.SetResult(); var results = await Task.WhenAll(tasks);
        Assert.Equal(3, results.Count(r => r.Booking is not null));
        Assert.All(results.Where(r => r.Error is not null), r => Assert.Contains("3 đơn", r.Error!.Message));
        Assert.Equal(3, await Count(setup, "SELECT COUNT(*) FROM dbo.Bookings WHERE CustomerId=@id;", common.CustomerId));
    }

    [SqlFact]
    public async Task ConcurrentQuickCustomersReuseOnePhoneAndAuditNeverContainsIdentity()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var other = new M21TestClient(); await other.LoginNewAsync(Position.Staff);
        var phone = M14TestClient.Phone();
        var request = Request(d) with { CustomerId = null, QuickCustomer = new() { FullName = "Khách nhanh", Phone = phone, IdNumber = "987654321098" } };
        var bookings = await Task.WhenAll(c.Bookings.CreateAsync(request), other.Bookings.CreateAsync(request));
        Assert.Equal(bookings[0].Booking.CustomerId, bookings[1].Booking.CustomerId);
        Assert.Equal(1, await Count(c, "SELECT COUNT(*) FROM dbo.Customers WHERE Id=@id;", bookings[0].Booking.CustomerId));
        Assert.Equal(1, await Count(c, "SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Customers' AND RecordId=CONVERT(varchar(50),@id) AND ActionCode='Customer.Create';", bookings[0].Booking.CustomerId));
        Assert.Equal(0, await Count(c, "SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Customers' AND RecordId=CONVERT(varchar(50),@id) AND NewValues LIKE '%987654321098%';", bookings[0].Booking.CustomerId));
    }

    [SqlFact]
    public async Task NewCounterKeyIsSafelyInitializedAndIncrementedAcrossConcurrentTransactions()
    {
        var clients = Enumerable.Range(0, 6).Select(_ => new M21TestClient()).ToArray();
        await Task.WhenAll(clients.Select(c => c.LoginNewAsync(Position.Staff)));
        var key = "M23" + Guid.NewGuid().ToString("N")[..26];
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = clients.Select(async c =>
        {
            var writes = new WriteOperationRunner(c.Factory, c.Current, new WriteSessionValidator(new AuthRepository()));
            await start.Task;
            return await writes.ExecuteAsync((db, ct) => new BookingRepository().NextSequenceAsync(db, key, ct), CancellationToken.None);
        }).ToArray();
        start.SetResult();
        Assert.Equal(Enumerable.Range(1, 6).ToArray(), (await Task.WhenAll(tasks)).Order().ToArray());
        Assert.Equal(6, await clients[0].ScalarAsync<int>("SELECT LastValue FROM dbo.Counters WHERE CounterKey=@key;", cmd => cmd.Parameters.AddVarChar("@key", 30, key)));
    }

    [SqlFact]
    public async Task RejectsInactiveHomestayTypeRoomAndContractEndingBeforeCheckout()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        await c.Login.ExecuteAsync("UPDATE dbo.Homestays SET Status='Suspended' WHERE Id=@id;", d.HomestayId);
        await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.CreateAsync(Request(d)));
        await c.Login.ExecuteAsync("UPDATE dbo.Homestays SET Status='Active' WHERE Id=@id;", d.HomestayId);
        await c.Login.ExecuteAsync("UPDATE dbo.RoomTypes SET IsActive=0 WHERE Id=@id;", d.RoomTypeId);
        await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.CreateAsync(Request(d)));
        await c.Login.ExecuteAsync("UPDATE dbo.RoomTypes SET IsActive=1 WHERE Id=@id;", d.RoomTypeId);
        await c.Login.ExecuteAsync("UPDATE dbo.Rooms SET IsActive=0 WHERE Id=@id;", d.Rooms[0]);
        await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.CreateAsync(Request(d) with { RoomId = d.Rooms[0] }));
        await c.Login.ExecuteAsync("UPDATE dbo.Contracts SET EndDate='2099-01-02' WHERE HomestayId=@id;", d.HomestayId);
        await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.CreateAsync(Request(d)));
        await c.Login.ExecuteAsync("UPDATE dbo.Contracts SET EndDate='2099-01-03' WHERE HomestayId=@id;", d.HomestayId);
        Assert.NotNull(await c.Bookings.CreateAsync(Request(d))); // EndDate == CheckOut.
    }

    [SqlFact]
    public async Task RejectsMissingPricesTinyDepositAndInvalidStayInputsWithoutPartialData()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var max = await new SettingsProvider(c.Factory).GetBookingMaxNightsAsync(CancellationToken.None);
        var today = DateOnly.FromDateTime((await new DbClock(c.Factory).GetUtcNowAsync(CancellationToken.None)).AddHours(7));
        CreateBookingRequest[] invalid = [Request(d) with { Guests = 5 }, Request(d) with { Guests = 0 },
            Request(d) with { CheckOut = M21Data.CheckIn.AddDays(max + 1) }, Request(d) with { CheckIn = today.AddDays(-1), CheckOut = today },
            Request(d) with { CheckOut = M21Data.CheckIn }];
        foreach (var r in invalid) await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.CreateAsync(r));
        await c.Login.ExecuteAsync("UPDATE dbo.RoomTypePrices SET EffectiveFrom='2099-02-01' WHERE RoomTypeId=@id AND DayType='Holiday';", d.RoomTypeId);
        await Assert.ThrowsAsync<MissingRoomPriceException>(() => c.Bookings.CreateAsync(Request(d)));
        await c.Login.ExecuteAsync("UPDATE dbo.RoomTypePrices SET EffectiveFrom='2098-01-01',Price=1 WHERE RoomTypeId=@id;", d.RoomTypeId);
        Assert.Contains("Tiền cọc sau làm tròn bằng 0", (await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.CreateAsync(Request(d)))).Message);
        Assert.Equal(0, await Count(c, "SELECT COUNT(*) FROM dbo.Bookings WHERE RoomTypeId=@id;", d.RoomTypeId));
    }

    [SqlFact]
    public async Task ExplicitRoomMustBeFreeAndBelongToChosenTypeAndAccountantCannotCreate()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var otherType = await M21Data.CreateAsync(c);
        await c.Bookings.CreateAsync(Request(d) with { RoomId = d.Rooms[0] });
        Assert.Equal(BookingCreationRules.SoldOutMessage, (await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.CreateAsync(Request(d) with { RoomId = d.Rooms[0] }))).Message);
        await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.CreateAsync(Request(d) with { RoomId = otherType.Rooms[0] }));
        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant);
        await Assert.ThrowsAsync<ForbiddenException>(() => accountant.Bookings.CreateAsync(Request(d)));
    }

    [SqlFact]
    public async Task LedgerConstraintRollsBackQuickCustomerCounterHeaderNightsExpiryHistoryAndAudit()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c, 2);
        var expired = await d.BookingAsync(c, 0, expiredHold: true);
        // Deliberately stale terminal ledger: read availability ignores it but the PK remains the final guard.
        await d.BookingAsync(c, 1, BookingStatus.Expired, checkIn: M21Data.CheckIn.AddDays(1));
        var phone = PhoneNumber.Normalize(M14TestClient.Phone());
        var counterBefore = await c.ScalarAsync<int>("SELECT COALESCE(SUM(LastValue),0) FROM dbo.Counters WHERE CounterKey LIKE 'FJ%';");
        var error = await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.CreateAsync(Request(d) with
            { RoomId = d.Rooms[1], CustomerId = null, QuickCustomer = new() { FullName = "Rollback", Phone = phone } }));
        Assert.Equal("PK_RoomNights", error.ConstraintName); Assert.Equal(ConstraintMessages.GetMessage("PK_RoomNights"), error.Message);
        Assert.Null(await c.Customers.FindByPhoneAsync(phone));
        Assert.Equal(3, await Count(c, "SELECT COUNT(*) FROM dbo.RoomNights rn JOIN dbo.Rooms r ON r.Id=rn.RoomId WHERE r.RoomTypeId=@id;", d.RoomTypeId));
        Assert.Equal(3, await Count(c, "SELECT COUNT(*) FROM dbo.BookingNights n JOIN dbo.Bookings b ON b.Id=n.BookingId WHERE b.RoomTypeId=@id;", d.RoomTypeId));
        Assert.Equal(counterBefore, await c.ScalarAsync<int>("SELECT COALESCE(SUM(LastValue),0) FROM dbo.Counters WHERE CounterKey LIKE 'FJ%';"));
        Assert.Equal(2, await Count(c, "SELECT COUNT(*) FROM dbo.Bookings WHERE RoomTypeId=@id;", d.RoomTypeId));
        Assert.Equal(2, await Count(c, "SELECT COUNT(*) FROM dbo.BookingNights WHERE BookingId=@id;", expired));
        Assert.Equal(2, await Count(c, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", expired));
        Assert.Equal(BookingStatus.AwaitingPayment, (await c.Bookings.GetAsync(expired)).Booking.Status);
        Assert.Equal(1, await Count(c, "SELECT COUNT(*) FROM dbo.BookingStatusHistories WHERE BookingId=@id;", expired));
        Assert.Equal(0, await Count(c, "SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Bookings' AND RecordId=CONVERT(varchar(50),@id) AND ActionCode='Booking.Expire';", expired));
    }

    [SqlFact]
    public async Task CreateUsesRealSessionValidatorAndRollsBackOnReplacedSession()
    {
        var c = new M21TestClient(); var identity = await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var other = new M21TestClient(); await other.Login.LoginAsync(identity);
        await Assert.ThrowsAsync<SessionExpiredException>(() => c.Bookings.CreateAsync(Request(d)));
        Assert.Equal(0, await Count(c, "SELECT COUNT(*) FROM dbo.Bookings WHERE RoomTypeId=@id;", d.RoomTypeId));
    }

    [SqlFact]
    public async Task JobsExpiresMultipleTypesOnlyOnceAndSkipsContendedJobsLock()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var a = await M21Data.CreateAsync(c); var b = await M21Data.CreateAsync(c);
        var expiredA = await a.BookingAsync(c, 0, expiredHold: true);
        var expiredB = await b.BookingAsync(c, 0, expiredHold: true);
        var protectedId = await b.BookingAsync(c, 1, expiredHold: true); await b.PendingDepositAsync(c, protectedId);
        var liveId = await b.BookingAsync(c, 2);
        var submitted = await b.BookingAsync(c, 3, BookingStatus.PaymentSubmitted, expiredHold: true); await b.PendingDepositAsync(c, submitted);
        var job = Jobs(c);
        var lockDb = await UnitOfWork.BeginAsync(c.Factory, CancellationToken.None);
        await using (lockDb.ConfigureAwait(false))
        {
            await lockDb.AcquireAppLockAsync("Jobs", 0, CancellationToken.None);
            Assert.Equal(0, await job.RunOnceAsync());
            Assert.Equal(BookingStatus.AwaitingPayment, (await c.Bookings.GetAsync(expiredA)).Booking.Status);
        }
        // The job has no dependency on a current user session.
        c.Current.Clear();
        var results = await Task.WhenAll(job.RunOnceAsync(), Jobs(c).RunOnceAsync());
        Assert.Single(results, n => n > 0); Assert.True(results.Sum() >= 2); // May also expire seed/earlier fixture holds.
        Assert.Equal(0, await job.RunOnceAsync());
        foreach (var id in new[] { expiredA, expiredB })
        {
            Assert.Equal(0, await Count(c, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", id));
            Assert.Equal(1, await Count(c, "SELECT COUNT(*) FROM dbo.BookingStatusHistories WHERE BookingId=@id AND ToStatus='Expired' AND ActorType='System' AND ActorUserId IS NULL;", id));
            Assert.Equal(1, await Count(c, "SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Bookings' AND RecordId=CONVERT(varchar(50),@id) AND ActionCode='Booking.Expire' AND UserId IS NULL;", id));
        }
        foreach (var id in new[] { protectedId, liveId, submitted })
            Assert.Equal(2, await Count(c, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", id));
    }

    [SqlFact]
    public async Task PaymentTransferredAtCheckAndSeedDetailPreserveUtcSeparatelyFromRecordedAt()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var booking = await c.Bookings.CreateAsync(Request(d));
        var db = await UnitOfWork.BeginAsync(c.Factory, CancellationToken.None);
        await using (db.ConfigureAwait(false))
        {
            using var command = db.CreateCommand("""
                INSERT dbo.Payments(VoucherNo,BookingId,Direction,Type,Method,Amount,TransferredAt,Status,RecordedByUserId,RecordedAt)
                VALUES(@voucher,@booking,'Out','Refund','Cash',1,SYSUTCDATETIME(),'Pending',@user,SYSUTCDATETIME());
                """);
            command.Parameters.AddVarChar("@voucher", 20, "T" + Guid.NewGuid().ToString("N")[..19]);
            command.Parameters.AddInt32("@booking", booking.Booking.Id); command.Parameters.AddInt32("@user", c.Current.RequireSession().UserId);
            var error = await Assert.ThrowsAsync<DbConstraintViolationException>(() => db.ExecuteAsync(command, CancellationToken.None));
            Assert.Equal("CK_Payments_TransferredAt", error.ConstraintName);
        }
        Assert.Equal(0, await c.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Payments WHERE Note=N'Phiếu mẫu, không có giao dịch tiền thật' AND Direction='In' AND (TransferredAt IS NULL OR (Method='Cash' AND TransferredAt<>RecordedAt) OR (Method='BankTransfer' AND TransferredAt>=RecordedAt));"));
        var seedBooking = await c.ScalarAsync<int>("SELECT TOP(1) BookingId FROM dbo.Payments WHERE Note=N'Phiếu mẫu, không có giao dịch tiền thật' AND Direction='In' AND Method='BankTransfer' ORDER BY Id;");
        var detail = await c.Bookings.GetAsync(seedBooking);
        var incoming = detail.Payments.Where(p => p.Type == PaymentType.Deposit).ToArray(); Assert.NotEmpty(incoming);
        Assert.All(incoming, p => { Assert.NotNull(p.TransferredAtUtc); Assert.Equal(DateTimeKind.Utc, p.TransferredAtUtc!.Value.Kind); Assert.True(p.TransferredAtUtc <= p.RecordedAtUtc); });
        Assert.Equal(0, await c.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Payments WHERE Direction='Out' AND TransferredAt IS NOT NULL;"));
    }
}
