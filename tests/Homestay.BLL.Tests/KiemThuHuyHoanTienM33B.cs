using System.Globalization;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DTO.Bookings;
using Homestay.DTO.ChangeRequests;
using Homestay.DTO.Enums;
using Homestay.DTO.Payments;

namespace Homestay.BLL.Tests;

[Collection("M23 SQL")]
public sealed class M33BCancelRefundTests
{
    private static async Task<M21TestClient> Client(Position role = Position.Manager, int? area = null, bool all = true)
    {
        var c = new M21TestClient(); await c.LoginNewAsync(role, area, all); return c;
    }
    private static Task<T> Scalar<T>(M21TestClient c, string sql, int id) =>
        c.ScalarAsync<T>(sql, cmd => cmd.Parameters.AddInt32("@id", id));
    private static async Task<DateTime> Now(M21TestClient c) =>
        DateTime.SpecifyKind(await c.ScalarAsync<DateTime>("SELECT CONVERT(datetime2(3),SYSUTCDATETIME());"), DateTimeKind.Utc);
    private static async Task<DateOnly> Today(M21TestClient c) => DateOnly.FromDateTime((await Now(c)).AddHours(7));
    private static async Task<BookingDetail> Collect(M21TestClient c, int id)
    {
        var b = await c.Bookings.GetAsync(id);
        var recorded = await c.Payments.RecordReceiptAsync(new() { BookingId = id, RowVer = b.Booking.RowVer,
            Amount = b.AmountDue, Method = PaymentMethod.Cash });
        var kt = await Client(Position.Accountant);
        var r = await kt.Payments.GetReceiptForVerificationAsync(recorded.Receipt.Id);
        await kt.Payments.VerifyReceiptAsync(new(r.Receipt.Id, r.Receipt.RowVer, r.Booking.Booking.RowVer));
        return await c.Bookings.GetAsync(id);
    }
    private static async Task<BookingDetail> Deposit(M21TestClient c, M21Data d, int index = 0, DateOnly? date = null)
    {
        var id = await d.BookingAsync(c, index, checkIn: date, checkOut: date?.AddDays(2));
        return await Collect(c, id);
    }
    private static Task<CancelRequestDetail> Submit(M21TestClient c, BookingDetail b) =>
        c.Changes.SubmitCancelAsync(new(b.Booking.Id, "  Khách yêu cầu hủy  ", b.Booking.RowVer));
    private static Task<CancelRequestDetail> Approve(M21TestClient c, CancelRequestDetail r) =>
        c.Changes.ApproveAsync(new(r.Request.Id, r.Request.RowVer, r.Booking.Booking.RowVer, "  Đồng ý hoàn theo báo giá  "));
    private static async Task<PaymentQueueItem> Pending(M21TestClient kt, int id) =>
        Assert.Single((await kt.Payments.ListPendingRefundsAsync(new() { BookingId = id })).Items);
    private static async Task<RecordRefundPaidRequest> PaidRequest(M21TestClient kt, PaymentQueueItem p) => new()
    {
        PaymentId = p.Id, RowVer = p.RowVer, BankRef = "  REFUND-TEST-123  ", PaidAtUtc = await Now(kt)
    };
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
    private static Task<int> Notice(M21TestClient c, int bookingId, string message) => c.ScalarAsync<int>(
        "SELECT COUNT(*) FROM dbo.Notifications WHERE UserId=@user AND RefType='Booking' AND RefId=@ref AND Message=@message;", cmd =>
        {
            cmd.Parameters.AddInt32("@user", c.Current.RequireSession().UserId);
            cmd.Parameters.AddVarChar("@ref", 30, bookingId.ToString(CultureInfo.InvariantCulture));
            cmd.Parameters.AddNVarChar("@message", 500, message);
        });
    private static Task<int> Audit(M21TestClient c, string table, int id, string action) => c.ScalarAsync<int>(
        "SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName=@table AND RecordId=@ref AND ActionCode=@action;", cmd =>
        {
            cmd.Parameters.AddVarChar("@table", 50, table); cmd.Parameters.AddVarChar("@ref", 50, id.ToString(CultureInfo.InvariantCulture));
            cmd.Parameters.AddVarChar("@action", 50, action);
        });

