namespace Homestay.DTO.Notifications;

public sealed record NotificationItem(long Id, string Message, string? RefType, string? RefId, bool IsRead, DateTime CreatedAtUtc);
