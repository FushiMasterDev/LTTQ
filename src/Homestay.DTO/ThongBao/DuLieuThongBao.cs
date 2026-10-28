namespace Homestay.DTO.Notifications;

/// <summary>Một thông báo gửi người dùng (nội dung, liên kết tới đơn/việc nào, đã đọc chưa).</summary>
public sealed record NotificationItem(long Id, string Message, string? RefType, string? RefId, bool IsRead, DateTime CreatedAtUtc);
