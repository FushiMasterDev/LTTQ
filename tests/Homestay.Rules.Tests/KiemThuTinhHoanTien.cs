using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DTO.Availability;
using Homestay.DTO.Bookings;
using Homestay.DTO.Catalog;
using Homestay.DTO.Enums;

namespace Homestay.Rules.Tests;

public sealed class RefundCalculatorTests
{
    private static readonly DateOnly CheckIn = new(2026, 11, 10);
    private static readonly ApplicableCancellationPolicy Policy = new(11, null, "Chung", CancellationScope.All, false,
        [new(0, 0), new(7, 100), new(3, 50)]);
    private static RefundCalculationRequest Request(int days = 5) => new(CheckIn, CheckIn.AddDays(-days), 600000, true, Policy)
        { TotalAmount = 2000000, CommissionRate = .12m };

    // Exactly the four rows of spec 7.6.3 (the third row also covers NoShow separately below).
    [Theory]
    [InlineData(RefundReason.Completed, 0, 0, 600000, 240000, 360000)]
    [InlineData(RefundReason.CustomerCancellation, 5, 300000, 300000, 240000, 60000)]
    [InlineData(RefundReason.CustomerCancellation, 1, 0, 600000, 240000, 360000)]
    [InlineData(RefundReason.CustomerCancellation, 10, 600000, 0, 0, 0)]
    public void MatchesAllFourExampleRows(RefundReason reason, int days, int refund, int retained, int commission, int payout)
    {
        var result = RefundCalculator.Calculate(Request(days) with { Reason = reason });
        Assert.Equal(refund, result.RefundAmount); Assert.Equal(retained, result.RetainedAmount);
        Assert.Equal(commission, result.CommissionAmount); Assert.Equal(payout, result.OwnerPayoutAmount);
        Assert.Equal(retained, result.CommissionAmount + result.OwnerPayoutAmount);
    }
    [Theory]
    [InlineData(8, 100)] [InlineData(7, 100)] [InlineData(6, 50)] [InlineData(3, 50)]
    [InlineData(2, 0)] [InlineData(0, 0)] [InlineData(-1, 0)] [InlineData(-30, 0)]
    public void UsesLargestEligibleThresholdAndPreservesNegativeDays(int days, int percent)
    {
        var result = RefundCalculator.Calculate(Request(days));
        Assert.Equal(days, result.DaysBefore); Assert.Equal(percent, result.RefundPercent);
        Assert.Equal(PricingCalculator.RoundToThousand(600000m * percent / 100m), result.RefundAmount);
    }
    [Theory]
    [InlineData(RefundReason.OwnerRejected, 600123)]
    [InlineData(RefundReason.HomestayUnavailable, 600123)]
    [InlineData(RefundReason.NoShow, 0)]
    [InlineData(RefundReason.UnpaidCancellation, 0)]
    public void SpecialReasonsOverridePolicy(RefundReason reason, int refund)
    {
        var result = RefundCalculator.Calculate(Request(-2) with { Reason = reason, AmountDue = 600123 });
        Assert.Equal(refund, result.RefundAmount);
    }
    [Theory]
    [InlineData(RefundReason.CustomerCancellation)] [InlineData(RefundReason.OwnerRejected)]
    [InlineData(RefundReason.HomestayUnavailable)] [InlineData(RefundReason.NoShow)]
    public void UncollectedBookingHasNoRefundOrDistribution(RefundReason reason)
    {
        var result = RefundCalculator.Calculate(Request(10) with { HasCollectedDeposit = false, Reason = reason });
        Assert.Equal(0, result.RefundAmount); Assert.Equal(0, result.RetainedAmount);
        Assert.Equal(0, result.CommissionAmount); Assert.Equal(0, result.OwnerPayoutAmount);
    }
    [Fact]
    public void NoShowMatchesThirdExampleRowRegardlessOfHowEarlyItIsCalculated()
    {
        var result = RefundCalculator.Calculate(Request(10) with { Reason = RefundReason.NoShow });
        Assert.Equal(0, result.RefundAmount); Assert.Equal(600000, result.RetainedAmount);
        Assert.Equal(240000, result.CommissionAmount); Assert.Equal(360000, result.OwnerPayoutAmount);
    }
    [Fact]
    public void UsesSavedPrivateOrHolidayPolicyEvenWhenInactive()
    {
        var privatePolicy = Policy with { Id = 21, HomestayId = 3, Rules = [new(0, 0), new(3, 80)] };
        var holidayPolicy = privatePolicy with { Id = 22, Scope = CancellationScope.Holiday, Rules = [new(0, 0), new(3, 20)] };
        var normal = RefundCalculator.Calculate(Request());
        var own = RefundCalculator.Calculate(Request() with { Policy = privatePolicy });
        var holiday = RefundCalculator.Calculate(Request() with { Policy = holidayPolicy });
        Assert.Equal(300000, normal.RefundAmount); Assert.Equal(480000, own.RefundAmount); Assert.Equal(120000, holiday.RefundAmount);
        Assert.Equal(21, own.CancellationPolicyId); Assert.Equal(22, holiday.CancellationPolicyId);
    }
    [Theory]
    [InlineData(601000, 301000)] [InlineData(600999, 300000)] [InlineData(601001, 301000)]
    public void RefundUsesExistingThousandRounding(int due, int expected)
    {
        Assert.Equal(expected, RefundCalculator.Calculate(Request() with { AmountDue = due }).RefundAmount);
    }
    [Fact]
    public void CommissionUsesTotalAndIsCappedByRetainedAmount()
    {
        var result = RefundCalculator.Calculate(Request() with { AmountDue = 300000 });
        Assert.Equal(150000, result.RetainedAmount); Assert.Equal(150000, result.CommissionAmount); Assert.Equal(0, result.OwnerPayoutAmount);
        result = RefundCalculator.Calculate(Request() with { TotalAmount = 2000005 });
        Assert.Equal(240001, result.CommissionAmount);
        result = RefundCalculator.Calculate(Request() with { CommissionRate = null });
        Assert.Null(result.CommissionAmount); Assert.Null(result.OwnerPayoutAmount);
    }
    [Fact]
    public void RejectsInvalidInputAndInvalidSavedRules()
    {
        Assert.Throws<ArgumentNullException>(() => RefundCalculator.Calculate(null!));
        Assert.Throws<BusinessException>(() => RefundCalculator.Calculate(Request() with { AmountDue = -1 }));
        Assert.Throws<BusinessException>(() => RefundCalculator.Calculate(Request() with { CommissionRate = 1 }));
        Assert.Throws<BusinessException>(() => RefundCalculator.Calculate(Request() with { Reason = (RefundReason)999 }));
        Assert.Throws<BusinessException>(() => RefundCalculator.Calculate(Request() with { Policy = Policy with { Rules = [new CancellationRuleItem(1, 50)] } }));
    }
}
