using System.Globalization;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DTO.Availability;
using Homestay.DTO.Bookings;
using Homestay.DTO.Enums;
using Homestay.DTO.Payments;

namespace Homestay.BLL.Tests;

[Collection("M23 SQL")]
public sealed class M33ALifecycleTests
{
    private static Task<int> Count(M21TestClient c, string sql, int id) => c.ScalarAsync<int>(sql, cmd => cmd.Parameters.AddInt32("@id", id));
    private static Task<DateTime> Now(M21TestClient c) => c.ScalarAsync<DateTime>("SELECT SYSUTCDATETIME();");
    private static async Task<DateOnly> Today(M21TestClient c) => DateOnly.FromDateTime((await Now(c)).AddHours(7));
    private static async Task<M21TestClient> Client(Position role = Position.Manager, int? area = null, bool all = true)
    {
        var c = new M21TestClient(); await c.LoginNewAsync(role, area, all); return c;
    }
    private static async Task<RecordReceiptResult> Record(M21TestClient c, int id, decimal? amount = null)
    {
        var b = await c.Bookings.GetAsync(id);
        return await c.Payments.RecordReceiptAsync(new() { BookingId = id, RowVer = b.Booking.RowVer,
            Amount = amount ?? b.AmountDue, Method = PaymentMethod.Cash });
    }
    private static async Task<BookingDetail> Verify(M21TestClient accountant, int id)
    {
        var r = await accountant.Payments.GetReceiptForVerificationAsync(id);
        return await accountant.Payments.VerifyReceiptAsync(new(id, r.Receipt.RowVer, r.Booking.Booking.RowVer));
    }
    private static async Task<BookingDetail> Deposit(M21TestClient c, M21Data d, int index = 0,
        DateOnly? checkIn = null, DateOnly? checkOut = null)
    {
        var id = await d.BookingAsync(c, index, checkIn: checkIn, checkOut: checkOut);
        var receipt = await Record(c, id);
        await Verify(await Client(Position.Accountant), receipt.Receipt.Id);
        return await c.Bookings.GetAsync(id);
    }
    private static async Task<(T? Value, BusinessException? Error)[]> Race<T>(params Func<Task<T>>[] actions) where T : class
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = actions.Select(async action =>
        {
            await start.Task;
            try { return (Value: (T?)await action(), Error: (BusinessException?)null); }
            catch (BusinessException ex) { return (Value: (T?)null, Error: ex); }
        }).ToArray();
        start.SetResult(); return await Task.WhenAll(tasks);
    }
    private static CreateBookingRequest Request(M21Data d, DateOnly from, DateOnly to, int? room = null) => new()
    {
        CustomerId = d.CustomerId, RoomTypeId = d.RoomTypeId, RoomId = room, CheckIn = from, CheckOut = to,
        Guests = 2, ContactName = "Khách M33A", ContactPhone = "0901234567", SourceChannel = SourceChannel.Phone
    };
    private static async Task AssertLedger(M21TestClient c, int id, int count) =>
        Assert.Equal(count, await Count(c, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", id));
    private static async Task AssertStamp(M21TestClient c, int id, string column, BookingHistoryItem history)
    {
        // Only test-owned column identifiers are supplied here.
        var time = await c.Login.ReadAsync($"SELECT {column} AS Stamp FROM dbo.Bookings WHERE Id=@id;", r => r.GetUtcDateTime("Stamp"), id);
        Assert.Equal(history.ChangedAtUtc, time);
    }

    [SqlFact]
    public async Task FullPublicFlowCreatesRecordsVerifiesConfirmsChecksInAndCompletesEarly()
    {
        var setup = await Client(); var d = await M21Data.CreateAsync(setup);
        var today = await Today(setup);
        await setup.Login.ExecuteAsync("UPDATE dbo.Contracts SET StartDate='2000-01-01' WHERE HomestayId=@id;", d.HomestayId);
        await setup.Login.ExecuteAsync("UPDATE dbo.RoomTypePrices SET EffectiveFrom='2000-01-01' WHERE RoomTypeId=@id;", d.RoomTypeId);
        var staff = await Client(Position.Staff, d.AreaId, false); var kt = await Client(Position.Accountant);
        var created = await staff.Bookings.CreateAsync(Request(d, today, today.AddDays(3)));
        Assert.Equal(BookingStatus.AwaitingPayment, created.Booking.Status);
        Assert.Contains(BookingAction.RecordDeposit, created.AllowedActions);
        var recorded = await Record(staff, created.Booking.Id);
        Assert.Equal(BookingStatus.PaymentSubmitted, recorded.Status);
        await AssertStamp(staff, created.Booking.Id, "PaymentSubmittedAt", recorded.Booking.History.Last());
        var verified = await Verify(kt, recorded.Receipt.Id);
        Assert.Equal(BookingStatus.Deposited, verified.Booking.Status);
        var deposit = verified.History.Last(); await AssertStamp(staff, created.Booking.Id, "DepositedAt", deposit);
        var confirmMinutes = await setup.ScalarAsync<int>("SELECT CONVERT(int,SettingValue) FROM dbo.SystemSettings WHERE SettingKey='Booking.ConfirmMinutes';");
        Assert.Equal(deposit.ChangedAtUtc.AddMinutes(confirmMinutes), verified.ConfirmDeadlineAtUtc);
        Assert.Equal(kt.Current.RequireSession().UserId, Assert.Single(verified.Payments).VerifiedByUserId);
        Assert.Equal(deposit.ChangedAtUtc, Assert.Single(verified.Payments).VerifiedAtUtc);
        Assert.Equal(1, await Count(staff, "SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Payments' AND ActionCode='Payment.Verify' AND RecordId=CONVERT(varchar(50),@id);", recorded.Receipt.Id));
        Assert.Equal(1, await staff.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Notifications WHERE UserId=@user AND RefType='Booking' AND RefId=@ref AND Message=@message;", cmd =>
        {
            cmd.Parameters.AddInt32("@user", staff.Current.RequireSession().UserId);
            cmd.Parameters.AddVarChar("@ref", 30, created.Booking.Id.ToString(CultureInfo.InvariantCulture));
            cmd.Parameters.AddNVarChar("@message", 500, $"Đơn {created.Booking.Code} đã xác minh cọc, gọi chủ xác nhận");
        }));
        var ready = await staff.Bookings.GetAsync(created.Booking.Id);
        Assert.Contains(BookingAction.ConfirmOnBehalf, ready.AllowedActions);
        var confirmed = await staff.Bookings.ConfirmOnBehalfAsync(new(ready.Booking.Id, "  Chủ đồng ý  ", ready.Booking.RowVer));
        Assert.Equal(BookingStatus.Confirmed, confirmed.Booking.Status); Assert.Contains(BookingAction.CheckIn, confirmed.AllowedActions);
        await AssertStamp(staff, ready.Booking.Id, "ConfirmedAt", confirmed.History.Last());
        var arrived = await staff.Bookings.CheckInAsync(new(ready.Booking.Id, "  Chủ báo đã nhận phòng  ", confirmed.Booking.RowVer));
        Assert.Equal(BookingStatus.CheckedIn, arrived.Booking.Status); Assert.Contains(BookingAction.CheckOut, arrived.AllowedActions);
        await AssertStamp(staff, ready.Booking.Id, "CheckedInAt", arrived.History.Last());
        var completed = await staff.Bookings.CheckOutAsync(new(ready.Booking.Id, "  Khách trả sớm  ", arrived.Booking.RowVer));
        Assert.Equal(BookingStatus.Completed, completed.Booking.Status); Assert.Empty(completed.AllowedActions);
        await AssertStamp(staff, ready.Booking.Id, "CompletedAt", completed.History.Last());
        Assert.Equal(6, completed.History.Count);
        Assert.All(completed.History.Skip(3), h => { Assert.True(h.OnBehalfOfOwner); Assert.Equal(h.Note!.Trim(), h.Note); });
        Assert.All(completed.History.Take(3), h => Assert.False(h.OnBehalfOfOwner));
        Assert.Equal(created.TotalAmount, completed.TotalAmount); Assert.Equal(created.AmountDue, completed.AmountDue);
        await AssertLedger(staff, ready.Booking.Id, 3);
        foreach (var action in new[] { "Booking.Create", "Booking.ChangeStatus", "Booking.ConfirmOnBehalf", "Booking.CheckIn", "Booking.CheckOut" })
            Assert.Contains(completed.Audit, a => a.ActionCode == action);
    }

    [SqlFact]
    public async Task SelfVerificationAndUnderpaymentFailButSelfRejectionIsAllowed()
    {
        var setup = await Client(); var d = await M21Data.CreateAsync(setup); var kt = await Client(Position.Accountant);
        var id = await d.BookingAsync(setup, 0); var receipt = await Record(kt, id);
        var r = await kt.Payments.GetReceiptForVerificationAsync(receipt.Receipt.Id);
        var ex = await Assert.ThrowsAsync<ForbiddenException>(() => kt.Payments.VerifyReceiptAsync(new(r.Receipt.Id, r.Receipt.RowVer, r.Booking.Booking.RowVer)));
        Assert.Contains("không được tự xác minh", ex.Message);
        var rejected = await kt.Payments.RejectReceiptAsync(new(r.Receipt.Id, "  Sai chứng từ  ", r.Receipt.RowVer, r.Booking.Booking.RowVer));
        Assert.Equal(BookingStatus.AwaitingPayment, rejected.Booking.Status);
        var underpaid = await Record(setup, id, rejected.AmountDue - 1);
        var other = await Client(Position.Accountant); r = await other.Payments.GetReceiptForVerificationAsync(underpaid.Receipt.Id);
        var insufficient = await Assert.ThrowsAsync<BusinessException>(() => other.Payments.VerifyReceiptAsync(new(r.Receipt.Id, r.Receipt.RowVer, r.Booking.Booking.RowVer)));
        Assert.Contains("thiếu tiền, không xác minh được", insufficient.Message);
        Assert.Equal(PaymentStatus.Pending, (await other.Payments.GetReceiptForVerificationAsync(r.Receipt.Id)).Receipt.Status);
        Assert.Equal(BookingStatus.PaymentSubmitted, (await setup.Bookings.GetAsync(id)).Booking.Status);
    }

    [SqlFact]
    public async Task RejectionsExtendHoldThenExpireAtConfiguredLimitAndAllowNewDeposit()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c, 1); var kt = await Client(Position.Accountant);
        var max = await c.ScalarAsync<int>("SELECT CONVERT(int,SettingValue) FROM dbo.SystemSettings WHERE SettingKey='Booking.MaxReceiptRejections';");
        var minutes = await c.ScalarAsync<int>("SELECT CONVERT(int,SettingValue) FROM dbo.SystemSettings WHERE SettingKey='Booking.HoldMinutes';");
        Assert.True(max >= 2); // fresh reference seed has two attempts
        var id = await d.BookingAsync(c, 0);
        for (var n = 1; n <= max; n++)
        {
            var receipt = await Record(c, id); var r = await kt.Payments.GetReceiptForVerificationAsync(receipt.Receipt.Id);
            var before = await Now(c);
            var result = await kt.Payments.RejectReceiptAsync(new(r.Receipt.Id, "  Chứng từ không hợp lệ  ", r.Receipt.RowVer, r.Booking.Booking.RowVer));
            var after = await Now(c);
            Assert.Equal(n, await Count(c, "SELECT CONVERT(int,ReceiptRejectCount) FROM dbo.Bookings WHERE Id=@id;", id));
            Assert.Equal("Chứng từ không hợp lệ", (await kt.Payments.GetReceiptForVerificationAsync(r.Receipt.Id)).Receipt.RejectReason);
            Assert.Equal(n, await Count(c, "SELECT COUNT(*) FROM dbo.Payments WHERE BookingId=@id AND Type='Deposit' AND Status='Rejected';", id));
            Assert.Equal(n < max ? BookingStatus.AwaitingPayment : BookingStatus.Expired, result.Booking.Status);
            await AssertLedger(c, id, n < max ? 2 : 0);
            var fresh = await c.Bookings.GetAsync(id);
            if (n < max)
            {
                Assert.InRange(result.HoldExpiresAtUtc, before.AddMinutes(minutes), after.AddMinutes(minutes));
                Assert.Contains(BookingAction.RecordDeposit, fresh.AllowedActions);
            }
            else Assert.Empty(fresh.AllowedActions);
        }
        Assert.Equal(1, Assert.Single(await c.Availability.SearchAsync(new() { CheckIn = M21Data.CheckIn, CheckOut = M21Data.CheckOut }), x => x.RoomTypeId == d.RoomTypeId).AvailableRooms);
        Assert.Equal(max, await Count(c, "SELECT COUNT(*) FROM dbo.AuditLogs WHERE ActionCode='Payment.Reject' AND TableName='Payments' AND RecordId IN (SELECT CONVERT(varchar(50),Id) FROM dbo.Payments WHERE BookingId=@id);", id));
    }

    [SqlFact]
    public async Task ConcurrentStaffAndManagerConfirmationHasExactlyOneWinnerRepeatedly()
    {
        var manager = await Client(); var d = await M21Data.CreateAsync(manager);
        var staff = await Client(Position.Staff, d.AreaId, false);
        for (var i = 0; i < 5; i++)
        {
            var from = M21Data.CheckIn.AddDays(i * 3); var b = await Deposit(staff, d, checkIn: from, checkOut: from.AddDays(2));
            var results = await Race(() => staff.Bookings.ConfirmOnBehalfAsync(new(b.Booking.Id, "Chủ đồng ý", b.Booking.RowVer)),
                () => manager.Bookings.ConfirmOnBehalfAsync(new(b.Booking.Id, "Chủ xác nhận", b.Booking.RowVer)));
            Assert.Single(results, x => x.Value is not null); Assert.IsType<ConcurrencyException>(Assert.Single(results, x => x.Error is not null).Error);
            Assert.Equal(1, await Count(manager, "SELECT COUNT(*) FROM dbo.BookingStatusHistories WHERE BookingId=@id AND ToStatus='Confirmed' AND OnBehalfOfOwner=1;", b.Booking.Id));
            Assert.Equal(1, await Count(manager, "SELECT COUNT(*) FROM dbo.AuditLogs WHERE RecordId=CONVERT(varchar(50),@id) AND TableName='Bookings' AND ActionCode='Booking.ConfirmOnBehalf';", b.Booking.Id));
        }
    }

    [SqlFact]
    public async Task TwoAccountantsVerifyExactlyOnce()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var a = await Client(Position.Accountant); var b = await Client(Position.Accountant);
        for (var i = 0; i < 3; i++)
        {
            var id = await d.BookingAsync(c, i); var receipt = await Record(c, id);
            var r = await a.Payments.GetReceiptForVerificationAsync(receipt.Receipt.Id);
            var request = new VerifyReceiptRequest(r.Receipt.Id, r.Receipt.RowVer, r.Booking.Booking.RowVer);
            var results = await Race(() => a.Payments.VerifyReceiptAsync(request), () => b.Payments.VerifyReceiptAsync(request));
            Assert.Single(results, x => x.Value is not null); Assert.IsType<ConcurrencyException>(Assert.Single(results, x => x.Error is not null).Error);
            Assert.Equal(1, await Count(c, "SELECT COUNT(*) FROM dbo.BookingStatusHistories WHERE BookingId=@id AND ToStatus='Deposited';", id));
            Assert.Equal(1, await Count(c, "SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Payments' AND ActionCode='Payment.Verify' AND RecordId=CONVERT(varchar(50),@id);", r.Receipt.Id));
        }
    }

    [SqlFact]
    public async Task ConfirmationCanBeOverdueAndMovesLedgerAndReleasesExpiredTargetInPlace()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c, 2); var b = await Deposit(c, d);
        var expired = await d.BookingAsync(c, 1, expiredHold: true);
        await c.Login.ExecuteAsync("UPDATE dbo.Bookings SET ConfirmDeadlineAt=DATEADD(minute,-60,SYSUTCDATETIME()) WHERE Id=@id;", b.Booking.Id);
        b = await c.Bookings.GetAsync(b.Booking.Id);
        Assert.Equal(d.Rooms[1], Assert.Single(await c.Bookings.ListConfirmationRoomsAsync(b.Booking.Id)).Id);
        var result = await c.Bookings.ConfirmOnBehalfAsync(new(b.Booking.Id, "Chủ đổi phòng", b.Booking.RowVer, d.Rooms[1]));
        Assert.Equal(d.Rooms[1], result.Booking.RoomId);
        Assert.Equal(2, await Count(c, "SELECT COUNT(*) FROM dbo.RoomNights rn JOIN dbo.Bookings b ON b.Id=rn.BookingId WHERE b.Id=@id AND rn.RoomId=b.RoomId;", b.Booking.Id));
        Assert.Equal(0, await Count(c, "SELECT COUNT(*) FROM dbo.RoomNights WHERE RoomId=@id;", d.Rooms[0]));
        Assert.Equal(BookingStatus.Expired, (await c.Bookings.GetAsync(expired)).Booking.Status);
        Assert.Equal(d.Rooms[0], Assert.Single((await c.Availability.QuoteAsync(new(d.RoomTypeId, M21Data.CheckIn, M21Data.CheckOut, 2))).AvailableRooms).RoomId);
        var audit = Assert.Single(result.Audit, a => a.ActionCode == "Booking.ConfirmOnBehalf");
        Assert.Contains($"\"RoomId\":{d.Rooms[0]}", audit.OldValues); Assert.Contains($"\"RoomId\":{d.Rooms[1]}", audit.NewValues);
    }

    [SqlFact]
    public async Task InvalidReplacementRoomsLeaveEverythingUnchanged()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var otherType = await M21Data.CreateAsync(c);
        var b = await Deposit(c, d); await d.BlockAsync(c, 1, RoomBlockReason.Maintenance);
        await c.Login.ExecuteAsync("UPDATE dbo.Rooms SET IsActive=0 WHERE Id=@id;", d.Rooms[2]);
        var expired = await d.BookingAsync(c, 3, expiredHold: true); await d.PendingDepositAsync(c, expired);
        foreach (var room in new[] { d.Rooms[0], d.Rooms[1], d.Rooms[2], d.Rooms[3], otherType.Rooms[0], int.MaxValue })
        {
            await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.ConfirmOnBehalfAsync(new(b.Booking.Id, "Đổi phòng", b.Booking.RowVer, room)));
            var unchanged = await c.Bookings.GetAsync(b.Booking.Id);
            Assert.Equal(b.Booking.RowVer, unchanged.Booking.RowVer); Assert.Equal(b.Booking.RoomId, unchanged.Booking.RoomId);
            Assert.Equal(b.History.Count, unchanged.History.Count); await AssertLedger(c, b.Booking.Id, 2);
        }
    }

    [SqlFact]
    public async Task RoomSwapRacesWithCreateWithoutDuplicateOrPartialLedger()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c, 2); var b = await Deposit(c, d);
        var creator = await Client(Position.Staff, d.AreaId, false);
        var results = await Race(() => c.Bookings.ConfirmOnBehalfAsync(new(b.Booking.Id, "Chủ đổi phòng", b.Booking.RowVer, d.Rooms[1])),
            () => creator.Bookings.CreateAsync(Request(d, M21Data.CheckIn, M21Data.CheckOut, d.Rooms[1])));
        Assert.Single(results, x => x.Value is not null); Assert.Single(results, x => x.Error is not null);
        var fresh = await c.Bookings.GetAsync(b.Booking.Id);
        Assert.Equal(results[0].Value is not null ? BookingStatus.Confirmed : BookingStatus.Deposited, fresh.Booking.Status);
        Assert.Equal(results[0].Value is not null ? d.Rooms[1] : d.Rooms[0], fresh.Booking.RoomId);
        Assert.Equal(2, await Count(c, "SELECT COUNT(*) FROM dbo.RoomNights rn JOIN dbo.Bookings b ON b.Id=rn.BookingId WHERE b.Id=@id AND b.RoomId=rn.RoomId;", b.Booking.Id));
        Assert.Equal(0, await Count(c, "SELECT COUNT(*) FROM (SELECT rn.RoomId,rn.[Date] FROM dbo.RoomNights rn JOIN dbo.Rooms r ON r.Id=rn.RoomId WHERE r.RoomTypeId=@id GROUP BY rn.RoomId,rn.[Date] HAVING COUNT(*)>1) x;", d.RoomTypeId));
    }

    [SqlFact]
    public async Task OwnerRejectionCreatesOnePendingRefundAndNotifiesEveryActiveAccountant()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c, 1); var b = await Deposit(c, d); var kt = await Client(Position.Accountant);
        var result = await c.Bookings.RejectOnBehalfAsync(new(b.Booking.Id, "  Chủ không nhận khách  ", b.Booking.RowVer));
        Assert.Equal(BookingStatus.Rejected, result.Booking.Status); Assert.Empty(result.AllowedActions);
        var refund = Assert.Single(result.Payments, p => p.Type == PaymentType.Refund);
        Assert.Equal(b.AmountDue, refund.Amount); Assert.Equal(PaymentStatus.Pending, refund.Status);
        Assert.StartsWith("PC", refund.VoucherNo); Assert.Null(refund.TransferredAtUtc); Assert.Null(refund.VerifiedAtUtc);
        Assert.Equal(b.AmountDue, await c.Login.ReadAsync("SELECT RefundAmount FROM dbo.Bookings WHERE Id=@id;", r => r.GetDecimal("RefundAmount"), b.Booking.Id));
        Assert.Equal("Chủ không nhận khách", await c.Login.ReadAsync("SELECT CancelReason FROM dbo.Bookings WHERE Id=@id;", r => r.GetString("CancelReason"), b.Booking.Id));
        Assert.True(result.History.Last().OnBehalfOfOwner); await AssertLedger(c, b.Booking.Id, 0);
        Assert.Equal(1, Assert.Single(await c.Availability.SearchAsync(new() { CheckIn = M21Data.CheckIn, CheckOut = M21Data.CheckOut }), x => x.RoomTypeId == d.RoomTypeId).AvailableRooms);
        Assert.Equal(await c.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Employees e JOIN dbo.Users u ON u.Id=e.UserId WHERE e.Position='Accountant' AND e.EmploymentStatus='Active' AND u.Status='Active';"),
            await Count(c, "SELECT COUNT(*) FROM dbo.Notifications WHERE RefType='Payment' AND RefId=CONVERT(varchar(30),@id);", refund.Id));
        Assert.Contains((await kt.Payments.ListPendingRefundsAsync(new() { BookingId = b.Booking.Id })).Items, p => p.Id == refund.Id);
        await Assert.ThrowsAsync<ConcurrencyException>(() => c.Bookings.RejectOnBehalfAsync(new(b.Booking.Id, "Lặp lại", b.Booking.RowVer)));
        Assert.Equal(1, await Count(c, "SELECT COUNT(*) FROM dbo.Payments WHERE BookingId=@id AND Type='Refund';", b.Booking.Id));
    }

    [SqlFact]
    public async Task CheckInAndNoShowEnforceServerVietnamDatesAndNoShowHasNoRefund()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var today = await Today(c);
        var tomorrow = await d.BookingAsync(c, 0, BookingStatus.Confirmed, checkIn: today.AddDays(1), checkOut: today.AddDays(3));
        var b = await c.Bookings.GetAsync(tomorrow);
        await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.CheckInAsync(new(tomorrow, "Chủ báo", b.Booking.RowVer)));
        var exact = await d.BookingAsync(c, 1, BookingStatus.Confirmed, checkIn: today, checkOut: today.AddDays(2));
        b = await c.Bookings.GetAsync(exact);
        await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.MarkNoShowAsync(new(exact, "Chưa thấy khách", b.Booking.RowVer)));
        var arrived = await c.Bookings.CheckInAsync(new(exact, "Khách đến", b.Booking.RowVer));
        Assert.Equal(BookingStatus.CheckedIn, arrived.Booking.Status);
        var past = await d.BookingAsync(c, 2, BookingStatus.Confirmed, checkIn: today.AddDays(-1), checkOut: today.AddDays(1));
        b = await c.Bookings.GetAsync(past);
        var noShow = await c.Bookings.MarkNoShowAsync(new(past, "  Chủ báo khách không đến  ", b.Booking.RowVer));
        Assert.Equal(BookingStatus.NoShow, noShow.Booking.Status); Assert.Empty(noShow.AllowedActions);
        Assert.True(noShow.History.Last().OnBehalfOfOwner);
        Assert.Equal(0m, await c.Login.ReadAsync("SELECT RefundAmount FROM dbo.Bookings WHERE Id=@id;", r => r.GetDecimal("RefundAmount"), past));
        await AssertLedger(c, past, 0); Assert.Empty(noShow.Payments);
    }

    [SqlFact]
    public async Task AllSevenWritesRejectStaleBookingVersionsBeforeStateValidation()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var kt = await Client(Position.Accountant);
        var id = await d.BookingAsync(c, 0); var record = await Record(c, id); var r = await kt.Payments.GetReceiptForVerificationAsync(record.Receipt.Id);
        await c.Login.ExecuteAsync("UPDATE dbo.Bookings SET UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;", id);
        await Assert.ThrowsAsync<ConcurrencyException>(() => kt.Payments.VerifyReceiptAsync(new(r.Receipt.Id, r.Receipt.RowVer, r.Booking.Booking.RowVer)));
        await Assert.ThrowsAsync<ConcurrencyException>(() => kt.Payments.RejectReceiptAsync(new(r.Receipt.Id, "Sai phiếu", r.Receipt.RowVer, r.Booking.Booking.RowVer)));
        var current = await c.Bookings.GetAsync(id);
        var verified = await kt.Payments.VerifyReceiptAsync(new(r.Receipt.Id, r.Receipt.RowVer, current.Booking.RowVer));
        await c.Login.ExecuteAsync("UPDATE dbo.Bookings SET UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;", id);
        foreach (var call in LifecycleCalls(c, verified)) await Assert.ThrowsAsync<ConcurrencyException>(call);
    }

    private static Func<Task<BookingDetail>>[] LifecycleCalls(M21TestClient c, BookingDetail b, string note = "Chủ báo", int? room = null) =>
    [
        () => c.Bookings.ConfirmOnBehalfAsync(new(b.Booking.Id, note, b.Booking.RowVer, room)),
        () => c.Bookings.RejectOnBehalfAsync(new(b.Booking.Id, note, b.Booking.RowVer)),
        () => c.Bookings.CheckInAsync(new(b.Booking.Id, note, b.Booking.RowVer)),
        () => c.Bookings.CheckOutAsync(new(b.Booking.Id, note, b.Booking.RowVer)),
        () => c.Bookings.MarkNoShowAsync(new(b.Booking.Id, note, b.Booking.RowVer))
    ];

    [SqlFact]
    public async Task BothReceiptWritesRejectStalePaymentVersionAndDoNotWriteBooking()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var kt = await Client(Position.Accountant);
        var id = await d.BookingAsync(c, 0); var record = await Record(c, id); var r = await kt.Payments.GetReceiptForVerificationAsync(record.Receipt.Id);
        await c.Login.ExecuteAsync("UPDATE dbo.Payments SET Note=N'Mới' WHERE Id=@id;", r.Receipt.Id);
        await Assert.ThrowsAsync<ConcurrencyException>(() => kt.Payments.VerifyReceiptAsync(new(r.Receipt.Id, r.Receipt.RowVer, r.Booking.Booking.RowVer)));
        await Assert.ThrowsAsync<ConcurrencyException>(() => kt.Payments.RejectReceiptAsync(new(r.Receipt.Id, "Sai phiếu", r.Receipt.RowVer, r.Booking.Booking.RowVer)));
        Assert.Equal(r.Booking.Booking.RowVer, (await c.Bookings.GetAsync(id)).Booking.RowVer);
        await AssertLedger(c, id, 2);
    }

    [SqlFact]
    public async Task RolesScopeNotesAndInvalidStatesAreEnforcedWithoutPartialWrites()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var b = await Deposit(c, d);
        var kt = await Client(Position.Accountant); var outsider = await Client(Position.Staff, all: false);
        foreach (var call in LifecycleCalls(kt, b)) await Assert.ThrowsAsync<ForbiddenException>(call);
        foreach (var call in LifecycleCalls(outsider, b)) await Assert.ThrowsAsync<NotFoundException>(call);
        await Assert.ThrowsAsync<NotFoundException>(() => outsider.Bookings.ConfirmOnBehalfAsync(new(b.Booking.Id, "Chủ báo", b.Booking.RowVer, d.Rooms[1])));
        foreach (var note in new[] { " ", new string('x', 501) })
            foreach (var call in LifecycleCalls(c, b, note)) await Assert.ThrowsAsync<BusinessException>(call);
        await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.CheckOutAsync(new(b.Booking.Id, "Chủ báo", b.Booking.RowVer)));
        var id = await d.BookingAsync(c, 1); var record = await Record(c, id); var r = await kt.Payments.GetReceiptForVerificationAsync(record.Receipt.Id);
        await Assert.ThrowsAsync<BusinessException>(() => kt.Payments.RejectReceiptAsync(new(r.Receipt.Id, " ", r.Receipt.RowVer, r.Booking.Booking.RowVer)));
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Payments.VerifyReceiptAsync(new(r.Receipt.Id, r.Receipt.RowVer, r.Booking.Booking.RowVer)));
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Payments.RejectReceiptAsync(new(r.Receipt.Id, "Sai phiếu", r.Receipt.RowVer, r.Booking.Booking.RowVer)));
        Assert.Equal(b.Booking.RowVer, (await c.Bookings.GetAsync(b.Booking.Id)).Booking.RowVer);
    }

    [SqlFact]
    public async Task UnassignedVerificationNotifiesOnlyManagersWhoseScopeIncludesBooking()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var kt = await Client(Position.Accountant);
        var inArea = await Client(Position.Manager, d.AreaId, false); var outside = await Client(Position.Manager, all: false);
        var id = await d.BookingAsync(c, 0, assigned: false); var record = await Record(c, id); await Verify(kt, record.Receipt.Id);
        foreach (var (client, expected) in new[] { (c, 1), (inArea, 1), (outside, 0) })
            Assert.Equal(expected, await client.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Notifications WHERE RefType='Booking' AND RefId=@ref AND UserId=@user;", cmd =>
            {
                cmd.Parameters.AddVarChar("@ref", 30, id.ToString(CultureInfo.InvariantCulture));
                cmd.Parameters.AddInt32("@user", client.Current.RequireSession().UserId);
            }));
    }

    [SqlFact]
    public async Task RoomNightPrimaryKeyUsesSharedMessageAndRollsBackInPlaceExpiry()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c, 3); var b = await Deposit(c, d);
        // Deliberately leave a terminal booking's ledger to exercise the final SQL safety net.
        await d.BookingAsync(c, 1, BookingStatus.Expired);
        var expiring = await d.BookingAsync(c, 2, expiredHold: true);
        var old = await c.Bookings.GetAsync(expiring);
        Assert.Contains(await c.Bookings.ListConfirmationRoomsAsync(b.Booking.Id), r => r.Id == d.Rooms[1]);
        var ex = await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.ConfirmOnBehalfAsync(new(b.Booking.Id, "Chủ đổi phòng", b.Booking.RowVer, d.Rooms[1])));
        Assert.Equal("PK_RoomNights", ex.ConstraintName);
        Assert.Equal(ConstraintMessages.GetMessage("PK_RoomNights"), ex.Message);
        var unchanged = await c.Bookings.GetAsync(b.Booking.Id);
        Assert.Equal(b.Booking.RowVer, unchanged.Booking.RowVer); Assert.Equal(b.Booking.RoomId, unchanged.Booking.RoomId);
        Assert.Equal(b.History.Count, unchanged.History.Count); Assert.Equal(b.Audit.Count, unchanged.Audit.Count);
        var stillHeld = await c.Bookings.GetAsync(expiring);
        Assert.Equal(BookingStatus.AwaitingPayment, stillHeld.Booking.Status); Assert.Equal(old.Booking.RowVer, stillHeld.Booking.RowVer);
        Assert.Equal(old.History.Count, stillHeld.History.Count); Assert.Equal(old.Audit.Count, stillHeld.Audit.Count);
        await AssertLedger(c, b.Booking.Id, 2); await AssertLedger(c, expiring, 2);
    }

    [SqlFact]
    public async Task RefundInsertFailureRollsBackStatusLedgerHistoryAuditAndCounter()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var b = await Deposit(c, d);
        var other = await d.BookingAsync(c, 1, BookingStatus.Rejected);
        var now = DateTime.SpecifyKind(await Now(c), DateTimeKind.Utc);
        var key = "PC" + now.AddHours(7).ToString("yyMM", CultureInfo.InvariantCulture);
        var before = await c.ScalarAsync<int>("SELECT COALESCE((SELECT LastValue FROM dbo.Counters WHERE CounterKey=@key),0);",
            cmd => cmd.Parameters.AddVarChar("@key", 30, key));
        var voucher = key + "-" + (before + 1).ToString("D4", CultureInfo.InvariantCulture);
        await c.MutateAsync("""
            INSERT dbo.Payments(VoucherNo,BookingId,Direction,Type,Method,Amount,Status,RecordedByUserId,RecordedAt)
            VALUES(@voucher,@booking,'Out','Refund','BankTransfer',1,'Pending',@user,SYSUTCDATETIME());
            """, cmd =>
        {
            cmd.Parameters.AddVarChar("@voucher", 20, voucher); cmd.Parameters.AddInt32("@booking", other);
            cmd.Parameters.AddInt32("@user", c.Current.RequireSession().UserId);
        });
        try
        {
            var ex = await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.RejectOnBehalfAsync(new(b.Booking.Id, "Chủ từ chối", b.Booking.RowVer)));
            Assert.Equal("UQ_Payments_VoucherNo", ex.ConstraintName);
            var unchanged = await c.Bookings.GetAsync(b.Booking.Id);
            Assert.Equal(b.Booking.RowVer, unchanged.Booking.RowVer); Assert.Equal(BookingStatus.Deposited, unchanged.Booking.Status);
            Assert.Equal(b.History.Count, unchanged.History.Count); Assert.Equal(b.Audit.Count, unchanged.Audit.Count);
            await AssertLedger(c, b.Booking.Id, 2); Assert.Single(unchanged.Payments);
            Assert.Equal(before, await c.ScalarAsync<int>("SELECT COALESCE((SELECT LastValue FROM dbo.Counters WHERE CounterKey=@key),0);",
                cmd => cmd.Parameters.AddVarChar("@key", 30, key)));
        }
        finally
        {
            // Remove the intentional global voucher collision so other tests can allocate normally.
            await c.MutateAsync("UPDATE dbo.Payments SET VoucherNo=@voucher WHERE BookingId=@id;", cmd =>
            {
                cmd.Parameters.AddVarChar("@voucher", 20, "X" + Guid.NewGuid().ToString("N")[..19]);
                cmd.Parameters.AddInt32("@id", other);
            });
        }
    }

    [SqlFact]
    public async Task ReplacedRealSessionsCannotRunAnyOfTheSevenWrites()
    {
        var c = new M21TestClient(); var identity = await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c); var b = await Deposit(c, d);
        var replacement = new M21TestClient(); await replacement.Login.LoginAsync(identity);
        foreach (var call in LifecycleCalls(c, b)) await Assert.ThrowsAsync<SessionExpiredException>(call);
        var setup = await Client(); var kt = new M21TestClient(); var accountant = await kt.LoginNewAsync(Position.Accountant);
        var id = await d.BookingAsync(setup, 1); var receipt = await Record(setup, id); var r = await kt.Payments.GetReceiptForVerificationAsync(receipt.Receipt.Id);
        var replacementKt = new M21TestClient(); await replacementKt.Login.LoginAsync(accountant);
        await Assert.ThrowsAsync<SessionExpiredException>(() => kt.Payments.VerifyReceiptAsync(new(r.Receipt.Id, r.Receipt.RowVer, r.Booking.Booking.RowVer)));
        await Assert.ThrowsAsync<SessionExpiredException>(() => kt.Payments.RejectReceiptAsync(new(r.Receipt.Id, "Sai phiếu", r.Receipt.RowVer, r.Booking.Booking.RowVer)));
    }
}
