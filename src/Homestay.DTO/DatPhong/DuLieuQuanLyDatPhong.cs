using Homestay.DTO.Common;

namespace Homestay.DTO.Bookings;

public sealed record BookingAssignmentItem(int BookingId, byte[] RowVer)
{
    public string? BookingCode { get; init; }
}
public sealed record AssignManyBookingsRequest(IReadOnlyList<BookingAssignmentItem> Bookings, int AssignedEmployeeId);
public enum BookingExceptionKind { ConfirmationOverdue, HoldExpiring, ReceiptWaiting, Unassigned }
public enum BookingExceptionAction { OpenBooking, Assign, CreateTask }
public sealed record BookingExceptionSearchRequest
{
    public PageRequest Page { get; init; } = new();
    public BookingExceptionKind? Kind { get; init; }
    public int? AreaId { get; init; }
    public int? HomestayId { get; init; }
}
public sealed record BookingExceptionItem(int BookingId, string BookingCode, BookingExceptionKind Kind, string Problem,
    DateTime ServerNowUtc, DateTime SinceUtc, long ElapsedSeconds, DateTime? DeadlineAtUtc, long? RemainingSeconds,
    int? PaymentId, byte[] RowVer, IReadOnlyList<BookingExceptionAction> AllowedActions)
{
    public long? WorkingElapsedSeconds { get; init; }
    public long OverdueSeconds { get; init; }
}
public sealed record BookingExceptionCount(BookingExceptionKind Kind, int Count);
public sealed record ManagerDashboard(DateTime ServerNowUtc, DateOnly Today, int ArrivalsToday, int DeparturesToday,
    int Unassigned, IReadOnlyList<BookingExceptionCount> Exceptions, int OverdueTasks, int PendingReceipts, int PendingCancellations);
