using Homestay.DTO.Common;

namespace Homestay.DTO.Bookings;

/// <summary>Một đơn được chọn để giao (kèm RowVer).</summary>
public sealed record BookingAssignmentItem(int BookingId, byte[] RowVer)
{
    public string? BookingCode { get; init; }
}
/// <summary>Yêu cầu quản lý giao nhiều đơn cho một nhân viên cùng lúc.</summary>
public sealed record AssignManyBookingsRequest(IReadOnlyList<BookingAssignmentItem> Bookings, int AssignedEmployeeId);
/// <summary>Loại đơn ngoại lệ cần quản lý chú ý: quá hạn xác nhận, sắp hết giữ phòng, phiếu thu chờ duyệt, chưa giao.</summary>
public enum BookingExceptionKind { ConfirmationOverdue, HoldExpiring, ReceiptWaiting, Unassigned }
/// <summary>Thao tác gợi ý cho đơn ngoại lệ: mở đơn, giao đơn, tạo công việc.</summary>
public enum BookingExceptionAction { OpenBooking, Assign, CreateTask }
/// <summary>Điều kiện lọc danh sách đơn ngoại lệ.</summary>
public sealed record BookingExceptionSearchRequest
{
    public PageRequest Page { get; init; } = new();
    public BookingExceptionKind? Kind { get; init; }
    public int? AreaId { get; init; }
    public int? HomestayId { get; init; }
}
/// <summary>Một đơn ngoại lệ: vấn đề gì, đã chờ bao lâu, quá hạn bao lâu.</summary>
public sealed record BookingExceptionItem(int BookingId, string BookingCode, BookingExceptionKind Kind, string Problem,
    DateTime ServerNowUtc, DateTime SinceUtc, long ElapsedSeconds, DateTime? DeadlineAtUtc, long? RemainingSeconds,
    int? PaymentId, byte[] RowVer, IReadOnlyList<BookingExceptionAction> AllowedActions)
{
    public long? WorkingElapsedSeconds { get; init; }
    public long OverdueSeconds { get; init; }
}
/// <summary>Số đơn ngoại lệ theo từng loại.</summary>
public sealed record BookingExceptionCount(BookingExceptionKind Kind, int Count);
/// <summary>Số liệu tổng quan cho quản lý: khách đến/đi hôm nay, đơn ngoại lệ, việc quá hạn…</summary>
public sealed record ManagerDashboard(DateTime ServerNowUtc, DateOnly Today, int ArrivalsToday, int DeparturesToday,
    int Unassigned, IReadOnlyList<BookingExceptionCount> Exceptions, int OverdueTasks, int PendingReceipts, int PendingCancellations);
