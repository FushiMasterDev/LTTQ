namespace Homestay.DTO.Enums;

/// <summary>Trạng thái homestay: nháp, chờ duyệt, đang hoạt động, tạm ngưng, đã kết thúc.</summary>
public enum HomestayStatus
{
    Draft,
    PendingReview,
    Active,
    Suspended,
    Ended,
}
