using Homestay.BLL.Infrastructure;
using Homestay.BLL.Security;
using Homestay.BLL.Services;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Bookings;
using Homestay.DTO.Enums;
using Homestay.DTO.Finance;
using Homestay.DTO.Payments;

namespace Homestay.BLL.Tests;

[Collection("M23 SQL")]
public sealed class M6AccountingContractSqlTests
{
    private static WriteOperationRunner Writes(M21TestClient c) =>
        new(c.Factory, c.Current, new WriteSessionValidator(new AuthRepository()));

    private static AccountingService Accounting(M21TestClient c) => new(new Authorizer(c.Current), c.Current,
        c.Factory, Writes(c), new FinanceRepository(), new BookingRepository(), c.Bookings,
        new DbClock(c.Factory), new SettingsProvider(c.Factory), new AuditWriter(), c.Login.Machine);

    private static SettlementService Settlements(M21TestClient c) => new(new Authorizer(c.Current), c.Current,
        c.Factory, Writes(c), new SettlementRepository(), new DbClock(c.Factory), new SettingsProvider(c.Factory),
        new AuditWriter(), c.Login.Machine);

    private static Task<BookingDetail> CreateBooking(M21TestClient c, M21Data data, int? roomId = null) => c.Bookings.CreateAsync(new()
    {
        CustomerId = data.CustomerId, RoomTypeId = data.RoomTypeId, RoomId = roomId, CheckIn = M21Data.CheckIn,
        CheckOut = M21Data.CheckOut, Guests = 2, ContactName = "Khách M6 hợp đồng",
        ContactPhone = M14TestClient.Phone(), SourceChannel = SourceChannel.Phone
    });

    private static Task<DateTime> Now(M21TestClient c) => new DbClock(c.Factory).GetUtcNowAsync(CancellationToken.None);

    private static Task<int> RecordBank(AccountingService service, string code, decimal amount, DateTime now) =>
        service.RecordBankTransactionAsync(new("M6-CONTRACT", Guid.NewGuid().ToString("N"), amount,
            code, "Khách M6", null, now));

    private static async Task<BankTransactionItem> Bank(AccountingService service, int id, DateTime now) =>
        Assert.Single(await service.ListBankTransactionsAsync(now.AddMinutes(-1), now.AddMinutes(1), null, null), x => x.Id == id);

    [SqlFact]
    public Task ManualMatchAuditsOldAndNewAmountOfExistingPendingReceipt() => ExistingReceiptAudit(false);

    [SqlFact]
    public Task AutoMatchAuditsOldAndNewAmountOfExistingPendingReceipt() => ExistingReceiptAudit(true);

    private static async Task ExistingReceiptAudit(bool automatic)
    {
        var manager = new M21TestClient(); await manager.LoginNewAsync();
        var data = await M21Data.CreateAsync(manager);
        var booking = await CreateBooking(manager, data);
        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant);
        var now = await Now(accountant);
        var originalAmount = booking.AmountDue - 100_000;
        var bankAmount = booking.AmountDue + 100_000;
        // Same accountant records and verifies: allowed because the verification attaches BankTransactionId (5.3).
        var recorded = await accountant.Payments.RecordReceiptAsync(new RecordReceiptRequest
        {
            BookingId = booking.Booking.Id, BookingCode = booking.Booking.Code, RowVer = booking.Booking.RowVer,
            Amount = originalAmount, Method = PaymentMethod.BankTransfer, TransferredAtUtc = now,
            TransferContent = booking.Booking.Code
        });
        var service = Accounting(accountant);
        var id = await RecordBank(service, booking.Booking.Code, bankAmount, now);
        var tx = await Bank(service, id, now);
        if (automatic) await service.AutoMatchAsync(now.AddMilliseconds(-1), now.AddMilliseconds(1));
        else await service.MatchBankTransactionAsync(new(id, booking.Booking.Id, tx.RowVer));

