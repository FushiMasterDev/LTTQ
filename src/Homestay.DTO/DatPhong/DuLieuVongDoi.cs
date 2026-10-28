using Homestay.DTO.Availability;

namespace Homestay.DTO.Bookings;

/// <summary>Lý do phát sinh hoàn tiền: khách hủy, chủ từ chối, homestay không phục vụ được, không đến, hoàn tất, hủy khi chưa cọc.</summary>
public enum RefundReason { CustomerCancellation, OwnerRejected, HomestayUnavailable, NoShow, Completed, UnpaidCancellation }

// CancelledOn is a Vietnam calendar date, supplied by the caller of the pure calculator.
/// <summary>Dữ liệu đầu vào để tính tiền hoàn: ngày nhận phòng, ngày hủy, tiền đã cọc, lý do, hoa hồng.</summary>
public sealed record RefundCalculationRequest(DateOnly CheckIn, DateOnly CancelledOn, decimal AmountDue,
    bool HasCollectedDeposit, ApplicableCancellationPolicy Policy)
{
    public RefundReason Reason { get; init; } = RefundReason.CustomerCancellation;
    public decimal? TotalAmount { get; init; }
    public decimal? CommissionRate { get; init; }
}
/// <summary>Kết quả tính hoàn tiền: áp chính sách nào, hủy trước bao nhiêu ngày, hoàn bao nhiêu phần trăm và bao nhiêu tiền.</summary>
public sealed record RefundCalculation(int CancellationPolicyId, int DaysBefore, int RefundPercent,
    decimal RefundAmount, decimal RetainedAmount, decimal? CommissionAmount, decimal? OwnerPayoutAmount);
/// <summary>Bản xem trước tiền hoàn của một đơn trước khi xác nhận hủy.</summary>
public sealed record BookingRefundPreview(int BookingId, DateTime ServerNowUtc, DateOnly CancellationDate,
    byte[] RowVer, RefundCalculation Calculation);
/// <summary>Yêu cầu nhân viên xác nhận đơn thay chủ nhà (có thể chọn phòng).</summary>
public sealed record ConfirmOnBehalfRequest(int BookingId, string Note, byte[] RowVer, int? RoomId = null);
/// <summary>Yêu cầu nhân viên ghi nhận chủ nhà từ chối đơn kèm lý do.</summary>
public sealed record RejectOnBehalfRequest(int BookingId, string Reason, byte[] RowVer);
/// <summary>Yêu cầu thao tác lưu trú: nhận phòng, trả phòng hoặc đánh dấu không đến.</summary>
public sealed record StayActionRequest(int BookingId, string Note, byte[] RowVer);
/// <summary>Yêu cầu hủy đơn vì homestay không phục vụ được kèm lý do.</summary>
public sealed record CancelHomestayUnavailableRequest(int BookingId, string Reason, byte[] RowVer);
