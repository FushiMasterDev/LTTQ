using Homestay.DTO.Catalog;
using Homestay.DTO.Enums;

namespace Homestay.DTO.Availability;

/// <summary>Điều kiện tìm phòng trống: khu vực, ngày nhận/trả, số khách, khoảng giá, hạng phòng, tiện nghi.</summary>
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
/// <summary>Yêu cầu báo giá cho một loại phòng theo ngày và số khách.</summary>
public sealed record QuoteRequest(int RoomTypeId, DateOnly CheckIn, DateOnly CheckOut, int Guests);
/// <summary>Một mức giá theo loại ngày có hiệu lực từ ngày nào, dùng khi tính giá.</summary>
public sealed record RoomPriceVersion(DayType DayType, DateOnly EffectiveFrom, decimal Price);
/// <summary>Giá của từng đêm (ngày, thứ, loại ngày, giá).</summary>
public sealed record NightlyPrice(DateOnly Date, DayOfWeek DayOfWeek, DayType DayType, decimal Price);
/// <summary>Chính sách hủy áp dụng cho đơn (chung hoặc riêng của homestay) cùng các mốc hoàn tiền.</summary>
public sealed record ApplicableCancellationPolicy(int Id, int? HomestayId, string Name, CancellationScope Scope,
    bool IsActive, IReadOnlyList<CancellationRuleItem> Rules);
/// <summary>Bảng tính giá chi tiết: giá từng đêm, tổng tiền, giá trung bình.</summary>
public sealed record PriceBreakdown(IReadOnlyList<NightlyPrice> Nights, decimal TotalAmount, decimal AveragePrice,
    decimal DepositRate, decimal DepositAmount);
/// <summary>Một phòng còn trống có thể chọn khi tạo đơn.</summary>
public sealed record AvailableRoom(int RoomId, string RoomCode);
/// <summary>Tài khoản ngân hàng của công ty để khách chuyển tiền cọc.</summary>
public sealed record FujiBankDetails(string BankName, string AccountNo, string AccountName);
/// <summary>Một loại phòng còn trống trong kết quả tìm kiếm (homestay, khu vực, số phòng trống, giá).</summary>
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
/// <summary>Báo giá cho khách: loại phòng, bảng giá, tiền cọc phải trả, chính sách hủy, phòng trống.</summary>
public sealed record BookingQuote(AvailabilityItem RoomType, PriceBreakdown Pricing, decimal AmountDue,
    decimal? CommissionRate, IReadOnlyList<AvailableRoom> AvailableRooms, FujiBankDetails Bank);

/// <summary>Điều kiện xem lịch phòng: homestay/khu vực, từ ngày, số ngày.</summary>
public sealed record RoomCalendarRequest(int? HomestayId, int? AreaId, DateOnly FromDate, int Days = 14);
// Bảng 7.4. GUI ánh xạ tên tiếng Việt/màu; không suy diễn từ BookingStatus.
/// <summary>Trạng thái một ô lịch phòng: trống, đang giữ, đã đặt, đang ở, khóa kênh khác, bảo trì, ngưng bán…</summary>
public enum RoomCalendarState { Available, Held, Booked, Occupied, Stayed, OtherChannelBlock, MaintenanceBlock, ChangeRequestHold, OffSale }
/// <summary>Một ô trên lịch phòng: ngày, trạng thái và đơn liên quan.</summary>
public sealed record RoomCalendarCell(DateOnly Date, RoomCalendarState State, int? BookingId, string? BookingCode,
    string? CustomerName, DateTime? HoldExpiresAtUtc, int? RoomBlockId, int? ChangeRequestId);
/// <summary>Một dòng trên lịch phòng (một phòng) gồm các ô theo ngày.</summary>
public sealed record RoomCalendarRow(int HomestayId, string HomestayName, int RoomTypeId, string RoomTypeName,
    int RoomId, string RoomCode, IReadOnlyList<RoomCalendarCell> Cells);
/// <summary>Toàn bộ lịch phòng trong khoảng ngày cần xem.</summary>
public sealed record RoomCalendar(DateOnly FromDate, int Days, DateTime ServerNowUtc, IReadOnlyList<RoomCalendarRow> Rooms);
