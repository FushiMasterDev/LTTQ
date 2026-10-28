using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DTO.Availability;
using Homestay.DTO.Catalog;
using Homestay.DTO.Enums;

namespace Homestay.Rules.Tests;

public sealed class PricingCalculatorTests
{
    private static readonly DateOnly Start = new(2026, 10, 26); // Thứ Hai.
    private static RoomPriceVersion[] Prices() => [new(DayType.Weekday, Start, 1_000_000), new(DayType.Weekend, Start, 1_200_000), new(DayType.Holiday, Start, 1_500_000)];

    [Theory]
    [InlineData(0, DayType.Weekday)]
    [InlineData(3, DayType.Weekday)]
    [InlineData(4, DayType.Weekend)]
    [InlineData(5, DayType.Weekend)]
    [InlineData(6, DayType.Weekday)]
    public void ClassifiesFridaySaturdayButNotSunday(int offset, DayType expected) =>
        Assert.Equal(expected, PricingCalculator.ClassifyNight(Start.AddDays(offset), new HashSet<DateOnly>()));

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    [InlineData(5)]
    public void HolidayOverridesEveryDayType(int offset)
    {
        var date = Start.AddDays(offset);
        Assert.Equal(DayType.Holiday, PricingCalculator.ClassifyNight(date, new HashSet<DateOnly> { date }));
    }

    [Fact]
    public void PricesUseLatestVersionForEachNightAndIgnoreFutureVersions()
    {
        var prices = Prices().Concat(new RoomPriceVersion[] { new(DayType.Weekday, Start.AddDays(-1), 900_000),
            new(DayType.Weekday, Start.AddDays(1), 1_100_000), new(DayType.Weekday, Start.AddDays(3), 9_000_000) }).Reverse().ToArray();
        var result = PricingCalculator.Calculate(Start, Start.AddDays(3), new HashSet<DateOnly>(), prices, .3m);
        Assert.Equal(new decimal[] { 1_000_000, 1_100_000, 1_100_000 }, result.Nights.Select(n => n.Price));
        Assert.Equal(3_200_000, result.TotalAmount);
        Assert.Equal(960_000, result.DepositAmount);
        Assert.Equal(Start.AddDays(2), result.Nights[^1].Date);
        Assert.Equal(DayOfWeek.Monday, result.Nights[0].DayOfWeek);
    }

    [Theory]
    [InlineData(1499, 1000)]
    [InlineData(1500, 2000)]
    [InlineData(500, 1000)]
    [InlineData(2500, 3000)]
    [InlineData(-1500, -2000)]
    public void RoundToThousandUsesHalfAwayFromZero(int value, int expected) => Assert.Equal(expected, PricingCalculator.RoundToThousand(value));

    [Fact]
    public void DepositAtExactlyFiveHundredRoundsUp()
    {
        var prices = Prices().Select(p => p with { Price = 105_000 }).ToArray();
        Assert.Equal(32_000, PricingCalculator.Calculate(Start, Start.AddDays(1), new HashSet<DateOnly>(), prices, .3m).DepositAmount);
    }

    [Theory]
    [InlineData(DayType.Weekday)]
    [InlineData(DayType.Weekend)]
    [InlineData(DayType.Holiday)]
    public void MissingAnyEffectiveRateMakesRoomUnsellableIncludingUnusedDayTypes(DayType missing)
    {
        var error = Assert.Throws<MissingRoomPriceException>(() => PricingCalculator.Calculate(Start, Start.AddDays(1), new HashSet<DateOnly>(),
            Prices().Where(p => p.DayType != missing).ToArray(), .3m));
        Assert.Equal(Start, error.Night);
        Assert.Equal(missing, error.DayType);
        Assert.Contains("26/10/2026", error.Message);
        Assert.Contains(missing.ToString(), error.Message);
    }

    [Fact]
    public void FutureOnlyRateDoesNotFillMissingEarlierNight() => Assert.Throws<MissingRoomPriceException>(() =>
        PricingCalculator.Calculate(Start, Start.AddDays(1), new HashSet<DateOnly>(), Prices().Select(p => p.DayType == DayType.Holiday ? p with { EffectiveFrom = Start.AddDays(1) } : p).ToArray(), .3m));

    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    public void HalfOpenStayHasExactlyRequestedNumberOfNights(int count)
    {
        var holiday = Start.AddDays(1);
        var result = PricingCalculator.Calculate(Start, Start.AddDays(count), new HashSet<DateOnly> { holiday }, Prices(), .3m);
        Assert.Equal(count, result.Nights.Count);
        var expected = Enumerable.Range(0, count).Select(i => Start.AddDays(i)).Sum(d => d == holiday ? 1_500_000 : d.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday ? 1_200_000 : 1_000_000);
        Assert.Equal(expected, result.TotalAmount);
        Assert.Equal((decimal)expected / count, result.AveragePrice);
    }

    [Theory]
    [InlineData(true, true, true, true, 1)]
    [InlineData(true, false, true, true, 2)]
    [InlineData(true, false, false, true, 3)]
    [InlineData(true, false, false, false, 4)]
    [InlineData(false, true, true, true, 2)]
    [InlineData(false, true, false, true, 4)]
    public void ChoosesAllFourPolicyBranchesInOrder(bool holiday, bool privateHoliday, bool privateAll, bool generalHoliday, int expected)
    {
        var policies = new[] { Policy(1, 10, CancellationScope.Holiday, privateHoliday), Policy(2, 10, CancellationScope.All, privateAll),
            Policy(3, null, CancellationScope.Holiday, generalHoliday), Policy(4, null, CancellationScope.All, true), Policy(5, 99, CancellationScope.Holiday, true) };
        Assert.Equal(expected, PricingCalculator.SelectCancellationPolicy(10, holiday, policies).Id);
    }
    [Fact]
    public void MissingApplicablePolicyReportsConfigurationProblem() => Assert.Throws<BusinessException>(() => PricingCalculator.SelectCancellationPolicy(10, false, []));
    [Fact]
    public void InvalidStayAndDepositRateFail()
    {
        Assert.Throws<BusinessException>(() => PricingCalculator.Calculate(Start, Start, new HashSet<DateOnly>(), Prices(), .3m));
        Assert.Throws<BusinessException>(() => PricingCalculator.Calculate(Start, Start.AddDays(1), new HashSet<DateOnly>(), Prices(), 0));
    }
    private static ApplicableCancellationPolicy Policy(int id, int? home, CancellationScope scope, bool active) => new(id, home, "Policy " + id, scope, active, [new CancellationRuleItem(0, 0)]);
}
