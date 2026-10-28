using Homestay.BLL.Rules;
using Homestay.DTO.Enums;
using Homestay.DTO.Finance;

namespace Homestay.Rules.Tests;

/// <summary>Kiểm thử công thức đối soát M6 với các trường hợp hoàn tất, không đến, có voucher.</summary>
public sealed class M6SettlementFormulaTests
{
    private static SettlementCalculationInput Booking(BookingStatus status = BookingStatus.Completed) =>
        new(6, "FJ261027-006", status, new(2025, 1, 10), false,
            2_000_000, 600_000, 600_000, null, 0, 0, .12m, true);

    // Expected values are hand calculated, never derived by calling production rounding/calculation code.
    private static void Line(SettlementCalculationInput input, SettlementItemType type, decimal deposit,
        decimal refund, decimal retained, decimal commission, decimal discount, decimal payout)
        => Assert.Equal(new SettlementLine(input.BookingId, input.BookingCode, type, input.TotalAmount,
            deposit, refund, retained, commission, discount, payout, input.CommissionRate)
            { CheckOut = input.CheckOut }, SettlementCalculator.Calculate(input));

    [Fact]
    public void CompletedWithVoucherRetainsGrossDepositAndShowsFujiFundedDiscount()
    {
        // refund=null => 0; p=0; base=600000; commission=2000000*.12=240000;
        // discount=50000; payout=600000-240000=360000; Fuji net=240000-50000=190000.
        Line(Booking() with { AmountDue = 550_000, DiscountAmount = 50_000 },
            SettlementItemType.Completed, 600_000, 0, 600_000, 240_000, 50_000, 360_000);
    }

    [Fact]
    public void NoShowWithZeroRefundRetainsDeposit()
    {
        // p=0; retained=600000; commission=240000; payout=360000.
        Line(Booking(BookingStatus.NoShow) with { RefundAmount = 0 },
            SettlementItemType.NoShow, 600_000, 0, 600_000, 240_000, 0, 360_000);
    }

    [Fact]
    public void CancelledHalfRefundProratesVoucher()
    {
        // Due=550000, refund=275000 => p=50; base=600000-300000=300000;
        // discount=50000-25000=25000; commission=240000; payout=60000.
        Line(Booking(BookingStatus.Cancelled) with { AmountDue = 550_000, RefundAmount = 275_000, DiscountAmount = 50_000 },
            SettlementItemType.CancelledRetained, 600_000, 275_000, 300_000, 240_000, 25_000, 60_000);
    }

    [Fact]
    public void CancelledRoundsPercentageAwayFromZeroBeforeRoundingDepositAndDiscountToThousands()
    {
        // 100*298455/591000=50.5 => p=51 (not 50); 601000*.51=306510 => 307000;
        // base=294000; 10000*.51=5100 => 5000; discount=5000; payout=54000.
        Line(Booking(BookingStatus.Cancelled) with { DepositAmount = 601_000, AmountDue = 591_000,
            RefundAmount = 298_455, DiscountAmount = 10_000 },
            SettlementItemType.CancelledRetained, 601_000, 298_455, 294_000, 240_000, 5_000, 54_000);
    }

    [Fact]
    public void CancelledFullRefundWithoutFeeHasNoLine()
    {
        // p=100; base=600000-600000=0; retained=0 => no line.
        Assert.Null(SettlementCalculator.Calculate(Booking(BookingStatus.Cancelled) with { RefundAmount = 600_000 }));
    }

    [Fact]
    public void CancelledRetainsChangeFeeInAdditionToUnrefundedDeposit()
    {
        // p=50; base=300000; fee=200000; baseCommission=240000; feeCommission=24000;
        // retained=500000; commission=264000; payout=236000; line deposit=800000.
        Line(Booking(BookingStatus.Cancelled) with { RefundAmount = 300_000, ChangeFeeAmount = 200_000 },
            SettlementItemType.CancelledRetained, 800_000, 300_000, 500_000, 264_000, 0, 236_000);
    }

    [Fact]
    public void CancelledFullRefundStillRetainsChangeFee()
    {
        // p=100; base=0; retained=fee=200000; commission=24000; payout=176000.
        Line(Booking(BookingStatus.Cancelled) with { RefundAmount = 600_000, ChangeFeeAmount = 200_000 },
            SettlementItemType.CancelledRetained, 800_000, 600_000, 200_000, 24_000, 0, 176_000);
    }

    [Theory]
    [InlineData(BookingStatus.Rejected)]
    [InlineData(BookingStatus.Expired)]
    public void FeeOnlyExcludesBaseDepositAndVoucher(BookingStatus status)
    {
        // feeOnly => base=0, discount=0; 200000*.12=24000; payout=176000.
        Line(Booking(status) with { AmountDue = 550_000, RefundAmount = 550_000,
            DiscountAmount = 50_000, ChangeFeeAmount = 200_000 },
            SettlementItemType.FeeOnly, 800_000, 550_000, 200_000, 24_000, 0, 176_000);
    }

    [Theory]
    [InlineData(BookingStatus.Rejected)]
    [InlineData(BookingStatus.Expired)]
    public void FeeOnlyWithoutFeeHasNoLine(BookingStatus status)
    {
        // base=0; fee=0 => retained=0, even though booking snapshot has deposit=600000.
        Assert.Null(SettlementCalculator.Calculate(Booking(status)));
    }

    [Fact]
    public void ReplacedBookingHasNoLineEvenWithRetainedFundsAndFee()
    {
        // ReplacedByBookingId is mapped to Replaced: all 600000+200000 are excluded.
        Assert.Null(SettlementCalculator.Calculate(Booking() with { Replaced = true, ChangeFeeAmount = 200_000 }));
    }