    [SqlFact]
    public async Task FullPublicCancellationFlowPreservesQuoteReleasesRoomAndPaysExactlyOneRefund()
    {
        var setup = await Client(); var d = await M21Data.CreateAsync(setup, 1);
        var staff = await Client(Position.Staff, d.AreaId, false); var manager = await Client(Position.Manager, d.AreaId, false);
        var kt = await Client(Position.Accountant); var kt2 = await Client(Position.Accountant);
        var created = await staff.Bookings.CreateAsync(new() { CustomerId = d.CustomerId, RoomTypeId = d.RoomTypeId,
            CheckIn = M21Data.CheckIn, CheckOut = M21Data.CheckOut, Guests = 2,
            ContactName = "Khách hủy M33B", ContactPhone = "0901234567", SourceChannel = SourceChannel.Phone });
        var deposited = await Collect(staff, created.Booking.Id);
        var b = await staff.Bookings.ConfirmOnBehalfAsync(new(deposited.Booking.Id, "Chủ xác nhận", deposited.Booking.RowVer));
        Assert.Equal(0, Assert.Single(await staff.Availability.SearchAsync(new() { AreaId = d.AreaId,
            CheckIn = M21Data.CheckIn, CheckOut = M21Data.CheckOut })).AvailableRooms);
        var r = await Submit(staff, b);
        var expected = RefundCalculator.Calculate(new(b.Booking.CheckIn, DateOnly.FromDateTime(r.Request.RequestedAtUtc.AddHours(7)),
            b.AmountDue, true, b.CancellationPolicy));
        Assert.Equal(expected.RefundAmount, r.Request.QuotedRefundAmount);
        Assert.Equal(ChangeRequestStatus.Pending, r.Request.Status); Assert.Equal("Khách yêu cầu hủy", r.Request.Reason);
        Assert.Equal(staff.Current.RequireSession().UserId, r.Request.RequestedByUserId);
        Assert.Equal(b.Booking.RowVer, r.Booking.Booking.RowVer); Assert.Equal(b.History.Count, r.Booking.History.Count);
        Assert.Equal(1, await Notice(manager, b.Booking.Id, $"Đơn {b.Booking.Code} có yêu cầu hủy chờ duyệt"));
        var applied = await Approve(manager, r);
        Assert.Equal(ChangeRequestStatus.Applied, applied.Request.Status); Assert.Equal(BookingStatus.Cancelled, applied.Booking.Booking.Status);
        Assert.Equal(manager.Current.RequireSession().UserId, applied.Request.DecidedByUserId);
        Assert.Equal("Đồng ý hoàn theo báo giá", applied.Request.DecisionNote);
        Assert.Equal(r.Request.QuotedRefundAmount, await Scalar<decimal>(setup, "SELECT RefundAmount FROM dbo.Bookings WHERE Id=@id;", b.Booking.Id));
        var stamp = await setup.Login.ReadAsync("SELECT CancelledAt AS Stamp FROM dbo.Bookings WHERE Id=@id;", rd => rd.GetUtcDateTime("Stamp"), b.Booking.Id);
        Assert.Equal(applied.Booking.History.Last().ChangedAtUtc, stamp);
        Assert.Equal(applied.Request.DecidedAtUtc, stamp);
        Assert.Equal("Khách yêu cầu hủy", applied.Booking.History.Last().Note); Assert.False(applied.Booking.History.Last().OnBehalfOfOwner);
        Assert.Equal(0, await Scalar<int>(setup, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", b.Booking.Id));
        Assert.Equal(1, Assert.Single(await staff.Availability.SearchAsync(new() { AreaId = d.AreaId,
            CheckIn = M21Data.CheckIn, CheckOut = M21Data.CheckOut })).AvailableRooms);
        var p = await Pending(kt, b.Booking.Id);
        Assert.Equal(r.Request.QuotedRefundAmount, p.Amount); Assert.Null(p.TransferredAtUtc); Assert.Null(p.PaidAtUtc);
        foreach (var accountant in new[] { kt, kt2 })
            Assert.Equal(1, await accountant.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Notifications WHERE RefType='Payment' AND RefId=@ref AND UserId=@user;", cmd =>
            {
                cmd.Parameters.AddVarChar("@ref", 30, p.Id.ToString(CultureInfo.InvariantCulture));
                cmd.Parameters.AddInt32("@user", accountant.Current.RequireSession().UserId);
            }));
        var paidRequest = await PaidRequest(kt, p);
        var paid = await kt.Payments.RecordRefundPaidAsync(paidRequest);
        Assert.Equal(PaymentStatus.Verified, paid.Status); Assert.Equal("REFUND-TEST-123", paid.BankRef);
        Assert.Equal(kt.Current.RequireSession().UserId, paid.VerifiedByUserId); Assert.Null(paid.TransferredAtUtc);
        Assert.Equal(paidRequest.PaidAtUtc, paid.PaidAtUtc); Assert.Equal(DateTimeKind.Utc, paid.PaidAtUtc!.Value.Kind);
        Assert.NotEqual(p.RowVer, paid.RowVer); Assert.Empty((await kt.Payments.ListPendingRefundsAsync(new() { BookingId = b.Booking.Id })).Items);
        Assert.Equal(1, await Notice(staff, b.Booking.Id, $"Yêu cầu hủy đơn {b.Booking.Code} đã được duyệt"));
        Assert.Equal(1, await Notice(staff, b.Booking.Id, $"Phiếu chi hoàn {p.VoucherNo} đơn {b.Booking.Code} đã chi"));
        Assert.Equal(1, await Audit(setup, "BookingChangeRequests", r.Request.Id, "ChangeRequest.SubmitCancel"));
        Assert.Equal(1, await Audit(setup, "BookingChangeRequests", r.Request.Id, "ChangeRequest.Approve"));
        Assert.Equal(1, await Audit(setup, "Bookings", b.Booking.Id, "Booking.CancelApproved"));
        Assert.Equal(1, await Audit(setup, "Payments", p.Id, "Payment.CreateRefund"));
        Assert.Equal(1, await Audit(setup, "Payments", p.Id, "Payment.RefundPaid"));
        var final = await setup.Bookings.GetAsync(b.Booking.Id);
        Assert.Equal(b.History.Count + 1, final.History.Count); Assert.Equal(b.TotalAmount, final.TotalAmount);
        Assert.Equal(b.Nights.Count, final.Nights.Count); Assert.Single(final.Payments, x => x.Type == PaymentType.Refund);
    }

    [SqlFact]
    public async Task ApprovalUsesSubmittedQuoteEvenWhenCurrentDateBracketAndRequestedAtHaveChanged()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var b = await Deposit(c, d);
        var r = await Submit(c, b); Assert.Equal(b.AmountDue, r.Request.QuotedRefundAmount);
        var today = await Today(c);
        // Simulate the passage to the final refund bracket without changing the SQL server clock.
        await c.MutateAsync("""
            UPDATE dbo.Bookings SET CheckIn=@date,CheckOut=DATEADD(day,2,@date) WHERE Id=@booking;
            UPDATE dbo.BookingChangeRequests SET RequestedAt=DATEADD(day,-10,SYSUTCDATETIME()) WHERE Id=@request;
            """, cmd => { cmd.Parameters.AddDate("@date", today.AddDays(1)); cmd.Parameters.AddInt32("@booking", b.Booking.Id); cmd.Parameters.AddInt32("@request", r.Request.Id); });
        var fresh = await c.Changes.GetAsync(r.Request.Id);
        Assert.Equal(0m, (await c.Bookings.PreviewRefundAsync(b.Booking.Id)).Calculation.RefundAmount);
        var approved = await Approve(await Client(), fresh);
        Assert.Equal(b.AmountDue, approved.Request.QuotedRefundAmount);
        Assert.Equal(b.AmountDue, await Scalar<decimal>(c, "SELECT RefundAmount FROM dbo.Bookings WHERE Id=@id;", b.Booking.Id));
    }

