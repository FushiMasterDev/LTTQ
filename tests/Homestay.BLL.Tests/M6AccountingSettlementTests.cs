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
}
