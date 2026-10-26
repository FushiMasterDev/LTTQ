using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.BLL.Services;
using Homestay.DTO.Auth;
using Homestay.DTO.Availability;
using Homestay.DTO.Bookings;
using Homestay.DTO.Enums;

namespace Homestay.Rules.Tests;

public sealed class BookingCreationRulesTests
{
    private static CreateBookingRequest Request() => new()
    {
        CustomerId = 1, RoomTypeId = 1, ContactName = " Người ở ", ContactPhone = "090 123 4567",
        CheckIn = new(2026, 10, 28), CheckOut = new(2026, 10, 30), Guests = 2, SourceChannel = SourceChannel.Phone
    };

    [Theory]
    [InlineData("2026-10-27T16:59:59Z", 1, "FJ261027-001")]
    [InlineData("2026-10-27T17:00:00Z", 9, "FJ261028-009")]
    [InlineData("2026-12-31T17:00:00Z", 999, "FJ270101-999")]
    [InlineData("2026-10-27T00:00:00Z", 1000, "FJ261027-1000")]
    public void BookingCodeUsesVietnamDateAndDoesNotTruncateSequence(string utc, int sequence, string expected) =>
        Assert.Equal(expected, BookingCreationRules.FormatCode(DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal), sequence));

    [Fact]
    public void CodeRejectsNonUtcAndNonPositiveSequence()
    {
        Assert.Throws<ArgumentException>(() => BookingCreationRules.CounterKey(new(2026, 10, 27)));
        Assert.Throws<ArgumentOutOfRangeException>(() => BookingCreationRules.FormatCode(DateTime.UtcNow, 0));
    }

    [Fact]
    public void ChoosesFirstRoomCodeAndHonorsExplicitRoom()
    {
        AvailableRoom[] rooms = [new(1, "R001"), new(2, "R002"), new(3, "R010")];
        Assert.Equal(1, BookingCreationRules.SelectRoom(rooms, null)!.RoomId);
        Assert.Equal(3, BookingCreationRules.SelectRoom(rooms, 3)!.RoomId);
        Assert.Null(BookingCreationRules.SelectRoom(rooms, 4));
        Assert.Null(BookingCreationRules.SelectRoom([], null));
        // A SQL collation can place lowercase a before uppercase Z: do not reorder the DAL result ordinally.
        Assert.Equal(2, BookingCreationRules.SelectRoom([new(2, "a001"), new(1, "Z001")], null)!.RoomId);
    }

    [Fact]
    public void NormalizesContactAndQuickCustomerWithSameCustomerRules()
    {
        var r = BookingCreationRules.Normalize(Request() with { CustomerId = null,
            QuickCustomer = new() { FullName = " Khách ", Phone = "0901234567", Email = " a@example.test ", IdNumber = "AB1234" } });
        Assert.Equal("Người ở", r.ContactName); Assert.Equal("+84901234567", r.ContactPhone);
        Assert.Equal("Khách", r.QuickCustomer!.FullName); Assert.Equal(r.ContactPhone, r.QuickCustomer.Phone);
        Assert.Equal("a@example.test", r.QuickCustomer.Email);
    }

    [Fact]
    public void RejectsInvalidCustomerChannelPhoneLengthsAndVersion()
    {
        CreateBookingRequest[] invalid =
        [
            Request() with { CustomerId = null }, Request() with { QuickCustomer = new() },
            Request() with { CustomerId = 0 }, Request() with { RoomTypeId = 0 }, Request() with { RoomId = 0 },
            Request() with { SourceChannel = (SourceChannel)999 }, Request() with { ContactPhone = "abc" },
            Request() with { ContactName = new string('x', 101) }, Request() with { SpecialRequests = new string('x', 1001) },
            Request() with { RowVer = new byte[8] }, Request() with { CustomerId = null, QuickCustomer = new() { FullName = "Khách", Phone = "0901234567", IdNumber = "••1234" } }
        ];
        foreach (var r in invalid) Assert.Throws<BusinessException>(() => BookingCreationRules.Normalize(r));
    }

    [Fact]
    public void StayUsesVietnamMidnightAndInclusiveCapacityAndMaxNights()
    {
        var now = new DateTime(2026, 10, 27, 17, 0, 0, DateTimeKind.Utc);
        var r = Request();
        BookingCreationRules.ValidateStay(r, now, 2, 2);
        CreateBookingRequest[] invalid = [r with { CheckIn = new(2026, 10, 27) }, r with { CheckOut = r.CheckIn },
            r with { CheckOut = r.CheckIn.AddDays(3) }, r with { Guests = 0 }, r with { Guests = 3 }];
        foreach (var value in invalid) Assert.Throws<BusinessException>(() => BookingCreationRules.ValidateStay(value, now, 2, 2));
    }

    [Fact]
    public async Task AccountantCannotCreateBeforeAnyDatabaseAccess()
    {
        var current = new CurrentSession(); current.Set(new SessionInfo { Position = Position.Accountant });
        var service = new BookingService(new Authorizer(current), current, null!, null!, null!, null!, null!, null!, null!);
        await Assert.ThrowsAsync<ForbiddenException>(() => service.CreateAsync(new()));
    }
}
