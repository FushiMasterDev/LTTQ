using Homestay.BLL.Infrastructure;
using Homestay.DTO.Auth;
using Homestay.DTO.Enums;

namespace Homestay.Rules.Tests;

public sealed class AuthorizationTests
{
    // Mỗi dòng kiểm tra đủ NV/QL/KT; bao phủ cả sáu bảng của spec.
    [Theory]
    [InlineData(Permission.AccountSelfManage, AccessLevel.All, AccessLevel.All, AccessLevel.All)]
    [InlineData(Permission.AccountManage, AccessLevel.None, AccessLevel.All, AccessLevel.None)]
    [InlineData(Permission.CatalogManage, AccessLevel.ReadOnly, AccessLevel.All, AccessLevel.ReadOnly)]
    [InlineData(Permission.AuditView, AccessLevel.Own, AccessLevel.All, AccessLevel.FinancialTables)]
    [InlineData(Permission.OwnerEdit, AccessLevel.DraftOnly, AccessLevel.All, AccessLevel.ReadOnly)]
    [InlineData(Permission.HomestayEdit, AccessLevel.DraftOnly, AccessLevel.All, AccessLevel.None)]
    [InlineData(Permission.HomestayReview, AccessLevel.None, AccessLevel.All, AccessLevel.None)]
    [InlineData(Permission.ContractManage, AccessLevel.None, AccessLevel.All, AccessLevel.ReadOnly)]
    [InlineData(Permission.RoomPriceAdd, AccessLevel.DraftOnly, AccessLevel.All, AccessLevel.None)]
    [InlineData(Permission.RoomCalendarView, AccessLevel.All, AccessLevel.All, AccessLevel.None)]
    [InlineData(Permission.RoomBlockManage, AccessLevel.None, AccessLevel.All, AccessLevel.None)]
    [InlineData(Permission.BookingCreate, AccessLevel.All, AccessLevel.All, AccessLevel.None)]
    [InlineData(Permission.BookingView, AccessLevel.All, AccessLevel.All, AccessLevel.ReadOnly)]
    [InlineData(Permission.BookingAssign, AccessLevel.Own, AccessLevel.All, AccessLevel.None)]
    [InlineData(Permission.BookingConfirm, AccessLevel.OnBehalfOfOwnerInScope, AccessLevel.OnBehalfOfOwner, AccessLevel.None)]
    [InlineData(Permission.PaymentRecord, AccessLevel.InScope, AccessLevel.All, AccessLevel.All)]
    [InlineData(Permission.BookingCancellationRequest, AccessLevel.InScope, AccessLevel.All, AccessLevel.None)]
    [InlineData(Permission.PaymentVerify, AccessLevel.None, AccessLevel.None, AccessLevel.All)]
    [InlineData(Permission.BankStatementManage, AccessLevel.None, AccessLevel.ReadOnly, AccessLevel.All)]
    [InlineData(Permission.SettlementCreate, AccessLevel.None, AccessLevel.All, AccessLevel.All)]
    [InlineData(Permission.SettlementManage, AccessLevel.None, AccessLevel.ReadOnly, AccessLevel.All)]
    [InlineData(Permission.OwnPerformanceView, AccessLevel.Own, AccessLevel.Own, AccessLevel.None)]
    [InlineData(Permission.OperationsReportView, AccessLevel.None, AccessLevel.All, AccessLevel.None)]
    [InlineData(Permission.FinancialReportView, AccessLevel.None, AccessLevel.All, AccessLevel.All)]
    [InlineData(Permission.OwnWorkTaskManage, AccessLevel.Own, AccessLevel.Own, AccessLevel.Own)]
    public void MatrixMatchesSpec(Permission permission, AccessLevel staff, AccessLevel manager, AccessLevel accountant)
    {
        Assert.Equal(staff, Authorizer.GetAccess(Position.Staff, permission));
        Assert.Equal(manager, Authorizer.GetAccess(Position.Manager, permission));
        Assert.Equal(accountant, Authorizer.GetAccess(Position.Accountant, permission));
    }

