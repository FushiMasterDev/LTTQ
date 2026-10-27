using Homestay.BLL.Infrastructure;
using Homestay.BLL.Security;
using Homestay.BLL.Services;
using Homestay.DTO.Auth;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Tests;

public sealed class M6AccountingInputTests
{
    private static AccountingService Service(Position role)
    {
        var current = new CurrentSession();
        current.Set(new SessionInfo { Position = role });
        // This contract must fail before using any SQL dependency, even in the sandbox.
        return new(new Authorizer(current), current, null!, null!, null!, null!, null!, null!, null!, null!, "M6-input");
    }

    [Fact]
    public async Task NullRefundRequestReportsArgumentNameBeforeOpeningDatabase()
    {
        var error = await Assert.ThrowsAsync<ArgumentNullException>(() =>
            Service(Position.Accountant).RecordUnmatchedRefundAsync(null!));
        Assert.Equal("request", error.ParamName);
    }

    [Theory]
    [InlineData(Position.Staff)]
    [InlineData(Position.Manager)]
    public async Task RefundStillChecksPermissionBeforeNullInput(Position role)
        => await Assert.ThrowsAsync<ForbiddenException>(() => Service(role).RecordUnmatchedRefundAsync(null!));
}
