namespace Homestay.DTO.Enums;

/// <summary>Trạng thái phiếu thanh toán: chờ duyệt, đã xác nhận, bị từ chối.</summary>
public enum PaymentStatus
{
    Pending,
    Verified,
    Rejected,
}
