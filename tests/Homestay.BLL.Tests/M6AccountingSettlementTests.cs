using System.Security.Cryptography;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Security;
using Homestay.BLL.Services;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Availability;
using Homestay.DTO.Bookings;
using Homestay.DTO.Enums;
using Homestay.DTO.Finance;

namespace Homestay.BLL.Tests;

[Collection("M23 SQL")]
public sealed class M6AccountingSettlementTests
{
    private static WriteOperationRunner Writes(M21TestClient c) =>
        new(c.Factory, c.Current, new WriteSessionValidator(new AuthRepository()));

    private static AccountingService Accounting(M21TestClient c)
    {
        var authorizer = new Authorizer(c.Current);
        var clock = new DbClock(c.Factory);
        var settings = new SettingsProvider(c.Factory);
        var audit = new AuditWriter();
        var bookings = new BookingRepository();
        return new(authorizer, c.Current, c.Factory, Writes(c), new FinanceRepository(), bookings, c.Bookings,
            clock, settings, audit, c.Login.Machine);
    }

    private static SettlementService Settlements(M21TestClient c) => new(new Authorizer(c.Current), c.Current,
        c.Factory, Writes(c), new SettlementRepository(), new DbClock(c.Factory), new SettingsProvider(c.Factory),
        new AuditWriter(), c.Login.Machine);

    private static RecordBankTransactionRequest BankRow(string account, string reference, string content,
        decimal amount, DateTime at) => new(account, reference, amount, content, "Khách M6", null, at);

