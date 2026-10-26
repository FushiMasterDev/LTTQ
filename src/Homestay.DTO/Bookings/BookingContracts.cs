using Homestay.DTO.Availability;
using Homestay.DTO.Common;
using Homestay.DTO.Customers;
using Homestay.DTO.Enums;

namespace Homestay.DTO.Bookings;

public enum BookingQueue { Mine, Unassigned, AwaitingPayment, DepositVerification, OwnerConfirmation, Upcoming, InHouse, ArrivalsToday, DeparturesToday, OpenChangeRequests, All }
// Các thao tác thuộc Đợt 2; vòng đời Đợt 3 sẽ bổ sung action và quy tắc riêng.
public enum BookingAction { RecordDeposit, CancelUnpaid, Claim, Reassign }
public sealed record BookingSearchRequest
{
    public PageRequest Page { get; init; } = new();
    public BookingQueue Queue { get; init; } = BookingQueue.Mine;
    public string? Code { get; init; }
    public string? Phone { get; init; }
    public string? CustomerName { get; init; }
    public int? HomestayId { get; init; }
    public DateOnly? CheckInFrom { get; init; }
    public DateOnly? CheckInTo { get; init; }
    public BookingStatus? Status { get; init; }
    public bool OnlyExpiringHolds { get; init; }
    public bool OnlyOverdueConfirmation { get; init; }
    public bool AssignedToMeOnly { get; init; }
}
public sealed record BookingListItem
{
    public int Id { get; init; }
    public string Code { get; init; } = "";
    public int CustomerId { get; init; }
    public string CustomerName { get; init; } = "";
    public string CustomerPhone { get; init; } = "";
    public int HomestayId { get; init; }
    public string HomestayName { get; init; } = "";
    public int AreaId { get; init; }
    public int RoomTypeId { get; init; }
    public string RoomTypeName { get; init; } = "";
    public int RoomId { get; init; }
    public string RoomCode { get; init; } = "";
    public DateOnly CheckIn { get; init; }
    public DateOnly CheckOut { get; init; }
    public BookingStatus Status { get; init; }
    public PaymentState PaymentState { get; init; }
    public string PaymentStateName { get; init; } = "";
    public int CreatedByEmployeeId { get; init; }
    public string CreatedByName { get; init; } = "";
    public int? AssignedEmployeeId { get; init; }
    public string? AssignedEmployeeName { get; init; }
    public DateTime? DeadlineAtUtc { get; init; }
    public bool IsConfirmationOverdue { get; init; }
    public string ContactName { get; init; } = "";
    public string ContactPhone { get; init; } = "";
    public string? HomestayPhone { get; init; }
    public string OwnerPhone { get; init; } = "";
    public byte[] RowVer { get; init; } = [];
}
public sealed record BookingPaymentItem(int Id, string VoucherNo, PaymentType Type, decimal Amount, PaymentMethod Method,
    PaymentStatus Status, int RecordedByUserId, string RecordedByName, DateTime RecordedAtUtc, int? VerifiedByUserId,
    string? VerifiedByName, DateTime? VerifiedAtUtc, Guid? EvidenceFileId, bool HasEvidence, string? TransferContent)
{
    public DateTime? TransferredAtUtc { get; init; }
}
public sealed record BookingHistoryItem(long Id, BookingStatus? FromStatus, BookingStatus ToStatus, ActorType ActorType,
    string? ActorName, bool OnBehalfOfOwner, string? Note, DateTime ChangedAtUtc);
public sealed record BookingAuditItem(long Id, string ActionCode, string? ActorName, string? OldValues, string? NewValues, string? Note, DateTime CreatedAtUtc);
public sealed record BookingChangeItem(int Id, ChangeRequestType RequestType, ChangeRequestStatus Status, string Reason,
    DateTime RequestedAtUtc, DateTime? PaymentDeadlineAtUtc);
public sealed record BookingDetail
{
    public BookingTransferInstructions? TransferInstructions { get; init; }
    public BookingListItem Booking { get; init; } = null!;
    public CustomerItem Customer { get; init; } = null!;
    public int Guests { get; init; }
    public SourceChannel SourceChannel { get; init; }
    public string? SpecialRequests { get; init; }
    public string Address { get; init; } = "";
    public string? CheckinGuide { get; init; }
    public TimeOnly CheckinTime { get; init; }
    public TimeOnly CheckoutTime { get; init; }
    public bool OnlyProvideArrivalDetailsAfterConfirmation { get; init; } = true;
    public bool CanProvideArrivalDetails { get; init; }
    public DateTime HoldExpiresAtUtc { get; init; }
    public DateTime? ConfirmDeadlineAtUtc { get; init; }
    public IReadOnlyList<NightlyPrice> Nights { get; init; } = [];
    public decimal TotalAmount { get; init; }
    public decimal DepositRate { get; init; }
    public decimal DepositAmount { get; init; }
    public decimal DiscountAmount { get; init; }
    public int? VoucherId { get; init; }
    public string? VoucherCode { get; init; }
    public decimal AmountDue { get; init; }
    public decimal? CommissionRate { get; init; }
    public ApplicableCancellationPolicy CancellationPolicy { get; init; } = null!;
    public IReadOnlyList<BookingPaymentItem> Payments { get; init; } = [];
    public IReadOnlyList<BookingHistoryItem> History { get; init; } = [];
    public IReadOnlyList<BookingAuditItem> Audit { get; init; } = [];
    public IReadOnlyList<BookingChangeItem> ChangeRequests { get; init; } = [];
    public IReadOnlyList<BookingAction> AllowedActions { get; init; } = [];
}
public sealed record CreateBookingRequest
{
    public int? CustomerId { get; init; }
    public CreateCustomerRequest? QuickCustomer { get; init; }
    public int RoomTypeId { get; init; }
    public int? RoomId { get; init; }
    public DateOnly CheckIn { get; init; }
    public DateOnly CheckOut { get; init; }
    public int Guests { get; init; }
    public string ContactName { get; init; } = "";
    public string ContactPhone { get; init; } = "";
    public SourceChannel SourceChannel { get; init; }
    public string? SpecialRequests { get; init; }
    // Tạo mới chưa có RowVer; null cho lần đầu. Không dùng RowVer thay khóa RoomType.
    public byte[]? RowVer { get; init; }
}
public sealed record ClaimBookingRequest(int BookingId, byte[] RowVer);
public sealed record ReassignBookingRequest(int BookingId, int AssignedEmployeeId, byte[] RowVer);
public sealed record CancelUnpaidBookingRequest(int BookingId, string Reason, byte[] RowVer);
public sealed record BookingTransferInstructions(int BookingId, string Code, decimal AmountDue, FujiBankDetails Bank,
    string TransferContent, DateTime HoldExpiresAtUtc);
public sealed record QuickSearchResult(IReadOnlyList<BookingListItem> Bookings, IReadOnlyList<CustomerItem> Customers);
public sealed record DashboardQueueCount(BookingQueue Queue, int Count);
public sealed record DashboardUrgentItem(int? BookingId, string? BookingCode, string? CustomerName, string Work,
    DateTime DeadlineAtUtc, int? WorkTaskId);
public sealed record EmployeeDashboard(DateTime ServerNowUtc, DateOnly Today, IReadOnlyList<DashboardQueueCount> MyQueues,
    int ExpiringHolds, int OverdueConfirmations, int ArrivalsToday, int DeparturesToday, int DueTasks,
    int Unassigned, int UnreadNotifications, IReadOnlyList<DashboardUrgentItem> UrgentItems);