    [Theory]
    [InlineData(BookingStatus.AwaitingPayment)]
    [InlineData(BookingStatus.PaymentSubmitted)]
    [InlineData(BookingStatus.Deposited)]
    [InlineData(BookingStatus.Confirmed)]
    [InlineData(BookingStatus.CheckedIn)]
    public void UnfinalizedBookingHasNoLine(BookingStatus status)
    {
        // Neither deposit=600000 nor fee=200000 admits an unfinalized booking.
        Assert.Null(SettlementCalculator.Calculate(Booking(status) with { ChangeFeeAmount = 200_000 }));
    }

    [Fact]
    public void BaseCommissionCannotExceedRetainedDeposit()
    {
        // refund=480000 => p=80; retained=120000; min(240000,120000)=120000; payout=0.
        Line(Booking(BookingStatus.Cancelled) with { RefundAmount = 480_000 },
            SettlementItemType.CancelledRetained, 600_000, 480_000, 120_000, 120_000, 0, 0);
    }

    [Theory]
    [InlineData(.10, 200_000, 400_000)]
    [InlineData(.15, 300_000, 300_000)]
    public void CommissionUsesBookingRateAtBothContractLimits(decimal rate, decimal commission, decimal payout)
    {
        // 2000000*.10=200000 / *.15=300000; payout=600000-commission.
        Line(Booking() with { CommissionRate = rate }, SettlementItemType.Completed,
            600_000, 0, 600_000, commission, 0, payout);
    }

    [Theory]
    [InlineData(1_000_005, .10, 100_001, 199_999)]
    [InlineData(1_000_010, .15, 150_002, 149_998)]
    public void BaseCommissionMidpointRoundsAwayFromZero(decimal room, decimal rate, decimal commission, decimal payout)
    {
        // 1000005*.10=100000.5=>100001; 1000010*.15=150001.5=>150002.
        Line(Booking() with { TotalAmount = room, DepositAmount = 300_000, AmountDue = 300_000, CommissionRate = rate },
            SettlementItemType.Completed, 300_000, 0, 300_000, commission, 0, payout);
    }

    [Fact]
    public void FeeCommissionMidpointRoundsAwayFromZero()
    {
        // fee=5; 5*.10=.5=>1; retained=5; payout=4; line deposit=600005.
        Line(Booking(BookingStatus.Expired) with { ChangeFeeAmount = 5, CommissionRate = .10m },
            SettlementItemType.FeeOnly, 600_005, 0, 5, 1, 0, 4);
    }

    [Theory]
    [InlineData(600_999, 300_499, 300_999)]
    [InlineData(601_000, 300_500, 300_000)]
    [InlineData(601_001, 300_500, 300_001)]
    public void RetainedDepositRoundsAtThousandMidpoint(decimal deposit, decimal refund, decimal retained)
    {
        // p rounds to 50 in all three cases; half-deposit=300499.5/300500/300500.5
        // rounds to 300000/301000/301000 => base=300999/300000/300001.
        Line(Booking(BookingStatus.Cancelled) with { DepositAmount = deposit, AmountDue = deposit, RefundAmount = refund },
            SettlementItemType.CancelledRetained, deposit, refund, retained, 240_000, 0, retained - 240_000);
    }

    [Fact]
    public void DiscountMidpointRoundsToThousandAwayFromZero()
    {
        // p=50; 1000*.5=500=>1000; discount=max(0,1000-1000)=0; base=300000.
        Line(Booking(BookingStatus.Cancelled) with { AmountDue = 599_000, RefundAmount = 299_500, DiscountAmount = 1_000 },
            SettlementItemType.CancelledRetained, 600_000, 299_500, 300_000, 240_000, 0, 60_000);
    }

    [Fact]
    public void DiscountIsClampedToZeroWhenThousandRoundingExceedsVoucher()
    {
        // p=100; base=0; discount=max(0,600-RoundToThousand(600))=max(0,-400)=0.
        // Fee keeps a line: retained=200000; commission=24000; payout=176000.
        Line(Booking(BookingStatus.Cancelled) with { AmountDue = 599_400, RefundAmount = 599_400,
            DiscountAmount = 600, ChangeFeeAmount = 200_000 },
            SettlementItemType.CancelledRetained, 800_000, 599_400, 200_000, 24_000, 0, 176_000);
    }

    [Fact]
    public void NegativeRetainedFromThousandRoundingHasNoLine()
    {
        // p=100; base=600999-RoundToThousand(600999)=600999-601000=-1 <= 0.
        Assert.Null(SettlementCalculator.Calculate(Booking(BookingStatus.Cancelled) with
            { DepositAmount = 600_999, AmountDue = 600_999, RefundAmount = 600_999 }));
    }

    [Fact]
    public void ZeroAmountDueUsesZeroRefundPercentage()
    {
        // AmountDue=0 => p=0 without division; base=600000; payout=360000.
        Line(Booking() with { AmountDue = 0 }, SettlementItemType.Completed,
            600_000, 0, 600_000, 240_000, 0, 360_000);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(200_000)]
    public void CancelledWithoutVerifiedDepositUsesImplementationFeeOnlyExtension(decimal fee)
    {
        // Outside literal spec feeOnly definition: unpaid cancellation ignores the 600000 snapshot.
        // fee=0=>no line; fee=200000=>commission=24000, payout=176000, line deposit=200000.
        var input = Booking(BookingStatus.Cancelled) with { HasVerifiedDeposit = false,
            AmountDue = 550_000, DiscountAmount = 50_000, ChangeFeeAmount = fee };
        if (fee == 0) Assert.Null(SettlementCalculator.Calculate(input));
        else Line(input, SettlementItemType.FeeOnly, 200_000, 0, 200_000, 24_000, 0, 176_000);
    }
}
