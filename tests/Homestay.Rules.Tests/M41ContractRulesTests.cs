using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DTO.Auth;
using Homestay.DTO.Enums;
namespace Homestay.Rules.Tests;

public sealed class M41ContractRulesTests
{
    public static IEnumerable<object?[]> EveryTransition()
    {
        foreach (var from in new HomestayStatus?[] { null }.Concat(Enum.GetValues<HomestayStatus>().Select(x => (HomestayStatus?)x)))
            foreach (var to in Enum.GetValues<HomestayStatus>())
                foreach (var actor in Enum.GetValues<HomestayTransitionActor>())
                    foreach (var sameCreator in new[] { false, true })
                        foreach (var inScope in new[] { false, true }) yield return [from, to, actor, sameCreator, inScope];
    }
    [Theory]
    [MemberData(nameof(EveryTransition))]
    public void CompleteTableRejectsEveryOtherPairRoleCreatorAndScope(HomestayStatus? from, HomestayStatus to,
        HomestayTransitionActor actor, bool sameCreator, bool inScope)
    {
        var manager = actor == HomestayTransitionActor.Manager;
        var expected = (from, to) switch
        {
            (null, HomestayStatus.Draft) or (HomestayStatus.Draft, HomestayStatus.PendingReview) => actor is HomestayTransitionActor.Staff or HomestayTransitionActor.Manager,
            (HomestayStatus.PendingReview, HomestayStatus.Active) => manager && !sameCreator,
            (HomestayStatus.PendingReview, HomestayStatus.Draft) or (HomestayStatus.Active, HomestayStatus.Suspended)
                or (HomestayStatus.Suspended, HomestayStatus.Active) => manager,
            (HomestayStatus.Draft or HomestayStatus.PendingReview or HomestayStatus.Active or HomestayStatus.Suspended, HomestayStatus.Ended) => manager,
            (HomestayStatus.Active, HomestayStatus.PendingReview) => actor == HomestayTransitionActor.System,
            _ => false
        };
        expected &= inScope || actor == HomestayTransitionActor.System;
        Assert.Equal(expected, HomestayStateMachine.CanTransition(from, to, actor, 11, sameCreator ? 11 : 12, inScope));
        if (expected) Assert.NotNull(HomestayStateMachine.Require(from, to, actor, 11, sameCreator ? 11 : 12, "Đã xác minh với chủ", inScope));
        else Assert.ThrowsAny<BusinessException>(() => HomestayStateMachine.Require(from, to, actor, 11, sameCreator ? 11 : 12, "Lý do", inScope));
    }
    [Theory]
    [InlineData(HomestayStatus.PendingReview, HomestayStatus.Draft)]
    [InlineData(HomestayStatus.Active, HomestayStatus.Suspended)]
    [InlineData(HomestayStatus.Suspended, HomestayStatus.Active)]
    [InlineData(HomestayStatus.Draft, HomestayStatus.Ended)]
    [InlineData(HomestayStatus.PendingReview, HomestayStatus.Ended)]
    [InlineData(HomestayStatus.Active, HomestayStatus.Ended)]
    [InlineData(HomestayStatus.Suspended, HomestayStatus.Ended)]
    public void ReasonsAreRequiredAndLimited(HomestayStatus from, HomestayStatus to)
    {
        foreach (var reason in new[] { null, "", "  ", new string('x', 501) })
            Assert.Throws<BusinessException>(() => HomestayStateMachine.Require(from, to, HomestayTransitionActor.Manager, 1, 2, reason));
        Assert.NotNull(HomestayStateMachine.Require(from, to, HomestayTransitionActor.Manager, 1, 2, new string('x', 500)));
    }
    [Theory]
    [InlineData(9.99, false)]
    [InlineData(10, true)]
    [InlineData(15, true)]
    [InlineData(15.01, false)]
    public void CommissionPercentBoundaries(decimal percent, bool valid)
    {
        Action action = () => ContractRules.Validate(percent / 100, new(2026, 1, 1), new(2026, 12, 31));
        if (valid) action(); else Assert.Throws<BusinessException>(action);
    }
    [Fact]
    public void ContractDatesPrecisionAndConfiguredBounds()
    {
        var date = new DateOnly(2026, 10, 27);
        ContractRules.Validate(.125m, date, date.AddDays(1));
        Assert.Throws<BusinessException>(() => ContractRules.Validate(.12m, date, date));
        Assert.Throws<BusinessException>(() => ContractRules.Validate(.12m, date, date.AddDays(-1)));
        Assert.Throws<BusinessException>(() => ContractRules.Validate(.12345m, date, date.AddDays(1)));
        ContractRules.Validate(.09m, date, date.AddDays(1), .08m, .11m);
        Assert.Throws<BusinessException>(() => ContractRules.Validate(.12m, date, date.AddDays(1), .13m, .11m));
        Assert.Throws<BusinessException>(() => ContractRules.Validate(.12m, date, date.AddDays(1), 0, .99m));
        Assert.Throws<BusinessException>(() => ContractRules.Validate(.12m, date, date.AddDays(1), .01m, 1));
        ContractRules.RequireCurrentDates(date.AddDays(-1), date, date);
        Assert.Throws<BusinessException>(() => ContractRules.RequireCurrentDates(date.AddDays(1), date.AddDays(2), date));
        Assert.Throws<BusinessException>(() => ContractRules.RequireCurrentDates(date.AddDays(-2), date.AddDays(-1), date));
    }
    [Theory]
    [InlineData(Permission.HomestayView, AccessLevel.InScope, AccessLevel.InScope, AccessLevel.None)]
    [InlineData(Permission.OwnerIdentityView, AccessLevel.None, AccessLevel.All, AccessLevel.None)]
    [InlineData(Permission.RefundAccountEdit, AccessLevel.None, AccessLevel.None, AccessLevel.All)]
    public void NewPermissionsMatchMatrix(Permission permission, AccessLevel staff, AccessLevel manager, AccessLevel accountant)
    {
        Assert.Equal(staff, Authorizer.GetAccess(Position.Staff, permission)); Assert.Equal(manager, Authorizer.GetAccess(Position.Manager, permission));
        Assert.Equal(accountant, Authorizer.GetAccess(Position.Accountant, permission));
    }
    [Fact]
    public void ProfileScopesNeverUseBookingCreatorExceptionAndFinancialReadIsExplicit()
    {
        var current = new CurrentSession(); current.Set(new SessionInfo { Position = Position.Staff, EmployeeId = 11, HasAllAreas = true, AreaIds = [3, 3, 7] });
        var scope = new DataScope(current); Assert.Equal("h.AreaId IN (@listingArea0,@listingArea1)", scope.HomestayFilter().Condition);
        Assert.Equal(2, scope.HomestayFilter().Parameters.Count); Assert.DoesNotContain("CreatedByUserId", scope.HomestayFilter().Condition);
        Assert.Contains("o.Status='Pending'", scope.OwnerFilter().Condition);
        Assert.Throws<ArgumentException>(() => scope.HomestayFilter("h;DELETE"));
        Assert.Throws<ArgumentException>(() => scope.AreaFilter(column: "Id;DELETE"));
        current.Set(new SessionInfo { Position = Position.Manager, HasAllAreas = true }); Assert.Equal("1=1", scope.HomestayFilter().Condition);
        current.Set(new SessionInfo { Position = Position.Accountant }); Assert.Equal("1=0", scope.HomestayFilter().Condition);
        Assert.Equal("1=1", scope.HomestayFilter(financialRead: true).Condition);
        Assert.Throws<ForbiddenException>(() => HomestayStateMachine.Actor((Position)999));
    }
}