        var matched = await Bank(service, id, now);
        Assert.Equal(automatic ? MatchStatus.Matched : MatchStatus.ManuallyMatched, matched.MatchStatus);
        Assert.Equal(BookingStatus.Deposited, (await accountant.Bookings.GetAsync(booking.Booking.Id)).Booking.Status);
        Assert.Equal(1, await manager.ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.Payments WHERE BookingId=@booking AND Id=@payment AND BankTransactionId=@bank
              AND Status='Verified' AND Amount=@amount AND RecordedByUserId=@user AND VerifiedByUserId=@user;
            """, c =>
        {
            c.Parameters.AddInt32("@booking", booking.Booking.Id); c.Parameters.AddInt32("@payment", recorded.Receipt.Id);
            c.Parameters.AddInt32("@bank", id); c.Parameters.AddMoney("@amount", bankAmount);
            c.Parameters.AddInt32("@user", accountant.Current.RequireSession().UserId);
        }));
        Assert.Equal(1, await manager.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.Payments WHERE BookingId=@id AND Type='Deposit';",
            c => c.Parameters.AddInt32("@id", booking.Booking.Id)));
        Assert.Equal(1, await manager.ScalarAsync<int>("""
            SELECT COUNT(*) FROM dbo.AuditLogs WHERE TableName='BankTransactions' AND RecordId=@id AND ActionCode=@action
              AND JSON_VALUE(OldValues,'$.Status')='Unmatched'
              AND CONVERT(int,JSON_VALUE(OldValues,'$.PaymentId'))=@payment
              AND CONVERT(decimal(18,0),JSON_VALUE(OldValues,'$.PaymentAmount'))=@old
              AND JSON_VALUE(NewValues,'$.Status')=@status
              AND CONVERT(int,JSON_VALUE(NewValues,'$.PaymentId'))=@payment
              AND CONVERT(decimal(18,0),JSON_VALUE(NewValues,'$.PaymentAmount'))=@new
              AND CONVERT(decimal(18,0),JSON_VALUE(NewValues,'$.Amount'))=@new;
            """, c =>
        {
            c.Parameters.AddVarChar("@id", 50, id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            c.Parameters.AddVarChar("@action", 100, automatic ? "BankTransaction.AutoMatch" : "BankTransaction.ManualMatch");
            c.Parameters.AddInt32("@payment", recorded.Receipt.Id); c.Parameters.AddMoney("@old", originalAmount);
            c.Parameters.AddMoney("@new", bankAmount); c.Parameters.AddVarChar("@status", 20, matched.MatchStatus.ToString());
        }));
        Assert.Equal(100_000, Assert.Single(await service.ListPayablesAsync(),
            x => x.SourceType == "BankTransaction" && x.SourceId == id).Amount);
    }

    [SqlFact]
    public async Task SamplePeriodMatchesHandLedgerFromDraftThroughPaidAndBothFinanceViews()
    {
        // Chapter 3 hand ledger, VND; every booking Total=2000000, Deposit=600000, rate=.12, discount=0.
        // Row             Refund   Fee    p   LineDeposit Retained Commission Discount Payout
        // Completed            0      0   0       600000   600000     240000        0 360000
        // NoShow               0      0   0       600000   600000     240000        0 360000
        // Cancelled       300000      0  50       600000   300000     240000        0  60000
        // Rejected        600000 200000 100       800000   200000      24000        0 176000
        // CheckedIn -> Completed: 0   0   0       600000   600000     240000        0 360000
        // Draft (4 rows): room=8000000, retained=1700000, commission=744000, discount=0, payout=956000.
        // Final (5 rows): room=10000000,retained=2300000, commission=984000, discount=0,payout=1316000.
        // Cash: deposits=3000000 + fee=200000; refunds=900000; payout=1316000 => balance=984000.
        var manager = new M21TestClient(); await manager.LoginNewAsync();
        var data = await M21Data.CreateAsync(manager, 5); // Own home, own .12 contract and own policy (50% at 3 days).
        var accountant = new M21TestClient(); await accountant.LoginNewAsync(Position.Accountant);
        var reviewer = await manager.Login.CreateIdentityAsync(Position.Manager);
        var accounting = Accounting(accountant); var settlements = Settlements(accountant);
        var now = await Now(accountant);
        // Own fixture prices only: all day types 1000000 => two nights exactly 2000000.
        await manager.MutateAsync("""
            UPDATE dbo.RoomTypePrices SET Price=1000000 WHERE RoomTypeId=@type;
            UPDATE o SET BankName=N'MB Bank',BankAccountNo='TEST-M6-OWNER',BankAccountName=N'CHU M6',
                Status='Approved',ApprovedByEmployeeId=@employee,ApprovedAt=SYSUTCDATETIME()
            FROM dbo.Owners o JOIN dbo.Homestays h ON h.OwnerId=o.Id WHERE h.Id=@home;
            """, c =>
        {
            c.Parameters.AddInt32("@type", data.RoomTypeId); c.Parameters.AddInt32("@home", data.HomestayId);
            c.Parameters.AddInt32("@employee", manager.Current.RequireSession().EmployeeId);
        });
        BookingStatus[] statuses = [BookingStatus.Completed, BookingStatus.NoShow, BookingStatus.Cancelled,
            BookingStatus.Rejected, BookingStatus.CheckedIn];
        var bookings = new List<BookingDetail>();
        for (var i = 0; i < statuses.Length; i++)
        {
            // Real BLL creates booking, records bank line, matches and verifies deposit.
            var booking = await CreateBooking(manager, data, data.Rooms[i]);
            Assert.Equal(2_000_000, booking.TotalAmount); Assert.Equal(600_000, booking.AmountDue);
            Assert.Equal(.12m, booking.CommissionRate);
            var bank = await RecordBank(accounting, booking.Booking.Code, 600_000, now);
            await accounting.MatchBankTransactionAsync(new(bank, booking.Booking.Id, (await Bank(accounting, bank, now)).RowVer));
            bookings.Add(booking);
            // Historical states/dates are fixture SQL: cannot backdate BLL checkout/no-show to January 2025.
            // Shift both night tables too, keeping the room ledger consistent with the booking dates.
            await manager.MutateAsync("""
                DECLARE @delta int=DATEDIFF(day,'2099-01-01','2025-01-05');
                UPDATE dbo.RoomNights SET [Date]=DATEADD(day,@delta,[Date]) WHERE BookingId=@id;
                UPDATE dbo.BookingNights SET [Date]=DATEADD(day,@delta,[Date]) WHERE BookingId=@id;
                UPDATE dbo.Bookings SET CheckIn='2025-01-05',CheckOut='2025-01-07',Status=@status,
                    ConfirmedAt=CASE WHEN @status IN ('Completed','NoShow','CheckedIn') THEN CONVERT(datetime2,'2025-01-02') END,
                    CheckedInAt=CASE WHEN @status IN ('Completed','CheckedIn') THEN CONVERT(datetime2,'2025-01-05') END,
                    CompletedAt=CASE WHEN @status='Completed' THEN CONVERT(datetime2,'2025-01-07') END,
                    CancelledAt=CASE WHEN @status='Cancelled' THEN CONVERT(datetime2,'2025-01-02') END,
                    CancelReason=CASE WHEN @status IN ('Cancelled','Rejected') THEN N'Dữ liệu kỳ mẫu M6' END,
                    RefundAmount=CASE WHEN @status='Cancelled' THEN 300000 WHEN @status='Rejected' THEN 600000 ELSE 0 END,
                    ChangeFeeAmount=CASE WHEN @status='Rejected' THEN 200000 ELSE 0 END WHERE Id=@id;
                DELETE dbo.RoomNights WHERE BookingId=@id AND @status IN ('Cancelled','Rejected','NoShow');
                """, c => { c.Parameters.AddInt32("@id", booking.Booking.Id); c.Parameters.AddVarChar("@status", 20, statuses[i].ToString()); });
        }
        var cancelled = bookings[2].Booking.Id; var rejected = bookings[3].Booking.Id;
        // SQL snapshots of historical refund/fee vouchers; no claim that the change lifecycle ran through BLL.
        // Three different real identities satisfy change actor separation; incoming fee has distinct recorder/verifier.
        await manager.MutateAsync("""
            INSERT dbo.BookingChangeRequests(BookingId,RequestType,NewCheckIn,NewCheckOut,Reason,ChangeFee,
                AdditionalDeposit,Status,RequestedByUserId,RequestedAt,DecidedByUserId,DecidedAt,FinanceConfirmedByUserId,FinanceConfirmedAt)
            VALUES(@rejected,'ChangeDates','2025-01-05','2025-01-07',N'Phí đổi đã thu trước khi từ chối',200000,
                200000,'Applied',@recorder,SYSUTCDATETIME(),@reviewer,SYSUTCDATETIME(),@accountant,SYSUTCDATETIME());
            DECLARE @change int=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.Payments(VoucherNo,BookingId,ChangeRequestId,Direction,Type,Method,Amount,Status,
                RecordedByUserId,RecordedAt,VerifiedByUserId,VerifiedAt)
            VALUES(@feeVoucher,@rejected,@change,'In','AdditionalDeposit','BankTransfer',200000,'Verified',
                @recorder,SYSUTCDATETIME(),@accountant,SYSUTCDATETIME());
            INSERT dbo.Payments(VoucherNo,BookingId,Direction,Type,Method,Amount,Status,RecordedByUserId,
                RecordedAt,VerifiedByUserId,VerifiedAt,BankRef)
            VALUES(@cancelVoucher,@cancelled,'Out','Refund','BankTransfer',300000,'Verified',@recorder,
                SYSUTCDATETIME(),@accountant,SYSUTCDATETIME(),'M6-CANCEL-REFUND'),
                (@rejectVoucher,@rejected,'Out','Refund','BankTransfer',600000,'Verified',@recorder,
                SYSUTCDATETIME(),@accountant,SYSUTCDATETIME(),'M6-REJECT-REFUND');
            """, c =>
        {
            c.Parameters.AddInt32("@cancelled", cancelled); c.Parameters.AddInt32("@rejected", rejected);
            c.Parameters.AddInt32("@recorder", manager.Current.RequireSession().UserId);
            c.Parameters.AddInt32("@reviewer", reviewer.UserId); c.Parameters.AddInt32("@accountant", accountant.Current.RequireSession().UserId);
            foreach (var name in new[] { "@feeVoucher", "@cancelVoucher", "@rejectVoucher" })
                c.Parameters.AddVarChar(name, 20, "M6" + Guid.NewGuid().ToString("N")[..18]);
        });

        // Expected rows are literal hand-ledger constants, independent of SettlementCalculator.
        SettlementLine[] expected =
        [
            Expected(bookings[0], SettlementItemType.Completed, 600_000, 0, 600_000, 240_000, 360_000),
            Expected(bookings[1], SettlementItemType.NoShow, 600_000, 0, 600_000, 240_000, 360_000),
            Expected(bookings[2], SettlementItemType.CancelledRetained, 600_000, 300_000, 300_000, 240_000, 60_000),
            Expected(bookings[3], SettlementItemType.FeeOnly, 800_000, 600_000, 200_000, 24_000, 176_000),
            Expected(bookings[4], SettlementItemType.Completed, 600_000, 0, 600_000, 240_000, 360_000)
        ];
        var draft = await settlements.CreateOrRecreateDraftAsync(data.HomestayId, 2025, 1, 1);
        Assert.Equal(SettlementStatus.Draft, draft.Status);
        AssertLines(expected.Take(4), draft.Items);
        Assert.Equal(new SettlementTotals(8_000_000, 1_700_000, 744_000, 0, 956_000), draft.Totals);
        Assert.Equal(bookings[4].Booking.Code, Assert.Single(draft.UnfinalizedBookingCodes));
        var blocked = await Assert.ThrowsAsync<BusinessException>(() => settlements.SendAsync(new(draft.Id, draft.RowVer, "")));
        Assert.Equal("Còn đơn chưa chốt trong kỳ: " + bookings[4].Booking.Code, blocked.Message);
        Assert.Equal(draft.RowVer, (await settlements.GetAsync(draft.Id)).RowVer);
        await manager.MutateAsync("UPDATE dbo.Bookings SET Status='Completed',CompletedAt='2025-01-07' WHERE Id=@id;",
            c => c.Parameters.AddInt32("@id", bookings[4].Booking.Id));
        var rebuilt = await settlements.CreateOrRecreateDraftAsync(data.HomestayId, 2025, 1, 1);
        Assert.Equal(draft.Id, rebuilt.Id); Assert.False(draft.RowVer.SequenceEqual(rebuilt.RowVer));
        Assert.Empty(rebuilt.UnfinalizedBookingCodes); AssertLines(expected, rebuilt.Items);
        Assert.Equal(new SettlementTotals(10_000_000, 2_300_000, 984_000, 0, 1_316_000), rebuilt.Totals);
        var sent = await settlements.SendAsync(new(rebuilt.Id, rebuilt.RowVer, ""));
        Assert.Equal(SettlementStatus.Sent, sent.Status);
        Assert.Equal(1_316_000, await manager.ScalarAsync<decimal>(
            "SELECT Amount FROM dbo.vw_Payables WHERE SourceType='Settlement' AND SourceId=@id;", c => c.Parameters.AddInt32("@id", sent.Id)));
        Assert.Equal(0, await SettlementCashCount(manager, sent.Id));
        var feedback = await settlements.RecordOwnerFeedbackAsync(new(sent.Id, sent.RowVer, "Chủ đã đối chiếu đủ 5 đơn qua điện thoại."));
        Assert.Equal(SettlementStatus.Sent, feedback.Status);
        Assert.Equal("Chủ đã đối chiếu đủ 5 đơn qua điện thoại.", feedback.OwnerFeedback);
        // D9/D12: reopen Sent with a reason, then recreate the same header and send the new snapshot.
        var reopened = await settlements.ReopenAsync(new(feedback.Id, feedback.RowVer, "Chủ đề nghị kiểm tra lại bảng."));
        Assert.Equal(SettlementStatus.Draft, reopened.Status); AssertLines(expected, reopened.Items);
        Assert.Equal(feedback.OwnerFeedback, reopened.OwnerFeedback);
        Assert.Equal(0, await manager.ScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.vw_Payables WHERE SourceType='Settlement' AND SourceId=@id;", c => c.Parameters.AddInt32("@id", reopened.Id)));
        var recalculated = await settlements.CreateOrRecreateDraftAsync(data.HomestayId, 2025, 1, 1);
        Assert.Equal(rebuilt.Id, recalculated.Id); Assert.Equal(rebuilt.Totals, recalculated.Totals);
        AssertLines(expected, recalculated.Items);
        var resent = await settlements.SendAsync(new(recalculated.Id, recalculated.RowVer, ""));
        const string reference = "M6-SAMPLE-PAYOUT";
        var paid = await settlements.MarkPaidAsync(new(resent.Id, resent.RowVer, reference));
        Assert.Equal(SettlementStatus.Paid, paid.Status); AssertLines(expected, paid.Items);
        Assert.Equal(rebuilt.Totals, paid.Totals);
        Assert.Equal(0, await manager.ScalarAsync<int>("SELECT COUNT(*) FROM dbo.vw_Payables WHERE HomestayId=@home;",
            c => c.Parameters.AddInt32("@home", data.HomestayId)));
        Assert.Equal(1, await SettlementCashCount(manager, paid.Id));
        var cash = await accounting.ListCashBookAsync(now.AddMinutes(-1), (await Now(accountant)).AddMinutes(1));
        var payout = Assert.Single(cash, x => x.SourceType == "Settlement" && x.SourceId == paid.Id);
        Assert.Equal(reference, payout.ReferenceNo); Assert.Equal("OwnerPayout", payout.EntryType);
        Assert.Equal(0, payout.IncomeAmount); Assert.Equal(1_316_000, payout.ExpenseAmount);
        Assert.Equal(data.HomestayId, payout.HomestayId);
        // Check entire own-home ledger directly in the view, independently of list TOP limits.
        Assert.Equal(3_200_000, await CashTotal(manager, data.HomestayId, true));
        Assert.Equal(2_216_000, await CashTotal(manager, data.HomestayId, false));
        Assert.DoesNotContain(await accounting.ListPayablesAsync(), x => x.HomestayId == data.HomestayId);
        await Assert.ThrowsAsync<BusinessException>(() => settlements.ReopenAsync(new(paid.Id, paid.RowVer, "Không mở Paid")));
        Assert.Contains(paid.History, x => x.Action == "Settlement.Recreate");
        Assert.Contains(paid.History, x => x.Action == "Settlement.Reopen" && x.Note == "Chủ đề nghị kiểm tra lại bảng.");
        Assert.Contains(paid.History, x => x.Action == "Settlement.Send");
        Assert.Contains(paid.History, x => x.Action == "Settlement.OwnerFeedback");
        Assert.Contains(paid.History, x => x.Action == "Settlement.Pay");
    }

    private static SettlementLine Expected(BookingDetail b, SettlementItemType type, decimal deposit,
        decimal refund, decimal retained, decimal commission, decimal payout) =>
        new(b.Booking.Id, b.Booking.Code, type, 2_000_000, deposit, refund, retained, commission, 0, payout, .12m)
        { CheckOut = new(2025, 1, 7) };

    private static void AssertLines(IEnumerable<SettlementLine> expected, IReadOnlyList<SettlementLine> actual)
        => Assert.Equal(expected.OrderBy(x => x.BookingId).ToArray(), actual.OrderBy(x => x.BookingId).ToArray());

    private static Task<int> SettlementCashCount(M21TestClient c, int id) => c.ScalarAsync<int>(
        "SELECT COUNT(*) FROM dbo.vw_CashBook WHERE SourceType='Settlement' AND SourceId=@id;", p => p.Parameters.AddInt32("@id", id));

    private static Task<decimal> CashTotal(M21TestClient c, int home, bool income) => c.ScalarAsync<decimal>(
        income ? "SELECT SUM(IncomeAmount) FROM dbo.vw_CashBook WHERE HomestayId=@home;"
            : "SELECT SUM(ExpenseAmount) FROM dbo.vw_CashBook WHERE HomestayId=@home;", p => p.Parameters.AddInt32("@home", home));
}
