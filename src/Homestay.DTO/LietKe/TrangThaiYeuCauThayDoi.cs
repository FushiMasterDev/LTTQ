namespace Homestay.DTO.Enums;

/// <summary>Trạng thái yêu cầu thay đổi đơn: chờ duyệt, chờ thanh toán, đã áp dụng, bị từ chối, hết hạn.</summary>
public enum ChangeRequestStatus
{
    Pending,
    AwaitingPayment,
    Applied,
    Rejected,
    Expired,
}
