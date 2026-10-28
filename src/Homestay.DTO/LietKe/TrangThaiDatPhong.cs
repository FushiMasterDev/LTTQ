namespace Homestay.DTO.Enums;

/// <summary>Trạng thái đơn đặt phòng: chờ thanh toán, đã gửi phiếu, đã cọc, đã xác nhận, đang ở, hoàn tất, hết hạn, bị từ chối, đã hủy, không đến.</summary>
public enum BookingStatus
{
    AwaitingPayment,
    PaymentSubmitted,
    Deposited,
    Confirmed,
    CheckedIn,
    Completed,
    Expired,
    Rejected,
    Cancelled,
    NoShow,
}
