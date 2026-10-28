using Homestay.BLL.Infrastructure;
using Homestay.BLL.Services;
using Homestay.DTO.Auth;
using Homestay.DTO.Enums;

namespace Homestay.Rules.Tests;

/// <summary>Kiểm thử hợp đồng API M2.1: kiểm dữ liệu nhập và quyền trước khi mở CSDL.</summary>
public sealed class M21ContractTests
{
    private static (CurrentSession Current, Authorizer Authorizer) Session(Position role)
    {
        var current = new CurrentSession(); current.Set(new SessionInfo { UserId = 1, EmployeeId = 1, Position = role });
        return (current, new Authorizer(current));
    }
    [Fact]
    public async Task M24WritesValidateInputBeforeOpeningDatabase()
    {
        var s = Session(Position.Manager);
        var b = new BookingService(s.Authorizer, s.Current, null!, null!, null!, null!, null!, null!, null!);
        await Assert.ThrowsAsync<BusinessException>(() => b.ClaimAsync(new(1, [])));
        await Assert.ThrowsAsync<BusinessException>(() => b.ReassignAsync(new(1, 2, [])));
        await Assert.ThrowsAsync<BusinessException>(() => b.CancelUnpaidAsync(new(1, "Test", [])));
        var payments = new PaymentService(s.Authorizer, s.Current, null!, null!, null!, null!, null!, null!, null!, b, null!, "test");
        await Assert.ThrowsAsync<BusinessException>(() => payments.RecordReceiptAsync(new()));
    }
    [Fact]
    public async Task AccountantReadOnlyCannotWriteCustomerOrAccessAvailability()
    {
        var s = Session(Position.Accountant);
        var customers = new CustomerService(s.Authorizer, s.Current, null!, null!, null!, null!, null!, "test");
        await Assert.ThrowsAsync<ForbiddenException>(() => customers.CreateAsync(new()));
        await Assert.ThrowsAsync<ForbiddenException>(() => customers.UpdateAsync(new()));
        await Assert.ThrowsAsync<ForbiddenException>(() => customers.ChangePhoneAsync(new(1, "0900000000", new byte[8])));
        var availability = new AvailabilityService(s.Authorizer, s.Current, null!, null!, null!, null!);
        await Assert.ThrowsAsync<ForbiddenException>(() => availability.SearchAsync(new()));
        await Assert.ThrowsAsync<ForbiddenException>(() => availability.GetCalendarAsync(new(1, null, new DateOnly(2026, 1, 1))));
    }
    [Fact]
    public async Task InputWhitelistsRejectInjectionAndInvalidPagingBeforeDatabaseAccess()
    {
        var s = Session(Position.Staff);
        var bookings = new BookingService(s.Authorizer, s.Current, null!, null!, null!, null!, null!, null!, null!);
        await Assert.ThrowsAsync<BusinessException>(() => bookings.ListAsync(new() { Page = new() { SortColumn = "Code; DELETE dbo.Bookings" } }));
        await Assert.ThrowsAsync<BusinessException>(() => bookings.ListAsync(new() { Page = new() { PageSize = 1 } }));
        await Assert.ThrowsAsync<BusinessException>(() => bookings.ListAsync(new() { Page = new() { PageSize = 100, PageNumber = int.MaxValue } }));
        await Assert.ThrowsAsync<BusinessException>(() => bookings.ListAsync(new() { Queue = (Homestay.DTO.Bookings.BookingQueue)999 }));
        var availability = new AvailabilityService(s.Authorizer, s.Current, null!, null!, null!, null!);
        await Assert.ThrowsAsync<BusinessException>(() => availability.GetCalendarAsync(new(1, 2, new DateOnly(2026, 1, 1))));
        await Assert.ThrowsAsync<BusinessException>(() => availability.GetCalendarAsync(new(null, null, new DateOnly(2026, 1, 1))));
        await Assert.ThrowsAsync<BusinessException>(() => availability.GetCalendarAsync(new(1, null, DateOnly.MaxValue, 31)));
        await Assert.ThrowsAsync<BusinessException>(() => availability.SearchAsync(new() { MinAveragePrice = 100, MaxAveragePrice = 1 }));
    }
    [Fact]
    public async Task CustomerValidationRejectsMaskedIdentityAndStaleVersionShape()
    {
        var s = Session(Position.Staff); var customers = new CustomerService(s.Authorizer, s.Current, null!, null!, null!, null!, null!, "test");
        await Assert.ThrowsAsync<BusinessException>(() => customers.CreateAsync(new() { FullName = "Khách", Phone = "0901234567", IdNumber = "••••••••1234" }));
        await Assert.ThrowsAsync<BusinessException>(() => customers.UpdateAsync(new() { FullName = "Khách", RowVer = [1] }));
        await Assert.ThrowsAsync<BusinessException>(() => customers.CreateAsync(new() { FullName = "Khách", Phone = "0901234567", Email = "invalid@" }));
    }
    [Fact]
    public async Task NotificationsRequireOwnSessionAndRejectUnsupportedSearch()
    {
        var current = new CurrentSession(); var notifications = new NotificationService(current, null!, null!, null!, null!);
        await Assert.ThrowsAsync<SessionExpiredException>(() => notifications.CountUnreadAsync());
        current.Set(new SessionInfo { UserId = 1, Position = Position.Accountant });
        await Assert.ThrowsAsync<BusinessException>(() => notifications.ListAsync(new() { SearchText = "test" }));
    }
}
