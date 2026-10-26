using System.Globalization;
using Homestay.BLL.Infrastructure;
using Homestay.DTO.Availability;
using Homestay.DTO.Bookings;

namespace Homestay.BLL.Rules;

public static class BookingCreationRules
{
    public const string SoldOutMessage = "Đã hết phòng loại này trong khoảng ngày đã chọn";

    public static CreateBookingRequest Normalize(CreateBookingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.CustomerId.HasValue == (request.QuickCustomer is not null) || request.CustomerId is <= 0
            || request.RoomTypeId <= 0 || request.RoomId is <= 0 || !Enum.IsDefined(request.SourceChannel) || request.RowVer is not null)
            throw new BusinessException("Vui lòng chọn một khách có sẵn hoặc tạo nhanh khách mới, loại phòng và kênh đặt hợp lệ; đơn mới không có RowVer.");
        StaffInputRules.Text(request.SpecialRequests, 1000);
        return request with
        {
            ContactName = CustomerInputRules.Name(request.ContactName),
            ContactPhone = PhoneNumber.Normalize(request.ContactPhone),
            SpecialRequests = string.IsNullOrWhiteSpace(request.SpecialRequests) ? null : request.SpecialRequests.Trim(),
            QuickCustomer = request.QuickCustomer is null ? null : CustomerInputRules.Normalize(request.QuickCustomer)
        };
    }

    public static void ValidateStay(CreateBookingRequest request, DateTime serverNowUtc, int maxNights, int maxGuests)
    {
        if (request.CheckIn < DateOnly.FromDateTime(serverNowUtc.AddHours(7)) || request.CheckOut <= request.CheckIn
            || request.CheckOut.DayNumber - request.CheckIn.DayNumber > maxNights)
            throw new BusinessException($"Ngày nhận phải từ hôm nay, ngày trả sau ngày nhận và tối đa {maxNights} đêm. Vui lòng chọn lại.");
        if (request.Guests < 1 || request.Guests > maxGuests)
            throw new BusinessException($"Số khách phải từ 1 đến {maxGuests} theo sức chứa loại phòng. Vui lòng chọn lại.");
    }

    // AvailabilityRepository orders by RoomCode, Id using the database collation.
    // Preserve that order, including case/accent rules, rather than re-sorting in a different culture.
    public static AvailableRoom? SelectRoom(IEnumerable<AvailableRoom> roomsInCodeOrder, int? requestedRoomId) =>
        roomsInCodeOrder.FirstOrDefault(r => requestedRoomId is null || r.RoomId == requestedRoomId);

    public static string CounterKey(DateTime serverNowUtc)
    {
        if (serverNowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Server time must be UTC.", nameof(serverNowUtc));
        return "FJ" + serverNowUtc.AddHours(7).ToString("yyMMdd", CultureInfo.InvariantCulture);
    }

    public static string FormatCode(DateTime serverNowUtc, int sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        return CounterKey(serverNowUtc) + "-" + sequence.ToString("D3", CultureInfo.InvariantCulture);
    }
}