    private static async Task<int> CountSettlement(M21TestClient c, int home, int year, int month, int period) =>
        await c.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Settlements WHERE HomestayId=@home AND [Year]=@year AND [Month]=@month AND PeriodNo=@period;", cmd =>
        {
            cmd.Parameters.AddInt32("@home", home); cmd.Parameters.AddInt32("@year", year);
            cmd.Parameters.AddInt32("@month", month); cmd.Parameters.AddInt32("@period", period);
        });

    [SqlFact]
    public async Task DuplicateBankReferenceIsRejectedForSameAccount()
    {
        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant);
        var service = Accounting(accountant);
        var now = DateTime.SpecifyKind(await new DbClock(accountant.Factory).GetUtcNowAsync(CancellationToken.None), DateTimeKind.Utc);
        var request = BankRow("M6-ACCOUNT", "M6-" + Guid.NewGuid().ToString("N"), "test", 100_000, now);

        await service.RecordBankTransactionAsync(request);
        var error = await Assert.ThrowsAsync<BusinessException>(() => service.RecordBankTransactionAsync(request));

        Assert.Contains("đã tồn tại", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, await accountant.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.BankTransactions WHERE AccountNumber=@account AND BankReference=@reference;", cmd =>
        {
            cmd.Parameters.AddVarChar("@account", 30, request.AccountNumber);
            cmd.Parameters.AddVarChar("@reference", 100, request.BankReference);
        }));
    }

    [SqlFact]
    public async Task AutoMatchContinuesAfterUnknownBookingCode()
    {
        var manager = new M21TestClient(); await manager.LoginNewAsync();
        var data = await M21Data.CreateAsync(manager);
        var booking = await manager.Bookings.CreateAsync(new CreateBookingRequest
        {
            CustomerId = data.CustomerId, RoomTypeId = data.RoomTypeId, CheckIn = M21Data.CheckIn,
            CheckOut = M21Data.CheckOut, Guests = 2, ContactName = "Khách M6", ContactPhone = M14TestClient.Phone(),
            SourceChannel = SourceChannel.Phone
        });

        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant);
        var service = Accounting(accountant);
        var now = DateTime.SpecifyKind(await new DbClock(accountant.Factory).GetUtcNowAsync(CancellationToken.None), DateTimeKind.Utc);
        var missingCode = $"FJ{RandomNumberGenerator.GetInt32(100000, 999999)}-{RandomNumberGenerator.GetInt32(0, 1000):D3}";
        var validId = await service.RecordBankTransactionAsync(BankRow("M6-ACCOUNT", "M6-OK-" + Guid.NewGuid().ToString("N"), booking.Booking.Code, booking.AmountDue, now.AddMinutes(-1)));
        var invalidId = await service.RecordBankTransactionAsync(BankRow("M6-ACCOUNT", "M6-BAD-" + Guid.NewGuid().ToString("N"), missingCode, booking.AmountDue, now.AddSeconds(-30)));

        var results = await service.AutoMatchAsync(now.AddHours(-1), now.AddMinutes(1));

        Assert.Equal(MatchStatus.Unmatched, Assert.Single(results, x => x.Id == invalidId).MatchStatus);
        var matched = Assert.Single(results, x => x.Id == validId);
        Assert.Equal(MatchStatus.Matched, matched.MatchStatus);
        Assert.Equal(booking.Booking.Id, matched.MatchedBookingId);
        Assert.Equal(BookingStatus.Deposited, (await accountant.Bookings.GetAsync(booking.Booking.Id)).Booking.Status);
    }

    [SqlFact]
    public async Task ConcurrentAccountantsCreateOnlyOneSettlementForPeriod()
    {
        var manager = new M21TestClient(); await manager.LoginNewAsync();
        var data = await M21Data.CreateAsync(manager, 2);
        await data.BookingAsync(manager, 0, BookingStatus.Completed, checkIn: new DateOnly(2025, 1, 5), checkOut: new DateOnly(2025, 1, 7));
        var first = new M21TestClient(); var second = new M21TestClient();
        await Task.WhenAll(first.LoginNewAsync(Position.Accountant), second.LoginNewAsync(Position.Accountant));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = new[] { first, second }.Select(async c =>
        {
            await gate.Task;
            return await Settlements(c).CreateOrRecreateDraftAsync(data.HomestayId, 2025, 1, 1);
        }).ToArray();
        gate.SetResult();
        var drafts = await Task.WhenAll(calls);

        Assert.Equal(drafts[0].Id, drafts[1].Id);
        Assert.Equal(1, await CountSettlement(manager, data.HomestayId, 2025, 1, 1));
    }

    [SqlFact]
    public async Task SendingSettlementIsBlockedWhilePeriodHasUnfinalizedBooking()
    {
        var manager = new M21TestClient(); await manager.LoginNewAsync();
        var data = await M21Data.CreateAsync(manager, 2);
        await data.BookingAsync(manager, 0, BookingStatus.Completed, checkIn: new DateOnly(2025, 1, 5), checkOut: new DateOnly(2025, 1, 7));
        var unfinalizedId = await data.BookingAsync(manager, 1, BookingStatus.AwaitingPayment,
            checkIn: new DateOnly(2025, 1, 8), checkOut: new DateOnly(2025, 1, 10));

        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant);
        var service = Settlements(accountant);
        var draft = await service.CreateOrRecreateDraftAsync(data.HomestayId, 2025, 1, 1);
        var unfinalized = await accountant.Bookings.GetAsync(unfinalizedId);
        Assert.Contains(unfinalized.Booking.Code, draft.UnfinalizedBookingCodes);

        var error = await Assert.ThrowsAsync<BusinessException>(() => service.SendAsync(new(draft.Id, draft.RowVer, "")));

        Assert.Contains("chưa chốt", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(SettlementStatus.Draft, (await service.GetAsync(draft.Id)).Status);
    }

    [SqlFact]
    public async Task UnpaidCancellationDoesNotBecomeDepositOrSettlementPayout()
    {
        var manager = new M21TestClient(); await manager.LoginNewAsync();
        var data = await M21Data.CreateAsync(manager, 2);
        var unpaidCancellationId = await data.BookingAsync(manager, 0, BookingStatus.Cancelled,
            checkIn: new DateOnly(2025, 1, 5), checkOut: new DateOnly(2025, 1, 7));
        var completedId = await data.BookingAsync(manager, 1, BookingStatus.Completed,
            checkIn: new DateOnly(2025, 1, 8), checkOut: new DateOnly(2025, 1, 10));
        var verifier = await manager.Login.CreateIdentityAsync(Position.Accountant);
        await manager.MutateAsync("""
            INSERT dbo.Payments(VoucherNo,BookingId,Direction,Type,Method,Amount,Status,RecordedByUserId,RecordedAt,VerifiedByUserId,VerifiedAt)
            VALUES(@voucher,@booking,'In','Deposit','BankTransfer',600000,'Verified',@recorder,SYSUTCDATETIME(),@verifier,SYSUTCDATETIME());
            """, cmd =>
        {
            cmd.Parameters.AddVarChar("@voucher", 20, "M6" + Guid.NewGuid().ToString("N")[..18]);
            cmd.Parameters.AddInt32("@booking", completedId); cmd.Parameters.AddInt32("@recorder", manager.Current.RequireSession().UserId);
            cmd.Parameters.AddInt32("@verifier", verifier.UserId);
        });
        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant);

        var draft = await Settlements(accountant).CreateOrRecreateDraftAsync(data.HomestayId, 2025, 1, 1);

        Assert.DoesNotContain(draft.Items, x => x.BookingId == unpaidCancellationId);
        Assert.Equal(completedId, Assert.Single(draft.Items).BookingId);
        Assert.Equal(600_000, draft.Totals.TotalRetained);
        Assert.Equal(360_000, draft.Totals.PayoutAmount);
    }

    [SqlFact]
    public async Task EmptyPeriodDoesNotCreateZeroValueDraft()
    {
        var manager = new M21TestClient(); await manager.LoginNewAsync();
        var data = await M21Data.CreateAsync(manager);
        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant);

        var error = await Assert.ThrowsAsync<BusinessException>(() =>
            Settlements(accountant).CreateOrRecreateDraftAsync(data.HomestayId, 2025, 1, 1));

        Assert.Equal("Kỳ này không có đơn để đối soát.", error.Message);
        Assert.Equal(0, await CountSettlement(manager, data.HomestayId, 2025, 1, 1));
    }

    [SqlFact]
    public async Task SendingRequiresRecreatingDraftAfterCheckedInBookingCompletes()
    {
        var manager = new M21TestClient(); await manager.LoginNewAsync();
        var data = await M21Data.CreateAsync(manager, 2);
        await data.BookingAsync(manager, 0, BookingStatus.Completed, checkIn: new(2025, 1, 5), checkOut: new(2025, 1, 7));
        var bookingId = await data.BookingAsync(manager, 1, BookingStatus.CheckedIn, checkIn: new(2025, 1, 8), checkOut: new(2025, 1, 10));
        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant);
        var service = Settlements(accountant);
        var draft = await service.CreateOrRecreateDraftAsync(data.HomestayId, 2025, 1, 1);
        Assert.Single(draft.Items);
        await manager.MutateAsync("UPDATE dbo.Bookings SET Status='Completed',CompletedAt=SYSUTCDATETIME() WHERE Id=@id;", c => c.Parameters.AddInt32("@id", bookingId));

        var error = await Assert.ThrowsAsync<BusinessException>(() => service.SendAsync(new(draft.Id, draft.RowVer, "")));

        Assert.Equal("Số liệu kỳ đã thay đổi, hãy lập lại bảng nháp trước khi gửi.", error.Message);
        var unchanged = await service.GetAsync(draft.Id);
        Assert.Equal(SettlementStatus.Draft, unchanged.Status); Assert.Equal(draft.RowVer, unchanged.RowVer);
        Assert.Equal(draft.Items.ToArray(), unchanged.Items.ToArray()); Assert.Empty(unchanged.UnfinalizedBookingCodes);
        var rebuilt = await service.CreateOrRecreateDraftAsync(data.HomestayId, 2025, 1, 1);
        Assert.Equal(2, rebuilt.Items.Count); Assert.Contains(rebuilt.Items, x => x.BookingId == bookingId);
        Assert.Equal(SettlementStatus.Sent, (await service.SendAsync(new(rebuilt.Id, rebuilt.RowVer, ""))).Status);
    }

    [SqlFact]
    public async Task SendingChecksEachLineEvenWhenBookingSetAndTotalsAreUnchanged()
    {
        var manager = new M21TestClient(); await manager.LoginNewAsync();
        var data = await M21Data.CreateAsync(manager, 2);
        var first = await data.BookingAsync(manager, 0, BookingStatus.Completed, checkIn: new(2025, 1, 5), checkOut: new(2025, 1, 7));
        var second = await data.BookingAsync(manager, 1, BookingStatus.Completed, checkIn: new(2025, 1, 8), checkOut: new(2025, 1, 10));
        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant);
        var service = Settlements(accountant);
        var draft = await service.CreateOrRecreateDraftAsync(data.HomestayId, 2025, 1, 1);
        await manager.MutateAsync("UPDATE dbo.Bookings SET TotalAmount=TotalAmount+CASE WHEN Id=@first THEN 100000 ELSE -100000 END WHERE Id IN (@first,@second);", c =>
        { c.Parameters.AddInt32("@first", first); c.Parameters.AddInt32("@second", second); });

        var error = await Assert.ThrowsAsync<BusinessException>(() => service.SendAsync(new(draft.Id, draft.RowVer, "")));

        Assert.Equal("Số liệu kỳ đã thay đổi, hãy lập lại bảng nháp trước khi gửi.", error.Message);
        Assert.Equal(draft.Items.ToArray(), (await service.GetAsync(draft.Id)).Items.ToArray());
        var rebuilt = await service.CreateOrRecreateDraftAsync(data.HomestayId, 2025, 1, 1);
        Assert.Equal(draft.Totals, rebuilt.Totals);
        Assert.Equal(SettlementStatus.Sent, (await service.SendAsync(new(rebuilt.Id, rebuilt.RowVer, ""))).Status);
    }

    [SqlFact]
    public async Task ManagerCanCreateAndRecreateDraftButOnlyAccountantCanManageIt()
    {
        var manager = new M21TestClient(); await manager.LoginNewAsync();
        var data = await M21Data.CreateAsync(manager);
        await data.BookingAsync(manager, 0, BookingStatus.Completed, checkIn: new(2025, 1, 5), checkOut: new(2025, 1, 7));
        var service = Settlements(manager);
        var draft = await service.CreateOrRecreateDraftAsync(data.HomestayId, 2025, 1, 1);
        var rebuilt = await service.CreateOrRecreateDraftAsync(data.HomestayId, 2025, 1, 1);
        Assert.Equal(draft.Id, rebuilt.Id); Assert.False(draft.RowVer.SequenceEqual(rebuilt.RowVer));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.SendAsync(new(rebuilt.Id, rebuilt.RowVer, "")));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.ReopenAsync(new(rebuilt.Id, rebuilt.RowVer, "Mở lại")));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.MarkPaidAsync(new(rebuilt.Id, rebuilt.RowVer, "BANK")));
        await Assert.ThrowsAsync<ForbiddenException>(() => service.RecordOwnerFeedbackAsync(new(rebuilt.Id, rebuilt.RowVer, "Phản hồi")));
        var staff = new M21TestClient(); await staff.LoginNewAsync(Position.Staff);
        await Assert.ThrowsAsync<ForbiddenException>(() => Settlements(staff).CreateOrRecreateDraftAsync(data.HomestayId, 2025, 1, 1));
        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant);
        Assert.Equal(SettlementStatus.Draft, (await Settlements(accountant).CreateOrRecreateDraftAsync(data.HomestayId, 2025, 1, 1)).Status);
    }

    private static Task<string> SettlementStamps(M21TestClient c, int id) => c.ScalarAsync<string>(
        "SELECT Status,SentAt,SentByUserId,PaidAt,PaidByUserId,PaidBankRef FROM dbo.Settlements WHERE Id=@id FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;", p => p.Parameters.AddInt32("@id", id));

    [SqlFact]
    public async Task OwnerFeedbackPreservesSentAndPaidTimestampsActorsAndBankReference()
    {
        var manager = new M21TestClient(); await manager.LoginNewAsync();
        var data = await M21Data.CreateAsync(manager);
        await data.BookingAsync(manager, 0, BookingStatus.Completed, checkIn: new(2025, 1, 5), checkOut: new(2025, 1, 7));
        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant);
        var service = Settlements(accountant);
        var draft = await service.CreateOrRecreateDraftAsync(data.HomestayId, 2025, 1, 1);
        await service.SendAsync(new(draft.Id, draft.RowVer, ""));
        foreach (var status in new[] { "Sent", "Paid" })
        {
            await manager.MutateAsync("UPDATE dbo.Settlements SET Status=@status,SentAt='2025-01-20',SentByUserId=@user,PaidAt=CASE WHEN @status='Paid' THEN CONVERT(datetime2,'2025-01-21') END,PaidByUserId=CASE WHEN @status='Paid' THEN @user END,PaidBankRef=CASE WHEN @status='Paid' THEN 'ORIGINAL-BANK-REF' END,UpdatedAt='2025-01-21' WHERE Id=@id;", c =>
            { c.Parameters.AddVarChar("@status", 20, status); c.Parameters.AddInt32("@user", manager.Current.RequireSession().UserId); c.Parameters.AddInt32("@id", draft.Id); });
            var before = await service.GetAsync(draft.Id); var stamps = await SettlementStamps(manager, draft.Id);

            var result = await service.RecordOwnerFeedbackAsync(new(draft.Id, before.RowVer, "Phản hồi " + status));

            Assert.Equal("Phản hồi " + status, result.OwnerFeedback);
            Assert.Equal(stamps, await SettlementStamps(manager, draft.Id)); Assert.False(before.RowVer.SequenceEqual(result.RowVer));
            Assert.Equal(1, await manager.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Settlements WHERE Id=@id AND UpdatedAt>'2025-01-21';", c => c.Parameters.AddInt32("@id", draft.Id)));
            await Assert.ThrowsAsync<ConcurrencyException>(() => service.RecordOwnerFeedbackAsync(new(draft.Id, before.RowVer, "Cũ")));
        }
        Assert.Equal(2, await AuditCount(manager, "Settlements", draft.Id, "Settlement.OwnerFeedback"));
    }

    private static Task<int> AuditCount(M21TestClient c, string table, int id, string action) => c.ScalarAsync<int>(
        "SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName=@table AND RecordId=@id AND ActionCode=@action;", p =>
        { p.Parameters.AddVarChar("@table", 50, table); p.Parameters.AddVarChar("@id", 50, id.ToString()); p.Parameters.AddVarChar("@action", 100, action); });

    private static async Task<BankTransactionItem> BankTransaction(AccountingService service, int id, DateTime now) =>
        Assert.Single(await service.ListBankTransactionsAsync(now.AddHours(-1), now.AddHours(1), null, null), x => x.Id == id);

    [SqlFact]
    public async Task UnmatchedUnderpaidAndLateTransfersCanBeRefundedOnceAndMatchingIsAudited()
    {
        var manager = new M21TestClient(); await manager.LoginNewAsync();
        var data = await M21Data.CreateAsync(manager, 2);
        var underpaidBooking = await data.BookingAsync(manager, 0);
        var lateBooking = await data.BookingAsync(manager, 1, BookingStatus.Expired);
        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant);
        var service = Accounting(accountant);
        var now = await new DbClock(accountant.Factory).GetUtcNowAsync(CancellationToken.None);
        foreach (var status in new[] { MatchStatus.Unmatched, MatchStatus.Underpaid, MatchStatus.LatePayment })
        {
            var amount = status == MatchStatus.Underpaid ? 100_000m : 600_000m;
            var id = await service.RecordBankTransactionAsync(BankRow("M6-REFUND", Guid.NewGuid().ToString("N"), "M6", amount, now));
            var tx = await BankTransaction(service, id, now);
            if (status != MatchStatus.Unmatched)
            {
                var booking = status == MatchStatus.Underpaid ? underpaidBooking : lateBooking;
                await service.MatchBankTransactionAsync(new(id, booking, tx.RowVer));
                await Assert.ThrowsAsync<ConcurrencyException>(() => service.MatchBankTransactionAsync(new(id, booking, tx.RowVer)));
                tx = await BankTransaction(service, id, now); Assert.Equal(status, tx.MatchStatus);
                Assert.Equal(1, await AuditCount(manager, "BankTransactions", id, "BankTransaction." + status));
            }
            var request = new RefundBankTransactionRequest(id, tx.RowVer, "REFUND-" + id, now, "Khách yêu cầu hoàn");

            var refunded = await service.RecordUnmatchedRefundAsync(request);

            Assert.Equal(MatchStatus.Refunded, refunded.MatchStatus);
            Assert.Equal(1, await manager.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.BankTransactions WHERE Id=@id AND MatchStatus='Refunded' AND RefundedAt=@time AND RefundBankRef=@ref AND ProcessedByUserId=@user AND ProcessedAt IS NOT NULL AND Note=N'Khách yêu cầu hoàn';", c =>
            { c.Parameters.AddInt32("@id", id); c.Parameters.AddUtcDateTime("@time", now); c.Parameters.AddVarChar("@ref", 100, request.RefundBankRef); c.Parameters.AddInt32("@user", accountant.Current.RequireSession().UserId); }));
            Assert.Equal(amount, await manager.ScalarAsync<decimal>("SELECT CONVERT(decimal(18,0),JSON_VALUE(NewValues,'$.Amount')) FROM dbo.AuditLogs WHERE TableName='BankTransactions' AND RecordId=@id AND ActionCode='BankTransaction.Refund';", c => c.Parameters.AddVarChar("@id", 50, id.ToString())));
            await Assert.ThrowsAsync<ConcurrencyException>(() => service.RecordUnmatchedRefundAsync(request));
            tx = await BankTransaction(service, id, now);
            await Assert.ThrowsAsync<BusinessException>(() => service.RecordUnmatchedRefundAsync(request with { RowVer = tx.RowVer }));
            Assert.Equal(1, await AuditCount(manager, "BankTransactions", id, "BankTransaction.Refund"));
        }
    }

    [SqlFact]
    public async Task MatchedExcessRefundKeepsMatchAndVerifiedPaymentAndRemovesPayable()
    {
        var manager = new M21TestClient(); await manager.LoginNewAsync();
        var data = await M21Data.CreateAsync(manager, 2);
        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant);
        var service = Accounting(accountant);
        var now = await new DbClock(accountant.Factory).GetUtcNowAsync(CancellationToken.None);
        foreach (var automatic in new[] { false, true })
        {
            var booking = await manager.Bookings.CreateAsync(new CreateBookingRequest
            {
                CustomerId = data.CustomerId, RoomTypeId = data.RoomTypeId, CheckIn = M21Data.CheckIn,
                CheckOut = M21Data.CheckOut, Guests = 2, ContactName = "Khách M6", ContactPhone = M14TestClient.Phone(), SourceChannel = SourceChannel.Phone
            });
            var amount = booking.AmountDue + 100_000;
            var id = await service.RecordBankTransactionAsync(BankRow("M6-EXCESS", Guid.NewGuid().ToString("N"), booking.Booking.Code.Replace('-', '.'), amount, now));
            var tx = await BankTransaction(service, id, now);
            if (automatic) await service.AutoMatchAsync(now.AddSeconds(-1), now.AddSeconds(1));
            else await service.MatchBankTransactionAsync(new(id, booking.Booking.Id, tx.RowVer));
            tx = await BankTransaction(service, id, now);
            var expectedStatus = automatic ? MatchStatus.Matched : MatchStatus.ManuallyMatched;
            Assert.Equal(expectedStatus, tx.MatchStatus);
            Assert.Equal(100_000, Assert.Single(await service.ListPayablesAsync(), x => x.SourceType == "BankTransaction" && x.SourceId == id).Amount);

            // A matched row with no excess or no verified payment cannot be refunded.
            foreach (var change in new[] { "UPDATE dbo.BankTransactions SET Amount=Amount-100000 WHERE Id=@id;", "UPDATE dbo.Payments SET Status='Pending' WHERE BankTransactionId=@id;" })
            {
                await manager.MutateAsync(change, c => c.Parameters.AddInt32("@id", id));
                tx = await BankTransaction(service, id, now);
                await Assert.ThrowsAsync<BusinessException>(() => service.RecordUnmatchedRefundAsync(new(id, tx.RowVer, "NO-REFUND", now, null)));
                await manager.MutateAsync("UPDATE dbo.BankTransactions SET Amount=@amount WHERE Id=@id;UPDATE dbo.Payments SET Status='Verified' WHERE BankTransactionId=@id;", c =>
                { c.Parameters.AddInt32("@id", id); c.Parameters.AddMoney("@amount", amount); });
            }
            tx = await BankTransaction(service, id, now);
            var request = new RefundBankTransactionRequest(id, tx.RowVer, "EXCESS-REFUND-" + id, now, "Hoàn phần chuyển dư");

            var refunded = await service.RecordUnmatchedRefundAsync(request);

            Assert.Equal(expectedStatus, refunded.MatchStatus); Assert.Equal(booking.Booking.Id, refunded.MatchedBookingId);
            Assert.DoesNotContain(await service.ListPayablesAsync(), x => x.SourceType == "BankTransaction" && x.SourceId == id);
            Assert.Equal(1, await manager.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.BankTransactions WHERE Id=@id AND RefundedAt=@time AND RefundBankRef=@ref AND ProcessedByUserId=@user AND ProcessedAt IS NOT NULL AND Note=N'Hoàn phần chuyển dư';", c =>
            { c.Parameters.AddInt32("@id", id); c.Parameters.AddUtcDateTime("@time", now); c.Parameters.AddVarChar("@ref", 100, request.RefundBankRef); c.Parameters.AddInt32("@user", accountant.Current.RequireSession().UserId); }));
            Assert.Equal(amount, await manager.ScalarAsync<decimal>("SELECT Amount FROM dbo.Payments WHERE BankTransactionId=@id AND Status='Verified';", c => c.Parameters.AddInt32("@id", id)));
            Assert.Equal(100_000, await manager.ScalarAsync<decimal>("SELECT CONVERT(decimal(18,0),JSON_VALUE(NewValues,'$.Amount')) FROM dbo.AuditLogs WHERE TableName='BankTransactions' AND RecordId=@id AND ActionCode='BankTransaction.Refund';", c => c.Parameters.AddVarChar("@id", 50, id.ToString())));
            await Assert.ThrowsAsync<ConcurrencyException>(() => service.RecordUnmatchedRefundAsync(request));
            tx = await BankTransaction(service, id, now);
            await Assert.ThrowsAsync<BusinessException>(() => service.RecordUnmatchedRefundAsync(request with { RowVer = tx.RowVer }));
            Assert.Equal(1, await AuditCount(manager, "BankTransactions", id, "BankTransaction.Refund"));
        }
    }

    [SqlFact]
    public async Task AdditionalDepositExcessRefundUsesChangeRequestDueInsteadOfBookingDeposit()
    {
        var manager = new M21TestClient(); await manager.LoginNewAsync();
        var data = await M21Data.CreateAsync(manager);
        var bookingId = await data.BookingAsync(manager, 0);
        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant);
        var service = Accounting(accountant);
        var now = await new DbClock(accountant.Factory).GetUtcNowAsync(CancellationToken.None);
        var id = await service.RecordBankTransactionAsync(BankRow("M6-ADDITIONAL", Guid.NewGuid().ToString("N"), "Chuyển dư phí đổi", 300_000, now));
        await manager.MutateAsync("""
            INSERT dbo.BookingChangeRequests(BookingId,RequestType,NewCheckIn,NewCheckOut,Reason,AdditionalDeposit,Status,RequestedByUserId,RequestedAt)
            VALUES(@booking,'ChangeDates','2099-02-01','2099-02-03',N'Test',200000,'Pending',@user,SYSUTCDATETIME());
            DECLARE @change int=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.Payments(VoucherNo,BookingId,ChangeRequestId,BankTransactionId,Direction,Type,Method,Amount,Status,RecordedByUserId,RecordedAt,VerifiedByUserId,VerifiedAt)
            VALUES(@voucher,@booking,@change,@id,'In','AdditionalDeposit','BankTransfer',300000,'Verified',@user,SYSUTCDATETIME(),@user,SYSUTCDATETIME());
            UPDATE dbo.BankTransactions SET MatchStatus='Matched',MatchedBookingId=@booking,Note=N'Chuyển dư' WHERE Id=@id;
            """, c =>
        {
            c.Parameters.AddInt32("@booking", bookingId); c.Parameters.AddInt32("@id", id);
            c.Parameters.AddInt32("@user", accountant.Current.RequireSession().UserId);
            c.Parameters.AddVarChar("@voucher", 20, "M6" + Guid.NewGuid().ToString("N")[..18]);
        });
        var tx = await BankTransaction(service, id, now);
        Assert.Equal(100_000, Assert.Single(await service.ListPayablesAsync(), x => x.SourceType == "BankTransaction" && x.SourceId == id).Amount);

        var result = await service.RecordUnmatchedRefundAsync(new(id, tx.RowVer, "ADDITIONAL-REFUND", now, null));

        Assert.Equal(MatchStatus.Matched, result.MatchStatus);
        Assert.DoesNotContain(await service.ListPayablesAsync(), x => x.SourceType == "BankTransaction" && x.SourceId == id);
        Assert.Equal(100_000, await manager.ScalarAsync<decimal>("SELECT CONVERT(decimal(18,0),JSON_VALUE(NewValues,'$.Amount')) FROM dbo.AuditLogs WHERE TableName='BankTransactions' AND RecordId=@id AND ActionCode='BankTransaction.Refund';", c => c.Parameters.AddVarChar("@id", 50, id.ToString())));
    }
}
