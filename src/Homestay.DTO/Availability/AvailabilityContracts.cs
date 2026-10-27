using Homestay.DTO.Catalog;
using Homestay.DTO.Enums;

namespace Homestay.DTO.Availability;

public sealed record AvailabilitySearchRequest
{
    public int? AreaId { get; init; }
    public DateOnly CheckIn { get; init; }
    public DateOnly CheckOut { get; init; }
    public int Guests { get; init; } = 1;
    public decimal? MinAveragePrice { get; init; }
    public decimal? MaxAveragePrice { get; init; }
    public int? CategoryId { get; init; }
    public IReadOnlyList<int> AmenityIds { get; init; } = [];
}
public sealed record QuoteRequest(int RoomTypeId, DateOnly CheckIn, DateOnly CheckOut, int Guests);
public sealed record RoomPriceVersion(DayType DayType, DateOnly EffectiveFrom, decimal Price);
public sealed record NightlyPrice(DateOnly Date, DayOfWeek DayOfWeek, DayType DayType, decimal Price);
public sealed record ApplicableCancellationPolicy(int Id, int? HomestayId, string Name, CancellationScope Scope,
    bool IsActive, IReadOnlyList<CancellationRuleItem> Rules);
public sealed record PriceBreakdown(IReadOnlyList<NightlyPrice> Nights, decimal TotalAmount, decimal AveragePrice,
    decimal DepositRate, decimal DepositAmount);
public sealed record AvailableRoom(int RoomId, string RoomCode);
public sealed record FujiBankDetails(string BankName, string AccountNo, string AccountName);
public sealed record AvailabilityItem
{
    public int HomestayId { get; init; }
    public string HomestayName { get; init; } = "";
    public int AreaId { get; init; }
    public string AreaName { get; init; } = "";
    public int RoomTypeId { get; init; }
    public string RoomTypeName { get; init; } = "";
    public int CategoryId { get; init; }
    public string CategoryName { get; init; } = "";
    public int MaxGuests { get; init; }
    public int AvailableRooms { get; init; }
    public int TotalActiveRooms { get; init; }
    public decimal AveragePrice { get; init; }
    public decimal TotalAmount { get; init; }
    public decimal DepositAmount { get; init; }
    public ApplicableCancellationPolicy CancellationPolicy { get; init; } = null!;
}
public sealed record BookingQuote(AvailabilityItem RoomType, PriceBreakdown Pricing, decimal AmountDue,
    decimal? CommissionRate, IReadOnlyList<AvailableRoom> AvailableRooms, FujiBankDetails Bank);

public sealed record RoomCalendarRequest(int? HomestayId, int? AreaId, DateOnly FromDate, int Days = 14);
// Bảng 7.4. GUI ánh xạ tên tiếng Việt/màu; không suy diễn từ BookingStatus.
public enum RoomCalendarState { Available, Held, Booked, Occupied, Stayed, OtherChannelBlock, MaintenanceBlock, ChangeRequestHold, OffSale }
public sealed record RoomCalendarCell(DateOnly Date, RoomCalendarState State, int? BookingId, string? BookingCode,
    string? CustomerName, DateTime? HoldExpiresAtUtc, int? RoomBlockId, int? ChangeRequestId);
public sealed record RoomCalendarRow(int HomestayId, string HomestayName, int RoomTypeId, string RoomTypeName,
    int RoomId, string RoomCode, IReadOnlyList<RoomCalendarCell> Cells);
public sealed record RoomCalendar(DateOnly FromDate, int Days, DateTime ServerNowUtc, IReadOnlyList<RoomCalendarRow> Rooms);