    [SqlFact]
    public async Task ZeroQuoteCancelsWithoutPaymentOrRefundNotification()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c);
        var b = await Deposit(c, d, date: (await Today(c)).AddDays(1)); var r = await Submit(c, b);
        Assert.Equal(0m, r.Request.QuotedRefundAmount);
        await Approve(await Client(), r);
        Assert.Equal(0m, await Scalar<decimal>(c, "SELECT RefundAmount FROM dbo.Bookings WHERE Id=@id;", b.Booking.Id));
        Assert.Equal(0, await Scalar<int>(c, "SELECT COUNT(*) FROM dbo.Payments WHERE BookingId=@id AND Type='Refund';", b.Booking.Id));
        Assert.Equal(0, await Scalar<int>(c, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", b.Booking.Id));
    }

    [SqlFact]
    public async Task SelfReviewAndStaffReviewAreForbiddenRejectionKeepsBookingAndAllowsResubmission()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var b = await Deposit(c, d); var r = await Submit(c, b);
        await Assert.ThrowsAsync<ForbiddenException>(() => Approve(c, r));
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Changes.RejectAsync(new(r.Request.Id, "Không đồng ý", r.Request.RowVer, b.Booking.RowVer)));
        var staff = await Client(Position.Staff, d.AreaId, false);
        await Assert.ThrowsAsync<ForbiddenException>(() => Approve(staff, r));
        await Assert.ThrowsAsync<ForbiddenException>(() => staff.Changes.RejectAsync(new(r.Request.Id, "Không đồng ý", r.Request.RowVer, b.Booking.RowVer)));
        var manager = await Client();
        var rejected = await manager.Changes.RejectAsync(new(r.Request.Id, "  Khách đổi ý  ", r.Request.RowVer, b.Booking.RowVer));
        Assert.Equal(ChangeRequestStatus.Rejected, rejected.Request.Status); Assert.Equal("Khách đổi ý", rejected.Request.DecisionNote);
        Assert.Equal(b.Booking.RowVer, rejected.Booking.Booking.RowVer); Assert.Equal(b.History.Count, rejected.Booking.History.Count);
        Assert.Equal(BookingStatus.Deposited, rejected.Booking.Booking.Status);
        Assert.Equal(1, await Notice(c, b.Booking.Id, $"Yêu cầu hủy đơn {b.Booking.Code} bị từ chối. Vui lòng xem lý do trên yêu cầu."));
        Assert.Equal(1, await Audit(c, "BookingChangeRequests", r.Request.Id, "ChangeRequest.Reject"));
        Assert.Single(rejected.Booking.Payments);
        var next = await Submit(c, rejected.Booking); Assert.NotEqual(r.Request.Id, next.Request.Id);
    }

    [SqlFact]
    public async Task AnyOpenRequestBlocksSubmissionWithExactUniqueMessageIncludingConcurrentSubmissions()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var b = await Deposit(c, d);
        var other = await Client();
        var results = await Race(() => Submit(c, b), () => Submit(other, b));
        Assert.Single(results, x => x.Value is not null);
        var error = Assert.Single(results, x => x.Error is not null).Error!;
        Assert.Equal("UX_BookingChangeRequests_OneOpenRequest", error.ConstraintName);
        Assert.Equal(ConstraintMessages.GetMessage(error.ConstraintName!), error.Message);
        var second = await Deposit(c, d, 1);
        foreach (var awaiting in new[] { false, true })
        {
            if (!awaiting)
                await c.MutateAsync("""
                    INSERT dbo.BookingChangeRequests(BookingId,RequestType,NewCheckIn,NewCheckOut,Reason,Status,RequestedByUserId,RequestedAt)
                    SELECT Id,'ChangeDates',CheckIn,CheckOut,N'Test','Pending',@user,SYSUTCDATETIME() FROM dbo.Bookings WHERE Id=@booking;
                    """, cmd => { cmd.Parameters.AddInt32("@booking", second.Booking.Id); cmd.Parameters.AddInt32("@user", c.Current.RequireSession().UserId); });
            else
                await c.MutateAsync("""
                    UPDATE dbo.BookingChangeRequests SET Status='AwaitingPayment',DecidedByUserId=@user,DecidedAt=SYSUTCDATETIME(),PaymentDeadlineAt=DATEADD(minute,30,SYSUTCDATETIME())
                    WHERE BookingId=@booking;
                    """, cmd => { cmd.Parameters.AddInt32("@booking", second.Booking.Id); cmd.Parameters.AddInt32("@user", other.Current.RequireSession().UserId); });
            var ex = await Assert.ThrowsAsync<BusinessException>(() => Submit(c, second));
            Assert.Equal("UX_BookingChangeRequests_OneOpenRequest", ex.ConstraintName); Assert.Equal(ConstraintMessages.GetMessage(ex.ConstraintName!), ex.Message);
        }
    }

    [SqlFact]
    public async Task InvalidBookingStatesCannotSubmitAndChangedBookingCannotBeApproved()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c);
        foreach (var (state, index) in new[] { (BookingStatus.AwaitingPayment, 0), (BookingStatus.PaymentSubmitted, 1), (BookingStatus.CheckedIn, 2) })
        {
            var id = await d.BookingAsync(c, index, state); var b = await c.Bookings.GetAsync(id);
            await Assert.ThrowsAsync<BusinessException>(() => Submit(c, b));
            Assert.Equal(0, await Scalar<int>(c, "SELECT COUNT(*) FROM dbo.BookingChangeRequests WHERE BookingId=@id;", id));
        }
        var deposited = await Deposit(c, d, 3, await Today(c));
        var confirmed = await c.Bookings.ConfirmOnBehalfAsync(new(deposited.Booking.Id, "Chủ đồng ý", deposited.Booking.RowVer));
        var r = await Submit(c, confirmed); var manager = await Client();
        var checkedIn = await c.Bookings.CheckInAsync(new(confirmed.Booking.Id, "Khách đã tới", confirmed.Booking.RowVer));
        await Assert.ThrowsAsync<ConcurrencyException>(() => Approve(manager, r));
        await Assert.ThrowsAsync<BusinessException>(() => manager.Changes.ApproveAsync(new(r.Request.Id, r.Request.RowVer, checkedIn.Booking.RowVer)));
        var unchanged = await c.Changes.GetAsync(r.Request.Id);
        Assert.Equal(ChangeRequestStatus.Pending, unchanged.Request.Status); Assert.Equal(r.Request.RowVer, unchanged.Request.RowVer);
        Assert.Equal(BookingStatus.CheckedIn, unchanged.Booking.Booking.Status); Assert.Single(unchanged.Booking.Payments);
    }

    [SqlFact]
    public async Task StaleRequestAndBookingVersionsBlockBothReviewsAndSubmission()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var b = await Deposit(c, d); var r = await Submit(c, b);
        var manager = await Client();
        await c.Login.ExecuteAsync("UPDATE dbo.BookingChangeRequests SET UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;", r.Request.Id);
        await Assert.ThrowsAsync<ConcurrencyException>(() => Approve(manager, r));
        await Assert.ThrowsAsync<ConcurrencyException>(() => manager.Changes.RejectAsync(new(r.Request.Id, "Test", r.Request.RowVer, b.Booking.RowVer)));
        var fresh = await c.Changes.GetAsync(r.Request.Id);
        await c.Login.ExecuteAsync("UPDATE dbo.Bookings SET UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;", b.Booking.Id);
        await Assert.ThrowsAsync<ConcurrencyException>(() => Approve(manager, fresh));
        await Assert.ThrowsAsync<ConcurrencyException>(() => manager.Changes.RejectAsync(new(r.Request.Id, "Test", fresh.Request.RowVer, b.Booking.RowVer)));
        await Assert.ThrowsAsync<ConcurrencyException>(() => Submit(c, b));
        Assert.Equal(ChangeRequestStatus.Pending, (await c.Changes.GetAsync(r.Request.Id)).Request.Status);
    }

    [SqlFact]
    public async Task TwoManagersApprovingHaveExactlyOneWinnerAndOneRefund()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var b = await Deposit(c, d); var r = await Submit(c, b);
        var a = await Client(); var z = await Client();
        var results = await Race(() => Approve(a, r), () => Approve(z, r));
        Assert.Single(results, x => x.Value is not null);
        Assert.IsType<ConcurrencyException>(Assert.Single(results, x => x.Error is not null).Error);
        Assert.Equal(1, await Scalar<int>(c, "SELECT COUNT(*) FROM dbo.Payments WHERE BookingId=@id AND Type='Refund';", b.Booking.Id));
        Assert.Equal(1, await Audit(c, "BookingChangeRequests", r.Request.Id, "ChangeRequest.Approve"));
        Assert.Equal(1, await Audit(c, "Bookings", b.Booking.Id, "Booking.CancelApproved"));
        Assert.Equal(b.History.Count + 1, (await c.Bookings.GetAsync(b.Booking.Id)).History.Count);
    }

    [SqlFact]
    public async Task HomestayUnavailableRefundsFullyAndExpiresPendingOrAwaitingPaymentCancellation()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var manager = await Client(); var kt = await Client(Position.Accountant);
        var today = await Today(c);
        for (var i = 0; i < 3; i++)
        {
            var b = await Deposit(c, d, i, today.AddDays(1));
            if (i == 1) b = await c.Bookings.ConfirmOnBehalfAsync(new(b.Booking.Id, "Chủ đồng ý", b.Booking.RowVer));
            var r = i == 2 ? null : await Submit(c, b);
            if (i == 1)
                await c.MutateAsync("""
                    UPDATE dbo.BookingChangeRequests SET Status='AwaitingPayment',DecidedByUserId=@manager,
                        DecidedAt=SYSUTCDATETIME(),PaymentDeadlineAt=DATEADD(minute,30,SYSUTCDATETIME()) WHERE Id=@request;
                    """, cmd => { cmd.Parameters.AddInt32("@manager", manager.Current.RequireSession().UserId); cmd.Parameters.AddInt32("@request", r!.Request.Id); });
            var before = r is null ? null : await c.Changes.GetAsync(r.Request.Id);
            var cancelled = await manager.Bookings.CancelHomestayUnavailableAsync(new(b.Booking.Id, "  Homestay không phục vụ được  ", b.Booking.RowVer));
            Assert.Equal(BookingStatus.Cancelled, cancelled.Booking.Status);
            Assert.Equal(b.AmountDue, await Scalar<decimal>(c, "SELECT RefundAmount FROM dbo.Bookings WHERE Id=@id;", b.Booking.Id));
            var refund = await Pending(kt, b.Booking.Id); Assert.Equal(b.AmountDue, refund.Amount);
            Assert.Equal(0, await Scalar<int>(c, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", b.Booking.Id));
            Assert.Equal(1, await Notice(c, b.Booking.Id, $"Đơn {b.Booking.Code} đã hủy do homestay không phục vụ được"));
            Assert.Equal(1, await Audit(c, "Bookings", b.Booking.Id, "Booking.CancelHomestayUnavailable"));
            if (r is not null)
            {
                var closed = await c.Changes.GetAsync(r.Request.Id);
                Assert.Equal(ChangeRequestStatus.Expired, closed.Request.Status); Assert.NotNull(closed.Request.DecidedByUserId); Assert.NotNull(closed.Request.DecidedAtUtc);
                Assert.Equal("Homestay không phục vụ được", closed.Request.DecisionNote);
                Assert.NotEqual(closed.Request.RequestedByUserId, closed.Request.DecidedByUserId);
                Assert.Equal(1, await Audit(c, "BookingChangeRequests", r.Request.Id, "ChangeRequest.Expire"));
                if (i == 1) Assert.Equal(before!.Request.DecidedAtUtc, closed.Request.DecidedAtUtc);
            }
        }
    }

    [SqlFact]
    public async Task HomestayUnavailableHonorsManagerScopeVersionsStatesAndActorSeparation()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var b = await Deposit(c, d);
        var staff = await Client(Position.Staff, d.AreaId, false); var outside = await Client(all: false);
        await Assert.ThrowsAsync<ForbiddenException>(() => staff.Bookings.CancelHomestayUnavailableAsync(new(b.Booking.Id, "Test", b.Booking.RowVer)));
        await Assert.ThrowsAsync<NotFoundException>(() => outside.Bookings.CancelHomestayUnavailableAsync(new(b.Booking.Id, "Test", b.Booking.RowVer)));
        var r = await Submit(c, b);
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Bookings.CancelHomestayUnavailableAsync(new(b.Booking.Id, "Test", b.Booking.RowVer)));
        Assert.Equal(ChangeRequestStatus.Pending, (await c.Changes.GetAsync(r.Request.Id)).Request.Status);
        Assert.Equal(b.Booking.RowVer, (await c.Bookings.GetAsync(b.Booking.Id)).Booking.RowVer);
        var manager = await Client();
        await c.Login.ExecuteAsync("UPDATE dbo.Bookings SET UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;", b.Booking.Id);
        await Assert.ThrowsAsync<ConcurrencyException>(() => manager.Bookings.CancelHomestayUnavailableAsync(new(b.Booking.Id, "Test", b.Booking.RowVer)));
        var id = await d.BookingAsync(c, 1); var unpaid = await c.Bookings.GetAsync(id);
        await Assert.ThrowsAsync<BusinessException>(() => manager.Bookings.CancelHomestayUnavailableAsync(new(id, "Test", unpaid.Booking.RowVer)));
        var brokenId = await d.BookingAsync(c, 2, BookingStatus.Deposited); var broken = await c.Bookings.GetAsync(brokenId);
        await Assert.ThrowsAsync<BusinessException>(() => manager.Bookings.CancelHomestayUnavailableAsync(new(brokenId, "Test", broken.Booking.RowVer)));
    }

    [SqlFact]
    public async Task TwoAccountantsPayingHaveExactlyOneWinnerAndOneEvidenceFile()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var b = await Deposit(c, d);
        await Approve(await Client(), await Submit(c, b));
        var a = await Client(Position.Accountant); var z = await Client(Position.Accountant); var p = await Pending(a, b.Booking.Id);
        var request = (await PaidRequest(a, p)) with { EvidenceBytes = "%PDF-test refund"u8.ToArray(), EvidenceFileName = "refund.pdf" };
        var beforeFiles = await c.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.StoredFiles;");
        var results = await Race(() => a.Payments.RecordRefundPaidAsync(request), () => z.Payments.RecordRefundPaidAsync(request));
        var paid = Assert.Single(results, x => x.Value is not null).Value!;
        Assert.IsType<ConcurrencyException>(Assert.Single(results, x => x.Error is not null).Error);
        Assert.Equal(beforeFiles + 1, await c.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.StoredFiles;"));
        Assert.Equal(PaymentStatus.Verified, paid.Status); Assert.NotNull(paid.EvidenceFileId);
        Assert.Equal(1, await Audit(c, "Payments", p.Id, "Payment.RefundPaid"));
        var file = await a.Files.GetEvidenceAsync(b.Booking.Id, paid.EvidenceFileId!.Value);
        Assert.Equal(request.EvidenceBytes, file.Content); Assert.Equal("application/pdf", file.ContentType);
        var hash = await c.Factory.ReadAsync(async (db, ct) =>
        {
            using var cmd = db.CreateCommand("SELECT Sha256 FROM dbo.StoredFiles WHERE Id=@id;");
            cmd.Parameters.AddGuid("@id", paid.EvidenceFileId);
            return (byte[])(await db.ScalarAsync(cmd, ct))!;
        }, CancellationToken.None);
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(request.EvidenceBytes!), hash);
        await Assert.ThrowsAsync<ConcurrencyException>(() => a.Payments.RecordRefundPaidAsync(request));
        await Assert.ThrowsAsync<BusinessException>(() => a.Payments.RecordRefundPaidAsync(request with { RowVer = paid.RowVer }));
        Assert.Equal(1, await Notice(c, b.Booking.Id, $"Phiếu chi hoàn {p.VoucherNo} đơn {b.Booking.Code} đã chi"));
    }

    [SqlFact]
    public async Task RefundValidationRejectsFakeOversizedFutureNonUtcAndInvalidReferencesWithoutWrites()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var b = await Deposit(c, d);
        await Approve(await Client(), await Submit(c, b));
        var kt = await Client(Position.Accountant); var p = await Pending(kt, b.Booking.Id); var request = await PaidRequest(kt, p);
        var beforeFiles = await c.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.StoredFiles;");
        var invalid = new[]
        {
            request with { EvidenceBytes = "fake pdf"u8.ToArray(), EvidenceFileName = "fake.pdf" },
            request with { EvidenceBytes = new byte[ReceiptRules.MaxEvidenceBytes + 1], EvidenceFileName = "big.pdf" },
            request with { EvidenceBytes = [], EvidenceFileName = "empty.pdf" },
            request with { EvidenceBytes = "%PDF-test"u8.ToArray(), EvidenceFileName = " " },
            request with { PaidAtUtc = request.PaidAtUtc.AddMinutes(10) },
            request with { PaidAtUtc = DateTime.SpecifyKind(request.PaidAtUtc, DateTimeKind.Local) },
            request with { PaidAtUtc = default },
            request with { BankRef = " " }, request with { BankRef = new string('X', 101) },
            request with { BankRef = "mã" }, request with { BankRef = "ABC\nDEF" }, request with { RowVer = [] }
        };
        foreach (var input in invalid) await Assert.ThrowsAsync<BusinessException>(() => kt.Payments.RecordRefundPaidAsync(input));
        var staff = await Client(Position.Staff, d.AreaId, false);
        await Assert.ThrowsAsync<ForbiddenException>(() => staff.Payments.RecordRefundPaidAsync(request));
        await Assert.ThrowsAsync<ForbiddenException>(() => c.Payments.RecordRefundPaidAsync(request));
        Assert.Equal(p.RowVer, (await Pending(kt, b.Booking.Id)).RowVer);
        Assert.Equal(beforeFiles, await c.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.StoredFiles;"));
        Assert.Equal(0, await Audit(c, "Payments", p.Id, "Payment.RefundPaid"));
    }

    [SqlFact]
    public async Task RefundPaymentMasksAccountInAuditAndSupportsCashWithoutTransferredAt()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var b = await Deposit(c, d);
        // Also exercises the extracted M3.3A refund path through public APIs.
        await c.Bookings.RejectOnBehalfAsync(new(b.Booking.Id, "Chủ từ chối", b.Booking.RowVer));
        var kt = await Client(Position.Accountant); var p = await Pending(kt, b.Booking.Id);
        const string account = "1234567890123456";
        await c.MutateAsync("UPDATE dbo.Payments SET Method='Cash',PayerOrPayeeAccount=@account WHERE Id=@id;", cmd =>
        { cmd.Parameters.AddInt32("@id", p.Id); cmd.Parameters.AddVarChar("@account", 30, account); });
        p = await Pending(kt, b.Booking.Id);
        var paid = await kt.Payments.RecordRefundPaidAsync(await PaidRequest(kt, p));
        Assert.Equal(PaymentMethod.Cash, paid.Method); Assert.Null(paid.TransferredAtUtc);
        var json = await c.ScalarAsync<string>("SELECT NewValues FROM dbo.AuditLogs WHERE TableName='Payments' AND RecordId=@ref AND ActionCode='Payment.RefundPaid';",
            cmd => cmd.Parameters.AddVarChar("@ref", 50, p.Id.ToString(CultureInfo.InvariantCulture)));
        Assert.DoesNotContain(account, json); Assert.Contains(ReceiptRules.MaskAccount(account)!, json);
    }

    [SqlFact]
    public async Task RefundPaymentRejectsCancellationSenderAndApproverEvenWithAnomalousAccountantData()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var b = await Deposit(c, d); var r = await Submit(c, b);
        var manager = await Client(); await Approve(manager, r);
        var kt = await Client(Position.Accountant); var p = await Pending(kt, b.Booking.Id); var request = await PaidRequest(kt, p);
        // Seed unusual historical identities directly: test BLL UserId separation independently of current role.
        foreach (var sender in new[] { true, false })
        {
            await c.MutateAsync(sender ? "UPDATE dbo.BookingChangeRequests SET RequestedByUserId=@user WHERE Id=@id;"
                : "UPDATE dbo.BookingChangeRequests SET RequestedByUserId=@sender,DecidedByUserId=@user WHERE Id=@id;", cmd =>
            {
                cmd.Parameters.AddInt32("@id", r.Request.Id); cmd.Parameters.AddInt32("@user", kt.Current.RequireSession().UserId);
                if (!sender) cmd.Parameters.AddInt32("@sender", c.Current.RequireSession().UserId);
            });
            await Assert.ThrowsAsync<ForbiddenException>(() => kt.Payments.RecordRefundPaidAsync(request));
        }
        // Homestay cancellation and owner rejection have no Cancel request: the refund creator is also protected.
        await c.MutateAsync("UPDATE dbo.BookingChangeRequests SET DecidedByUserId=@manager WHERE Id=@id; UPDATE dbo.Payments SET RecordedByUserId=@user WHERE Id=@payment;", cmd =>
        {
            cmd.Parameters.AddInt32("@id", r.Request.Id); cmd.Parameters.AddInt32("@manager", manager.Current.RequireSession().UserId);
            cmd.Parameters.AddInt32("@payment", p.Id); cmd.Parameters.AddInt32("@user", kt.Current.RequireSession().UserId);
        });
        p = await Pending(kt, b.Booking.Id);
        await Assert.ThrowsAsync<ForbiddenException>(() => kt.Payments.RecordRefundPaidAsync(request with { RowVer = p.RowVer }));
        Assert.Equal(0, await Audit(c, "Payments", p.Id, "Payment.RefundPaid"));
    }

    [SqlFact]
    public async Task RefundStaleVersionAndWrongTypeCannotWriteOrStoreEvidence()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var b = await Deposit(c, d);
        await Approve(await Client(), await Submit(c, b));
        var kt = await Client(Position.Accountant); var p = await Pending(kt, b.Booking.Id);
        var request = (await PaidRequest(kt, p)) with { EvidenceBytes = "%PDF-test"u8.ToArray(), EvidenceFileName = "proof.pdf" };
        var count = await c.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.StoredFiles;");
        await c.Login.ExecuteAsync("UPDATE dbo.Payments SET UpdatedAt=SYSUTCDATETIME() WHERE Id=@id;", p.Id);
        await Assert.ThrowsAsync<ConcurrencyException>(() => kt.Payments.RecordRefundPaidAsync(request));
        var receipt = (await kt.Payments.GetReceiptForVerificationAsync(Assert.Single(b.Payments).Id)).Receipt;
        await Assert.ThrowsAsync<NotFoundException>(() => kt.Payments.RecordRefundPaidAsync(request with { PaymentId = receipt.Id, RowVer = receipt.RowVer }));
        Assert.Equal(count, await c.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.StoredFiles;"));
        Assert.Equal(PaymentStatus.Pending, (await Pending(kt, b.Booking.Id)).Status);
    }

    [SqlFact]
    public async Task OutOfScopeWritesAreNotFoundAndSubmissionNotifiesOnlyScopedManagers()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var b = await Deposit(c, d);
        var inside = await Client(Position.Manager, d.AreaId, false); var outside = await Client(all: false);
        var staffOutside = await Client(Position.Staff, all: false);
        await Assert.ThrowsAsync<NotFoundException>(() => Submit(outside, b));
        await Assert.ThrowsAsync<NotFoundException>(() => Submit(staffOutside, b));
        var r = await Submit(c, b);
        await Assert.ThrowsAsync<NotFoundException>(() => Approve(outside, r));
        await Assert.ThrowsAsync<NotFoundException>(() => outside.Changes.RejectAsync(new(r.Request.Id, "Test", r.Request.RowVer, b.Booking.RowVer)));
        foreach (var (client, expected) in new[] { (c, 1), (inside, 1), (outside, 0) })
            Assert.Equal(expected, await Notice(client, b.Booking.Id, $"Đơn {b.Booking.Code} có yêu cầu hủy chờ duyệt"));
        Assert.Equal(ChangeRequestStatus.Pending, (await c.Changes.GetAsync(r.Request.Id)).Request.Status);
    }

    [SqlFact]
    public async Task RequiredReasonsOptionalApprovalNoteAndVersionsAreValidated()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var b = await Deposit(c, d); var manager = await Client();
        foreach (var reason in new[] { " ", new string('X', 501) })
        {
            await Assert.ThrowsAsync<BusinessException>(() => c.Changes.SubmitCancelAsync(new(b.Booking.Id, reason, b.Booking.RowVer)));
            await Assert.ThrowsAsync<BusinessException>(() => manager.Bookings.CancelHomestayUnavailableAsync(new(b.Booking.Id, reason, b.Booking.RowVer)));
        }
        await Assert.ThrowsAsync<BusinessException>(() => c.Changes.SubmitCancelAsync(new(b.Booking.Id, "Test", [])));
        var r = await Submit(c, b);
        await Assert.ThrowsAsync<BusinessException>(() => manager.Changes.RejectAsync(new(r.Request.Id, " ", r.Request.RowVer, b.Booking.RowVer)));
        await Assert.ThrowsAsync<BusinessException>(() => manager.Changes.ApproveAsync(new(r.Request.Id, r.Request.RowVer, b.Booking.RowVer, new string('X', 501))));
        await Assert.ThrowsAsync<BusinessException>(() => manager.Changes.ApproveAsync(new(r.Request.Id, [], b.Booking.RowVer)));
        var approved = await manager.Changes.ApproveAsync(new(r.Request.Id, r.Request.RowVer, b.Booking.RowVer));
        Assert.Null(approved.Request.DecisionNote);
    }

    [SqlFact]
    public async Task ReplacedRealSessionsCannotPerformAnyOfTheFiveWrites()
    {
        var c = new M21TestClient(); var identity = await c.LoginNewAsync(); var d = await M21Data.CreateAsync(c); var b = await Deposit(c, d);
        var r = await Submit(c, b);
        var manager = new M21TestClient(); var managerIdentity = await manager.LoginNewAsync();
        var replacement = new M21TestClient(); await replacement.Login.LoginAsync(identity);
        await Assert.ThrowsAsync<SessionExpiredException>(() => Submit(c, b));
        await Assert.ThrowsAsync<SessionExpiredException>(() => c.Bookings.CancelHomestayUnavailableAsync(new(b.Booking.Id, "Test", b.Booking.RowVer)));
        var replacementManager = new M21TestClient(); await replacementManager.Login.LoginAsync(managerIdentity);
        await Assert.ThrowsAsync<SessionExpiredException>(() => Approve(manager, r));
        await Assert.ThrowsAsync<SessionExpiredException>(() => manager.Changes.RejectAsync(new(r.Request.Id, "Test", r.Request.RowVer, b.Booking.RowVer)));
        await Approve(await Client(), r);
        var kt = new M21TestClient(); var ktIdentity = await kt.LoginNewAsync(Position.Accountant);
        var p = await Pending(kt, b.Booking.Id); var request = await PaidRequest(kt, p);
        var replacementKt = new M21TestClient(); await replacementKt.Login.LoginAsync(ktIdentity);
        await Assert.ThrowsAsync<SessionExpiredException>(() => kt.Payments.RecordRefundPaidAsync(request));
    }

    [SqlFact]
    public async Task RefundInsertFailureRollsBackApprovalOrExpiryBookingLedgerAuditNotificationsAndCounter()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var manager = await Client();
        for (var i = 0; i < 2; i++)
        {
            var b = await Deposit(c, d, i); var r = await Submit(c, b);
            var other = await d.BookingAsync(c, i + 2, BookingStatus.Rejected);
            var now = await Now(c); var key = "PC" + now.AddHours(7).ToString("yyMM", CultureInfo.InvariantCulture);
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
            var noticeCount = await c.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Notifications;");
            var auditCount = await c.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AuditLogs;");
            try
            {
                var ex = i == 0
                    ? await Assert.ThrowsAsync<BusinessException>(() => Approve(manager, r))
                    : await Assert.ThrowsAsync<BusinessException>(() => manager.Bookings.CancelHomestayUnavailableAsync(new(b.Booking.Id, "Không phục vụ được", b.Booking.RowVer)));
                Assert.Equal("UQ_Payments_VoucherNo", ex.ConstraintName);
                var unchanged = await c.Changes.GetAsync(r.Request.Id);
                Assert.Equal(ChangeRequestStatus.Pending, unchanged.Request.Status); Assert.Equal(r.Request.RowVer, unchanged.Request.RowVer);
                Assert.Equal(b.Booking.RowVer, unchanged.Booking.Booking.RowVer); Assert.Equal(b.History.Count, unchanged.Booking.History.Count);
                Assert.Single(unchanged.Booking.Payments); Assert.Null(unchanged.Request.DecidedAtUtc);
                Assert.Equal(2, await Scalar<int>(c, "SELECT COUNT(*) FROM dbo.RoomNights WHERE BookingId=@id;", b.Booking.Id));
                Assert.Equal(noticeCount, await c.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Notifications;"));
                Assert.Equal(auditCount, await c.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.AuditLogs;"));
                Assert.Equal(before, await c.ScalarAsync<int>("SELECT COALESCE((SELECT LastValue FROM dbo.Counters WHERE CounterKey=@key),0);",
                    cmd => cmd.Parameters.AddVarChar("@key", 30, key)));
            }
            finally
            {
                // Remove the intentional global voucher collision for later tests; Payments cannot be deleted by app role.
                await c.MutateAsync("UPDATE dbo.Payments SET VoucherNo=@voucher WHERE BookingId=@id;", cmd =>
                {
                    cmd.Parameters.AddVarChar("@voucher", 20, "X" + Guid.NewGuid().ToString("N")[..19]); cmd.Parameters.AddInt32("@id", other);
                });
            }
        }
    }

    [SqlFact]
    public async Task SubmissionRecalculatesFiftyPercentPolicyInTransactionAndRejectsMissingLegacyQuote()
    {
        var c = await Client(); var d = await M21Data.CreateAsync(c); var today = await Today(c);
        var b = await Deposit(c, d, date: today.AddDays(5));
        var preview = await c.Bookings.PreviewRefundAsync(b.Booking.Id); Assert.Equal(50, preview.Calculation.RefundPercent);
        var r = await Submit(c, b);
        Assert.Equal(preview.Calculation.RefundAmount, r.Request.QuotedRefundAmount);
        Assert.Equal(DateTimeKind.Utc, r.Request.RequestedAtUtc.Kind);
        Assert.Equal(DateOnly.FromDateTime(r.Request.RequestedAtUtc.AddHours(7)), today);
        await c.Login.ExecuteAsync("UPDATE dbo.BookingChangeRequests SET QuotedRefundAmount=NULL WHERE Id=@id;", r.Request.Id);
        var legacy = await c.Changes.GetAsync(r.Request.Id); var manager = await Client();
        await Assert.ThrowsAsync<BusinessException>(() => Approve(manager, legacy));
        Assert.Equal(ChangeRequestStatus.Pending, (await c.Changes.GetAsync(r.Request.Id)).Request.Status);
        await manager.Changes.RejectAsync(new(legacy.Request.Id, "Thiếu quote, gửi lại", legacy.Request.RowVer, b.Booking.RowVer));
        Assert.Equal(ChangeRequestStatus.Pending, (await Submit(c, b)).Request.Status);
    }

}
