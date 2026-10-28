using Homestay.BLL.Infrastructure;
using Homestay.DAL.Infrastructure;

namespace Homestay.BLL.Tests;

public sealed class SettingsProviderTests
{
    [SqlFact]
    public async Task ReadsEverySeedKeyThroughItsTypedGetterWithoutCaching()
    {
        var factory = SqlFixture.Factory();
        var provider = new SettingsProvider(factory);
        await using var uow = await UnitOfWork.BeginAsync(factory, SqlFixture.Token);
        var values = await provider.GetAllAsync(SqlFixture.Token, uow);
        foreach (var key in SettingsProvider.Keys)
        {
            Assert.True(values.ContainsKey(key), key);
            SettingsProvider.Validate(key, values[key]);
        }
        _ = await provider.GetBookingHoldMinutesAsync(SqlFixture.Token, uow);
        _ = await provider.GetBookingConfirmMinutesAsync(SqlFixture.Token, uow);
        _ = await provider.GetBookingDepositRateAsync(SqlFixture.Token, uow);
        _ = await provider.GetBookingMaxAwaitingPerCustomerAsync(SqlFixture.Token, uow);
        _ = await provider.GetBookingMaxReceiptRejectionsAsync(SqlFixture.Token, uow);
        _ = await provider.GetBookingMaxNightsAsync(SqlFixture.Token, uow);
        _ = await provider.GetChangeMinDaysBeforeAsync(SqlFixture.Token, uow);
        _ = await provider.GetChangeMaxPerBookingAsync(SqlFixture.Token, uow);
        _ = await provider.GetChangeWindowDaysAsync(SqlFixture.Token, uow);
        _ = await provider.GetContractMinCommissionAsync(SqlFixture.Token, uow);
        _ = await provider.GetContractMaxCommissionAsync(SqlFixture.Token, uow);
        _ = await provider.GetSettlementGraceDaysAsync(SqlFixture.Token, uow);
        _ = await provider.GetVoucherIssueRateAsync(SqlFixture.Token, uow);
        _ = await provider.GetVoucherMinAmountAsync(SqlFixture.Token, uow);
        _ = await provider.GetVoucherMaxAmountAsync(SqlFixture.Token, uow);
        _ = await provider.GetVoucherValidMonthsAsync(SqlFixture.Token, uow);
        _ = await provider.GetVoucherMaxShareOfCommissionAsync(SqlFixture.Token, uow);
        _ = await provider.GetPaymentReviewAlertWorkingHoursAsync(SqlFixture.Token, uow);
        _ = await provider.GetSessionIdleMinutesAsync(SqlFixture.Token, uow);
        _ = await provider.GetLoginMaxFailedAsync(SqlFixture.Token, uow);
        _ = await provider.GetLoginLockoutMinutesAsync(SqlFixture.Token, uow);
        _ = await provider.GetFujiBankNameAsync(SqlFixture.Token, uow);
        _ = await provider.GetFujiBankAccountNoAsync(SqlFixture.Token, uow);
        _ = await provider.GetFujiBankAccountNameAsync(SqlFixture.Token, uow);
        _ = await provider.GetWorkingHoursStartAsync(SqlFixture.Token, uow);
        _ = await provider.GetWorkingHoursEndAsync(SqlFixture.Token, uow);
        using var update = uow.CreateCommand("UPDATE dbo.SystemSettings SET SettingValue=@value WHERE SettingKey='Booking.HoldMinutes';");
        var value = update.Parameters.AddNVarChar("@value", 500, "5");
        await uow.ExecuteAsync(update, SqlFixture.Token);
        Assert.Equal(5, await provider.GetBookingHoldMinutesAsync(SqlFixture.Token, uow));
        value.Value = "120";
        await uow.ExecuteAsync(update, SqlFixture.Token);
        Assert.Equal(120, await provider.GetBookingHoldMinutesAsync(SqlFixture.Token, uow));
    }
}
