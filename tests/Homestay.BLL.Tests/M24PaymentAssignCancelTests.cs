using System.Globalization;
using System.Security.Cryptography;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Jobs;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Availability;
using Homestay.DTO.Bookings;
using Homestay.DTO.Enums;
using Homestay.DTO.Payments;

namespace Homestay.BLL.Tests;

[Collection("M23 SQL")]
public sealed class M24PaymentAssignCancelTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private static CreateBookingRequest CreateRequest(M21Data d) => new()
    {
        CustomerId = d.CustomerId, RoomTypeId = d.RoomTypeId, CheckIn = M21Data.CheckIn, CheckOut = M21Data.CheckOut,
        Guests = 2, ContactName = "Khách M24", ContactPhone = "0901234567", SourceChannel = SourceChannel.Phone
    };
    private static Task<int> Count(M21TestClient c, string sql, int id) => c.ScalarAsync<int>(sql, cmd => cmd.Parameters.AddInt32("@id", id));
    private static Task<DateTime> Now(M21TestClient c) => new DbClock(c.Factory).GetUtcNowAsync(CancellationToken.None);
    private static async Task<RecordReceiptRequest> Receipt(M21TestClient c, int id)
    {
        var detail = await c.Bookings.GetAsync(id);
        var transferred = (await Now(c)).AddMinutes(-1);
        transferred = new DateTime(transferred.Ticks / TimeSpan.TicksPerMillisecond * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
        return new() { BookingId = id, BookingCode = detail.Booking.Code, RowVer = detail.Booking.RowVer,
            Amount = detail.AmountDue, Method = PaymentMethod.BankTransfer, TransferredAtUtc = transferred,
            PayerName = "Khách M24", PayerAccount = "1234567890123456", TransferContent = detail.Booking.Code };
    }
    private static JobRunner Jobs(M21TestClient c)
    {
        var repo = new HoldExpiryRepository();
        return new(c.Factory, new DbClock(c.Factory), repo, new HoldExpiry(repo, new AuditWriter(), c.Login.Machine));
    }
    private static async Task<(T? Value, BusinessException? Error)> Try<T>(Func<Task<T>> operation)
    {
        try { return (await operation(), null); }
        catch (BusinessException error) { return (default, error); }
    }
    private static async Task<(T? Value, BusinessException? Error)[]> Race<T>(params Func<Task<T>>[] operations)
    {
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = operations.Select(async operation => { await gate.Task; return await Try(operation); }).ToArray();
        gate.SetResult(); return await Task.WhenAll(tasks);
    }

    [SqlFact]
    public async Task BankReceiptEvidenceAuditAllAccountantsAndJobKeepPendingBooking()
    {
        var c = new M21TestClient(); var actor = await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var accountant1 = await c.Login.CreateIdentityAsync(Position.Accountant);
        var accountant2 = await c.Login.CreateIdentityAsync(Position.Accountant);
        var locked = await c.Login.CreateIdentityAsync(Position.Accountant);
        var resigned = await c.Login.CreateIdentityAsync(Position.Accountant);
        await c.Login.ExecuteAsync("UPDATE dbo.Users SET Status='Locked',LockReason=N'Test' WHERE Id=@id;", locked.UserId);
        await c.Login.ExecuteAsync("UPDATE dbo.Employees SET EmploymentStatus='Resigned' WHERE Id=@id;", resigned.EmployeeId);
        var booking = await c.Bookings.CreateAsync(CreateRequest(d));
        var request = (await Receipt(c, booking.Booking.Id)) with { EvidenceBytes = Png, EvidenceFileName = "renamed.jpg", Amount = booking.AmountDue - 1 };
        var result = await c.Payments.RecordReceiptAsync(request);
        Assert.StartsWith("PT", result.VoucherNo); Assert.Equal(BookingStatus.PaymentSubmitted, result.Status);
        Assert.True(result.IsInsufficient); Assert.Contains("thiếu so với số cần chuyển", result.Warning!);
        Assert.Equal(PaymentStatus.Pending, result.Receipt.Status); Assert.Equal(PaymentType.Deposit, result.Receipt.Type);
        Assert.Equal(request.TransferredAtUtc, result.Receipt.TransferredAtUtc); Assert.Equal(actor.UserId, result.Receipt.RecordedByUserId);
        Assert.Equal(DateTimeKind.Utc, result.Receipt.TransferredAtUtc!.Value.Kind);
        Assert.Null(result.Booking.Booking.DeadlineAtUtc); Assert.DoesNotContain(BookingAction.RecordDeposit, result.Booking.AllowedActions);
        Assert.NotEqual(booking.Booking.RowVer, result.Booking.Booking.RowVer);
        var stored = await c.Login.ReadAsync("SELECT * FROM dbo.StoredFiles WHERE Id=(SELECT EvidenceFileId FROM dbo.Payments WHERE Id=@id);",
            r => (Category: r.GetString("Category"), Name: r.GetString("FileName"), Mime: r.GetString("ContentType"),
                Sha: r.GetFieldValue<byte[]>(r.GetOrdinal("Sha256")), Content: r.GetFieldValue<byte[]>(r.GetOrdinal("Content")),
                Size: r.GetInt32("SizeBytes"), User: r.GetInt32("UploadedByUserId")), result.Receipt.Id);
        Assert.Equal("Evidence", stored.Category); Assert.Equal("renamed.jpg", stored.Name); Assert.Equal("image/png", stored.Mime);
        Assert.Equal(SHA256.HashData(Png), stored.Sha); Assert.Equal(Png, stored.Content); Assert.Equal(Png.Length, stored.Size); Assert.Equal(actor.UserId, stored.User);
        Assert.Equal(Png, (await c.Files.GetEvidenceAsync(booking.Booking.Id, result.Receipt.EvidenceFileId!.Value)).Content);
        Assert.Equal(await c.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Employees e JOIN dbo.Users u ON u.Id=e.UserId WHERE e.Position='Accountant' AND e.EmploymentStatus='Active' AND u.Status='Active';"),
            await Count(c, "SELECT COUNT(*) FROM dbo.Notifications WHERE RefType='Payment' AND RefId=CONVERT(varchar(30),@id);", result.Receipt.Id));
        foreach (var identity in new[] { accountant1, accountant2 })
            Assert.Equal(1, await c.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Notifications WHERE UserId=@user AND RefType='Payment' AND RefId=@ref AND Message=@message AND IsRead=0;", cmd =>
            {
                cmd.Parameters.AddInt32("@user", identity.UserId); cmd.Parameters.AddVarChar("@ref", 30, result.Receipt.Id.ToString(CultureInfo.InvariantCulture));
                cmd.Parameters.AddNVarChar("@message", 500, $"Phiếu thu {result.VoucherNo} của đơn {booking.Booking.Code} chờ xác minh");
            }));
        foreach (var identity in new[] { locked, resigned }) Assert.Equal(0, await c.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.Notifications WHERE UserId=@user AND RefType='Payment' AND RefId=@ref;", cmd =>
            { cmd.Parameters.AddInt32("@user", identity.UserId); cmd.Parameters.AddVarChar("@ref", 30, result.Receipt.Id.ToString(CultureInfo.InvariantCulture)); }));
        var history = Assert.Single(result.Booking.History, h => h.ToStatus == BookingStatus.PaymentSubmitted);
        Assert.Equal(BookingStatus.AwaitingPayment, history.FromStatus); Assert.False(history.OnBehalfOfOwner); Assert.Equal(ActorType.Employee, history.ActorType);
        var audit = await c.Login.ReadAsync("SELECT NewValues FROM dbo.AuditLogs WHERE ActionCode='Payment.Record' AND TableName='Payments' AND RecordId=CONVERT(varchar(50),@id);", r => r.GetString("NewValues"), result.Receipt.Id);
        Assert.DoesNotContain(request.PayerAccount!, audit); Assert.Contains("••••3456", audit);
        Assert.Equal(1, await Count(c, "SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='Bookings' AND RecordId=CONVERT(varchar(50),@id) AND ActionCode='Booking.ChangeStatus';", booking.Booking.Id));
        await c.Login.ExecuteAsync("UPDATE dbo.Bookings SET HoldExpiresAt=DATEADD(minute,-30,SYSUTCDATETIME()) WHERE Id=@id;", booking.Booking.Id);
        await Jobs(c).RunOnceAsync();
        Assert.Equal(BookingStatus.PaymentSubmitted, (await c.Bookings.GetAsync(booking.Booking.Id)).Booking.Status);
        Assert.Equal(2, await Count(c, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", booking.Booking.Id));
    }

    [SqlFact]
    public async Task CashDefaultsToServerTimeAndTransferRequiresPlausibleUtcTime()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var id = await d.BookingAsync(c, 0); var request = await Receipt(c, id);
        await Assert.ThrowsAsync<BusinessException>(() => c.Payments.RecordReceiptAsync(request with { TransferredAtUtc = default }));
        await Assert.ThrowsAsync<BusinessException>(() => c.Payments.RecordReceiptAsync(request with { TransferredAtUtc = request.TransferredAtUtc.AddHours(1) }));
        await Assert.ThrowsAsync<BusinessException>(() => c.Payments.RecordReceiptAsync(request with { TransferredAtUtc = DateTime.SpecifyKind(request.TransferredAtUtc, DateTimeKind.Local) }));
        Assert.Equal(0, await Count(c, "SELECT COUNT(*) FROM dbo.Payments WHERE BookingId=@id;", id));
        var before = await Now(c);
        var result = await c.Payments.RecordReceiptAsync(request with { Method = PaymentMethod.Cash, TransferredAtUtc = default });
        var after = await Now(c);
        Assert.InRange(result.Receipt.TransferredAtUtc!.Value, before, after);
        Assert.Equal(result.Receipt.RecordedAtUtc, result.Receipt.TransferredAtUtc); Assert.Null(result.Receipt.EvidenceFileId);
        Assert.False(result.IsInsufficient); Assert.Null(result.Warning);
    }

    [SqlFact]
    public async Task ExpiredReceiptFailsWithoutAnyPartialWrites()
    {
        var c = new M21TestClient(); var actor = await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var id = await d.BookingAsync(c, 0, expiredHold: true); var request = await Receipt(c, id);
        var before = await c.ScalarAsync<int>("SELECT COALESCE(SUM(LastValue),0) FROM dbo.Counters;");
        var ex = await Assert.ThrowsAsync<BusinessException>(() => c.Payments.RecordReceiptAsync(request with { EvidenceBytes = Png, EvidenceFileName = "a.png" }));
        Assert.Equal(ReceiptRules.ExpiredMessage, ex.Message);
        Assert.Equal(before, await c.ScalarAsync<int>("SELECT COALESCE(SUM(LastValue),0) FROM dbo.Counters;"));
        Assert.Equal(0, await Count(c, "SELECT COUNT(*) FROM dbo.Payments WHERE BookingId=@id;", id));
        Assert.Equal(0, await Count(c, "SELECT COUNT(*) FROM dbo.StoredFiles WHERE UploadedByUserId=@id;", actor.UserId));
        Assert.Equal(1, await Count(c, "SELECT COUNT(*) FROM dbo.BookingStatusHistories WHERE BookingId=@id;", id));
        Assert.Equal(BookingStatus.AwaitingPayment, (await c.Bookings.GetAsync(id)).Booking.Status);
    }

    [SqlFact]
    public async Task TwoRecordersProduceExactlyOneReceiptAndFreshDuplicateUsesConstraintMessage()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var other = new M21TestClient(); await other.LoginNewAsync(Position.Staff, d.AreaId, false);
        var id = await d.BookingAsync(c, 0); var request = (await Receipt(c, id)) with { EvidenceBytes = Png, EvidenceFileName = "race.png" };
        var results = await Race(() => c.Payments.RecordReceiptAsync(request), () => other.Payments.RecordReceiptAsync(request));
        var winner = Assert.Single(results, r => r.Value is not null).Value!;
        Assert.IsType<ConcurrencyException>(Assert.Single(results, r => r.Error is not null).Error);
        Assert.Equal(1, await Count(c, "SELECT COUNT(*) FROM dbo.Payments WHERE BookingId=@id;", id));
        Assert.Equal(1, await Count(c, "SELECT COUNT(*) FROM dbo.BookingStatusHistories WHERE BookingId=@id AND ToStatus='PaymentSubmitted';", id));
        Assert.Equal(1, await Count(c, "SELECT COUNT(*) FROM dbo.StoredFiles WHERE Id=(SELECT EvidenceFileId FROM dbo.Payments WHERE BookingId=@id);", id));
        var fresh = await Receipt(c, id);
        var duplicate = await Assert.ThrowsAsync<BusinessException>(() => c.Payments.RecordReceiptAsync(fresh));
        Assert.Equal("UX_Payments_OneDeposit", duplicate.ConstraintName);
        Assert.Equal(ConstraintMessages.GetMessage("UX_Payments_OneDeposit"), duplicate.Message);
        Assert.Equal(winner.Receipt.Id, Assert.Single((await c.Bookings.GetAsync(id)).Payments).Id);
    }

    [SqlFact]
    public async Task ForgedEmptyAndOversizedFilesNeverCreatePaymentsOrFiles()
    {
        var c = new M21TestClient(); var actor = await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var id = await d.BookingAsync(c, 0); var request = await Receipt(c, id);
        foreach (var bytes in new[] { "MZ123"u8.ToArray(), "plain text"u8.ToArray(), Array.Empty<byte>(), new byte[ReceiptRules.MaxEvidenceBytes + 1] })
            await Assert.ThrowsAsync<BusinessException>(() => c.Payments.RecordReceiptAsync(request with { EvidenceBytes = bytes, EvidenceFileName = "fake.jpg" }));
        Assert.Equal(0, await Count(c, "SELECT COUNT(*) FROM dbo.Payments WHERE BookingId=@id;", id));
        Assert.Equal(0, await Count(c, "SELECT COUNT(*) FROM dbo.StoredFiles WHERE UploadedByUserId=@id;", actor.UserId));
        Assert.Equal(request.RowVer, (await c.Bookings.GetAsync(id)).Booking.RowVer);
    }

    [SqlFact]
    public async Task OutsideScopeIsNotFoundForAllWritesButAccountantCanRecordOnly()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var id = await d.BookingAsync(c, 0, assigned: false); var request = await Receipt(c, id);
        var staff = new M21TestClient(); await staff.LoginNewAsync(Position.Staff, allAreas: false);
        Assert.Empty((await staff.Bookings.GetAsync(id)).AllowedActions);
        await Assert.ThrowsAsync<NotFoundException>(() => staff.Payments.RecordReceiptAsync(request));
        await Assert.ThrowsAsync<NotFoundException>(() => staff.Bookings.ClaimAsync(new(id, request.RowVer)));
        await Assert.ThrowsAsync<NotFoundException>(() => staff.Bookings.ReassignAsync(new(id, c.Current.RequireSession().EmployeeId, request.RowVer)));
        await Assert.ThrowsAsync<NotFoundException>(() => staff.Bookings.CancelUnpaidAsync(new(id, "Khách hủy", request.RowVer)));
        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant, allAreas: false);
        Assert.Equal(new[] { BookingAction.RecordDeposit }, (await accountant.Bookings.GetAsync(id)).AllowedActions);
        await Assert.ThrowsAsync<ForbiddenException>(() => accountant.Bookings.ClaimAsync(new(id, request.RowVer)));
        await Assert.ThrowsAsync<ForbiddenException>(() => accountant.Bookings.ReassignAsync(new(id, c.Current.RequireSession().EmployeeId, request.RowVer)));
        await Assert.ThrowsAsync<ForbiddenException>(() => accountant.Bookings.CancelUnpaidAsync(new(id, "Khách hủy", request.RowVer)));
        var result = await accountant.Payments.RecordReceiptAsync(request);
        Assert.Equal(BookingStatus.PaymentSubmitted, result.Status); Assert.Equal(accountant.Current.RequireSession().UserId, result.Receipt.RecordedByUserId);
        Assert.Empty(result.Booking.AllowedActions);
    }

    [SqlFact]
    public async Task ConcurrentClaimHasOneWinnerAndAssignedOrTerminalOrdersCannotBeClaimed()
    {
        var setup = new M21TestClient(); await setup.LoginNewAsync(); var d = await M21Data.CreateAsync(setup);
        var a = new M21TestClient(); await a.LoginNewAsync(Position.Staff, d.AreaId, false);
        var b = new M21TestClient(); await b.LoginNewAsync(Position.Staff, d.AreaId, false);
        var id = await d.BookingAsync(setup, 0, assigned: false); var before = await setup.Bookings.GetAsync(id);
        var results = await Race(() => a.Bookings.ClaimAsync(new(id, before.Booking.RowVer)), () => b.Bookings.ClaimAsync(new(id, before.Booking.RowVer)));
        var winner = Assert.Single(results, r => r.Value is not null).Value!;
        var loser = Assert.Single(results, r => r.Error is not null).Error!;
        Assert.IsType<ConcurrencyException>(loser); Assert.Equal("Đơn vừa được người khác nhận phụ trách. Đã tải lại.", loser.Message);
        Assert.NotNull(winner.Booking.AssignedEmployeeId); Assert.Equal(before.Booking.CreatedByEmployeeId, winner.Booking.CreatedByEmployeeId);
        Assert.Equal(1, await Count(setup, "SELECT COUNT(*) FROM dbo.AuditLogs WHERE ActionCode='Booking.Claim' AND TableName='Bookings' AND RecordId=CONVERT(varchar(50),@id);", id));
        await Assert.ThrowsAsync<ConcurrencyException>(() => setup.Bookings.ClaimAsync(new(id, winner.Booking.RowVer)));
        var terminal = await d.BookingAsync(setup, 1, BookingStatus.Expired, assigned: false);
        var terminalVersion = (await a.Bookings.GetAsync(terminal)).Booking.RowVer;
        await Assert.ThrowsAsync<BusinessException>(() => a.Bookings.ClaimAsync(new(terminal, terminalVersion)));
    }

    [SqlFact]
    public async Task StaffReassignOnlyOwnAndRecipientMustBeActiveOperatorWithActiveAccount()
    {
        var setup = new M21TestClient(); await setup.LoginNewAsync(); var d = await M21Data.CreateAsync(setup);
        var staff = new M21TestClient(); var actor = await staff.LoginNewAsync(Position.Staff, d.AreaId, false);
        var target = await setup.Login.CreateIdentityAsync(Position.Staff);
        var accountant = await setup.Login.CreateIdentityAsync(Position.Accountant);
        var locked = await setup.Login.CreateIdentityAsync(Position.Staff);
        var suspended = await setup.Login.CreateIdentityAsync(Position.Manager);
        await setup.Login.ExecuteAsync("UPDATE dbo.Users SET Status='Locked',LockReason=N'Test' WHERE Id=@id;", locked.UserId);
        await setup.Login.ExecuteAsync("UPDATE dbo.Employees SET EmploymentStatus='Suspended' WHERE Id=@id;", suspended.EmployeeId);
        var id = await d.BookingAsync(setup, 0); var initial = await staff.Bookings.GetAsync(id);
        await Assert.ThrowsAsync<ForbiddenException>(() => staff.Bookings.ReassignAsync(new(id, target.EmployeeId, initial.Booking.RowVer)));
        var assigned = await setup.Bookings.ReassignAsync(new(id, actor.EmployeeId, initial.Booking.RowVer));
        foreach (var bad in new[] { accountant.EmployeeId, locked.EmployeeId, suspended.EmployeeId, actor.EmployeeId, int.MaxValue })
            await Assert.ThrowsAsync<BusinessException>(() => staff.Bookings.ReassignAsync(new(id, bad, assigned.Booking.RowVer)));
        var result = await staff.Bookings.ReassignAsync(new(id, target.EmployeeId, assigned.Booking.RowVer));
        Assert.Equal(target.EmployeeId, result.Booking.AssignedEmployeeId);
        Assert.Equal(initial.Booking.CreatedByEmployeeId, result.Booking.CreatedByEmployeeId);
        Assert.Equal(1, await setup.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Notifications WHERE UserId=@user AND RefType='Booking' AND RefId=@ref AND IsRead=0;", cmd =>
        { cmd.Parameters.AddInt32("@user", target.UserId); cmd.Parameters.AddVarChar("@ref", 30, id.ToString(CultureInfo.InvariantCulture)); }));
        await Assert.ThrowsAsync<ForbiddenException>(() => staff.Bookings.ReassignAsync(new(id, actor.EmployeeId, result.Booking.RowVer)));
    }

    [SqlFact]
    public async Task ManagerReassignRespectsScopeAndCanAssignToAnotherManager()
    {
        var setup = new M21TestClient(); await setup.LoginNewAsync(); var d = await M21Data.CreateAsync(setup);
        var id = await d.BookingAsync(setup, 0); var original = await setup.Bookings.GetAsync(id);
        var manager = new M21TestClient(); var target = await manager.LoginNewAsync(Position.Manager, allAreas: false);
        await Assert.ThrowsAsync<NotFoundException>(() => manager.Bookings.ReassignAsync(new(id, target.EmployeeId, original.Booking.RowVer)));
        var result = await setup.Bookings.ReassignAsync(new(id, target.EmployeeId, original.Booking.RowVer));
        Assert.Equal(target.EmployeeId, result.Booking.AssignedEmployeeId); Assert.Equal(original.Booking.CreatedByEmployeeId, result.Booking.CreatedByEmployeeId);
        // Being assigned grants scope even without an area.
        Assert.Contains(BookingAction.Reassign, (await manager.Bookings.GetAsync(id)).AllowedActions);
    }

    [SqlFact]
    public async Task CancelUnpaidReleasesRoomPreservesPriceHistoryAndCreatesNoOutgoingPayment()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c, 1);
        var initial = await c.Bookings.CreateAsync(CreateRequest(d)); var id = initial.Booking.Id;
        var search = new AvailabilitySearchRequest { AreaId = d.AreaId, CheckIn = M21Data.CheckIn, CheckOut = M21Data.CheckOut, Guests = 2 };
        Assert.DoesNotContain(await c.Availability.SearchAsync(search), r => r.RoomTypeId == d.RoomTypeId && r.AvailableRooms > 0);
        var result = await c.Bookings.CancelUnpaidAsync(new(id, "  Khách đổi kế hoạch  ", initial.Booking.RowVer));
        Assert.Equal(BookingStatus.Cancelled, result.Booking.Status); Assert.Equal(PaymentState.NoFinancialActivity, result.Booking.PaymentState);
        Assert.Equal(0, await Count(c, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", id));
        Assert.Equal(2, await Count(c, "SELECT COUNT(*) FROM dbo.BookingNights WHERE BookingId=@id;", id));
        Assert.Equal(0, await Count(c, "SELECT COUNT(*) FROM dbo.Payments WHERE BookingId=@id;", id));
        Assert.Equal(1, await Count(c, "SELECT COUNT(*) FROM dbo.Bookings WHERE Id=@id AND CancelledAt IS NOT NULL AND CancelReason=N'Khách đổi kế hoạch' AND RefundAmount IS NULL;", id));
        Assert.Equal(1, Assert.Single(await c.Availability.SearchAsync(search), r => r.RoomTypeId == d.RoomTypeId).AvailableRooms);
        Assert.Equal(BookingStatus.AwaitingPayment, Assert.Single(result.History, h => h.ToStatus == BookingStatus.Cancelled).FromStatus);
        Assert.Contains(result.Audit, a => a.ActionCode == "Booking.CancelUnpaid"); Assert.Empty(result.AllowedActions);
    }

    [SqlFact]
    public async Task CancellationRequiresReasonAwaitingStateAndNoPendingOrVerifiedDeposit()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c, 5);
        var id = await d.BookingAsync(c, 0); var detail = await c.Bookings.GetAsync(id);
        foreach (var reason in new[] { "", "  ", new string('a', 501) })
            await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.CancelUnpaidAsync(new(id, reason, detail.Booking.RowVer)));
        var pendingId = await d.BookingAsync(c, 1); await d.PendingDepositAsync(c, pendingId);
        var pending = await c.Bookings.GetAsync(pendingId);
        const string message = "Đơn đã có phiếu thu chờ xác minh. Vui lòng chờ kế toán xử lý trước khi hủy.";
        Assert.Equal(message, (await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.CancelUnpaidAsync(new(pendingId, "Khách hủy", pending.Booking.RowVer)))).Message);
        var verifier = await c.Login.CreateIdentityAsync(Position.Accountant);
        await c.MutateAsync("UPDATE dbo.Payments SET Status='Verified',VerifiedByUserId=@user,VerifiedAt=SYSUTCDATETIME() WHERE BookingId=@id;", cmd =>
        { cmd.Parameters.AddInt32("@user", verifier.UserId); cmd.Parameters.AddInt32("@id", pendingId); });
        Assert.Equal(message, (await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.CancelUnpaidAsync(new(pendingId, "Khách hủy", pending.Booking.RowVer)))).Message);
        var submittedId = await d.BookingAsync(c, 2); var receipt = await c.Payments.RecordReceiptAsync(await Receipt(c, submittedId));
        Assert.Equal(message, (await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.CancelUnpaidAsync(new(submittedId, "Khách hủy", receipt.Booking.Booking.RowVer)))).Message);
        var deposited = await d.BookingAsync(c, 3, BookingStatus.Deposited);
        var depositedVersion = (await c.Bookings.GetAsync(deposited)).Booking.RowVer;
        await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.CancelUnpaidAsync(new(deposited, "Khách hủy", depositedVersion)));
        var rejectedId = await d.BookingAsync(c, 4); await d.PendingDepositAsync(c, rejectedId);
        await c.Login.ExecuteAsync("UPDATE dbo.Payments SET Status='Rejected',RejectReason=N'Sai phiếu' WHERE BookingId=@id;", rejectedId);
        var cancelled = await c.Bookings.CancelUnpaidAsync(new(rejectedId, "Khách hủy", (await c.Bookings.GetAsync(rejectedId)).Booking.RowVer));
        Assert.Equal(BookingStatus.Cancelled, cancelled.Booking.Status);
        Assert.Equal(2, await Count(c, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", pendingId));
    }

    [SqlFact]
    public async Task StaleRowVersionIsConcurrencyForAllFourWritesWithoutSideEffects()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var target = await c.Login.CreateIdentityAsync(Position.Staff);
        var id = await d.BookingAsync(c, 0, assigned: false); var request = await Receipt(c, id);
        await c.Login.ExecuteAsync("UPDATE dbo.Bookings SET UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;", id);
        await Assert.ThrowsAsync<ConcurrencyException>(() => c.Payments.RecordReceiptAsync(request));
        await Assert.ThrowsAsync<ConcurrencyException>(() => c.Bookings.ClaimAsync(new(id, request.RowVer)));
        await Assert.ThrowsAsync<ConcurrencyException>(() => c.Bookings.ReassignAsync(new(id, target.EmployeeId, request.RowVer)));
        await Assert.ThrowsAsync<ConcurrencyException>(() => c.Bookings.CancelUnpaidAsync(new(id, "Khách hủy", request.RowVer)));
        var detail = await c.Bookings.GetAsync(id); Assert.Null(detail.Booking.AssignedEmployeeId); Assert.Equal(BookingStatus.AwaitingPayment, detail.Booking.Status);
        Assert.Empty(detail.Payments); Assert.Single(detail.History); Assert.Single(detail.Audit);
    }

    [SqlFact]
    public async Task ReplacedRealSessionCannotPerformAnyM24Write()
    {
        var c = new M21TestClient(); var identity = await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var id = await d.BookingAsync(c, 0, assigned: false); var request = await Receipt(c, id);
        var target = await c.Login.CreateIdentityAsync(Position.Staff);
        var replacement = new M21TestClient(); await replacement.Login.LoginAsync(identity);
        await Assert.ThrowsAsync<SessionExpiredException>(() => c.Payments.RecordReceiptAsync(request));
        await Assert.ThrowsAsync<SessionExpiredException>(() => c.Bookings.ClaimAsync(new(id, request.RowVer)));
        await Assert.ThrowsAsync<SessionExpiredException>(() => c.Bookings.ReassignAsync(new(id, target.EmployeeId, request.RowVer)));
        await Assert.ThrowsAsync<SessionExpiredException>(() => c.Bookings.CancelUnpaidAsync(new(id, "Khách hủy", request.RowVer)));
    }

    [SqlFact]
    public async Task DetailActionsFollowStateRoleScopeOwnershipAndPaymentStatus()
    {
        var setup = new M21TestClient(); var creator = await setup.LoginNewAsync(); var d = await M21Data.CreateAsync(setup, 10);
        var states = Enum.GetValues<BookingStatus>(); var ids = new Dictionary<BookingStatus, int>();
        for (var i = 0; i < states.Length; i++) ids[states[i]] = await d.BookingAsync(setup, i, states[i], assigned: false);
        var staff = new M21TestClient(); var employee = await staff.LoginNewAsync(Position.Staff, d.AreaId, false);
        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant, allAreas: false);
        foreach (var (state, id) in ids)
        {
            var expected = BookingStateMachine.IsTerminal(state) ? Array.Empty<BookingAction>() : state == BookingStatus.AwaitingPayment
                ? new[] { BookingAction.Claim, BookingAction.Reassign, BookingAction.RecordDeposit, BookingAction.CancelUnpaid }
                : new[] { BookingAction.Claim, BookingAction.Reassign };
            Assert.Equal(expected, (await setup.Bookings.GetAsync(id)).AllowedActions);
            Assert.Equal(expected.Where(a => a != BookingAction.Reassign), (await staff.Bookings.GetAsync(id)).AllowedActions);
            Assert.Equal(state == BookingStatus.AwaitingPayment ? new[] { BookingAction.RecordDeposit } : Array.Empty<BookingAction>(),
                (await accountant.Bookings.GetAsync(id)).AllowedActions);
        }
        var awaiting = ids[BookingStatus.AwaitingPayment];
        var claimed = await staff.Bookings.ClaimAsync(new(awaiting, (await staff.Bookings.GetAsync(awaiting)).Booking.RowVer));
        Assert.Equal(new[] { BookingAction.Reassign, BookingAction.RecordDeposit, BookingAction.CancelUnpaid }, claimed.AllowedActions);
        await setup.Login.ExecuteAsync("UPDATE dbo.Bookings SET HoldExpiresAt=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE Id=@id;", awaiting);
        Assert.Equal(new[] { BookingAction.Reassign, BookingAction.CancelUnpaid }, (await staff.Bookings.GetAsync(awaiting)).AllowedActions);
        await d.PendingDepositAsync(setup, awaiting);
        Assert.Equal(new[] { BookingAction.Reassign }, (await staff.Bookings.GetAsync(awaiting)).AllowedActions);
        var outside = new M21TestClient(); await outside.LoginNewAsync(Position.Staff, allAreas: false);
        foreach (var id in ids.Values) Assert.Empty((await outside.Bookings.GetAsync(id)).AllowedActions);
        // Creator and assigned employee remain in scope without area assignments.
        var creatorClient = new M21TestClient(); await setup.Login.ExecuteAsync("UPDATE dbo.Employees SET HasAllAreas=0 WHERE Id=@id;", creator.EmployeeId);
        await creatorClient.Login.LoginAsync(creator);
        Assert.Contains(BookingAction.Reassign, (await creatorClient.Bookings.GetAsync(awaiting)).AllowedActions);
        await staff.MutateAsync("DELETE dbo.EmployeeAreas WHERE EmployeeId=@id;", cmd => cmd.Parameters.AddInt32("@id", employee.EmployeeId));
        await staff.Login.LoginAsync(employee); Assert.Contains(BookingAction.Reassign, (await staff.Bookings.GetAsync(awaiting)).AllowedActions);
    }

    [SqlFact]
    public async Task FourthOrderStillBlockedAfterRealReceiptAndExpiredHold()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c, 4);
        var first = await c.Bookings.CreateAsync(CreateRequest(d));
        await c.Payments.RecordReceiptAsync(await Receipt(c, first.Booking.Id));
        await c.Login.ExecuteAsync("UPDATE dbo.Bookings SET HoldExpiresAt=DATEADD(minute,-30,SYSUTCDATETIME()) WHERE Id=@id;", first.Booking.Id);
        await c.Bookings.CreateAsync(CreateRequest(d)); await c.Bookings.CreateAsync(CreateRequest(d));
        var ex = await Assert.ThrowsAsync<BusinessException>(() => c.Bookings.CreateAsync(CreateRequest(d)));
        Assert.Contains("Khách đang có 3 đơn", ex.Message);
        Assert.Equal(3, await Count(c, "SELECT COUNT(*) FROM dbo.Bookings WHERE CustomerId=@id;", d.CustomerId));
        Assert.Equal(BookingStatus.PaymentSubmitted, (await c.Bookings.GetAsync(first.Booking.Id)).Booking.Status);
    }

    [SqlFact]
    public async Task ReceiptAndCancellationRacingCannotLeaveCancelledBookingWithPendingMoney()
    {
        var c = new M21TestClient(); await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var other = new M21TestClient(); await other.LoginNewAsync(Position.Staff, d.AreaId, false);
        var id = await d.BookingAsync(c, 0); var request = await Receipt(c, id);
        var results = await Race(async () => (await c.Payments.RecordReceiptAsync(request)).Booking,
            () => other.Bookings.CancelUnpaidAsync(new(id, "Khách hủy", request.RowVer)));
        Assert.Single(results, r => r.Value is not null);
        Assert.IsType<ConcurrencyException>(Assert.Single(results, r => r.Error is not null).Error);
        var final = await c.Bookings.GetAsync(id);
        Assert.Contains(final.Booking.Status, new[] { BookingStatus.Cancelled, BookingStatus.PaymentSubmitted });
        var submitted = final.Booking.Status == BookingStatus.PaymentSubmitted;
        Assert.Equal(submitted ? 1 : 0, final.Payments.Count);
        Assert.Equal(submitted ? 2 : 0, await Count(c, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", id));
        Assert.Equal(2, final.History.Count);
    }

    [SqlFact]
    public async Task FailureAfterEvidenceInsertRollsBackFileReceiptCounterAndState()
    {
        var c = new M21TestClient(); var actor = await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c);
        var first = await d.BookingAsync(c, 0); var existing = await c.Payments.RecordReceiptAsync(await Receipt(c, first));
        var second = await d.BookingAsync(c, 1); var request = (await Receipt(c, second)) with { EvidenceBytes = Png, EvidenceFileName = "rollback.png" };
        var key = existing.VoucherNo.Split('-')[0];
        var value = await c.ScalarAsync<int>("SELECT LastValue FROM dbo.Counters WHERE CounterKey=@key;", cmd => cmd.Parameters.AddVarChar("@key", 30, key));
        try
        {
            // Deliberate voucher collision happens after StoredFiles INSERT, without changing database DDL or privileges.
            await c.MutateAsync("UPDATE dbo.Counters SET LastValue=@value WHERE CounterKey=@key;", cmd =>
            { cmd.Parameters.AddInt32("@value", int.Parse(existing.VoucherNo.Split('-')[1], CultureInfo.InvariantCulture) - 1); cmd.Parameters.AddVarChar("@key", 30, key); });
            var ex = await Assert.ThrowsAsync<BusinessException>(() => c.Payments.RecordReceiptAsync(request));
            Assert.Equal("UQ_Payments_VoucherNo", ex.ConstraintName);
            Assert.Equal(0, await Count(c, "SELECT COUNT(*) FROM dbo.StoredFiles WHERE UploadedByUserId=@id;", actor.UserId));
            var detail = await c.Bookings.GetAsync(second); Assert.Empty(detail.Payments); Assert.Single(detail.History);
            Assert.Equal(BookingStatus.AwaitingPayment, detail.Booking.Status); Assert.Equal(request.RowVer, detail.Booking.RowVer);
            Assert.Equal(int.Parse(existing.VoucherNo.Split('-')[1], CultureInfo.InvariantCulture) - 1,
                await c.ScalarAsync<int>("SELECT LastValue FROM dbo.Counters WHERE CounterKey=@key;", cmd => cmd.Parameters.AddVarChar("@key", 30, key)));
        }
        finally
        {
            await c.MutateAsync("UPDATE dbo.Counters SET LastValue=@value WHERE CounterKey=@key;", cmd =>
            { cmd.Parameters.AddInt32("@value", value); cmd.Parameters.AddVarChar("@key", 30, key); });
        }
    }
}