    [Fact]
    public void RequireAndSessionLifecycle()
    {
        var current = new CurrentSession();
        var authorizer = new Authorizer(current);
        Assert.Null(current.Session);
        Assert.Throws<SessionExpiredException>(() => authorizer.Can(Permission.BookingView));
        var areas = new List<int> { 1 };
        current.Set(new SessionInfo { Position = Position.Staff, AreaIds = areas });
        areas.Add(2);
        Assert.Single(current.RequireSession().AreaIds);
        Assert.True(authorizer.Can(Permission.BookingCreate));
        Assert.False(authorizer.Can(Permission.PaymentVerify));
        Assert.Throws<ForbiddenException>(() => authorizer.Require(Permission.PaymentVerify));
        current.Clear();
        Assert.Throws<SessionExpiredException>(() => current.RequireSession());
    }

    [Fact]
    public void EveryPermissionHasAnExplicitGrantAndUnknownValuesFailClosed()
    {
        Assert.Equal(62, Enum.GetValues<Permission>().Length);
        Assert.All(Enum.GetValues<Permission>(), permission =>
            Assert.Contains(Enum.GetValues<Position>(), role => Authorizer.GetAccess(role, permission) != AccessLevel.None));
        Assert.Equal(AccessLevel.None, Authorizer.GetAccess((Position)999, Permission.BookingView));
        Assert.Equal(AccessLevel.None, Authorizer.GetAccess(Position.Manager, (Permission)999));
    }

    [Theory]
    [InlineData(Position.Staff, true)]
    [InlineData(Position.Manager, false)]
    public void ScopeUsesAreaOrCreatorOrAssigneeAndTypedParameters(Position position, bool hasAll)
    {
        var current = new CurrentSession();
        current.Set(new SessionInfo { Position = position, EmployeeId = 12, HasAllAreas = hasAll, AreaIds = new[] { 3, 7, 3 } });
        var scope = new DataScope(current).BookingFilter();
        Assert.Contains("scopeArea.AreaId=h.AreaId", scope.Condition);
        Assert.Contains("dbo.EmployeeAreas", scope.Condition);
        Assert.Contains("scopeEmployee.Position='Manager' AND scopeEmployee.HasAllAreas=1", scope.Condition);
        Assert.Contains("b.CreatedByEmployeeId = @scopeEmployeeId OR b.AssignedEmployeeId = @scopeEmployeeId", scope.Condition);
        Assert.Single(scope.Parameters);
        Assert.All(scope.Parameters, p => Assert.Equal(System.Data.SqlDbType.Int, p.SqlDbType));
        using var command = new Microsoft.Data.SqlClient.SqlCommand();
        scope.AddParametersTo(command);
        using var other = new Microsoft.Data.SqlClient.SqlCommand();
        scope.AddParametersTo(other);
        Assert.Equal(command.Parameters.Count, other.Parameters.Count);
    }

    [Theory]
    [InlineData(Position.Manager)]
    [InlineData(Position.Accountant)]
    public void ScopeAllowsAllForAllAreaManagerAndFinancialRead(Position position)
    {
        var current = new CurrentSession();
        current.Set(new SessionInfo { Position = position, HasAllAreas = true });
        var filter = new DataScope(current).BookingFilter();
        if (position == Position.Accountant) Assert.Equal("1 = 1", filter.Condition);
        else Assert.Contains("scopeEmployee.HasAllAreas=1", filter.Condition);
    }

    [Fact]
    public void ScopeWithoutAreasStillAllowsOwnBookingsAndRejectsSqlIdentifiers()
    {
        var current = new CurrentSession();
        current.Set(new SessionInfo { Position = Position.Staff, EmployeeId = 12 });
        var scope = new DataScope(current);
        Assert.Contains("b.CreatedByEmployeeId = @scopeEmployeeId", scope.BookingFilter().Condition);
        Assert.Throws<ArgumentException>(() => scope.BookingFilter("b; DELETE dbo.Users"));
    }
}
