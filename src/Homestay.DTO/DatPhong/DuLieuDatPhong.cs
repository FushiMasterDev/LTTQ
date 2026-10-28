using Homestay.DTO.Availability;
using Homestay.DTO.Common;
using Homestay.DTO.Customers;
using Homestay.DTO.Enums;

namespace Homestay.DTO.Bookings;

/// <summary>Các hàng đợi đơn trên màn hình nhân viên (của tôi, chưa giao, chờ thanh toán, chờ xác nhận, đang ở…).</summary>
public enum BookingQueue { Mine, Unassigned, AwaitingPayment, DepositVerification, OwnerConfirmation, Upcoming, InHouse, ArrivalsToday, DeparturesToday, OpenChangeRequests, All }
// Append-only: preserve the numeric values used by the existing GUI.
/// <summary>Các thao tác có thể làm trên một đơn (ghi cọc, nhận đơn, giao lại, xác nhận thay chủ, nhận/trả phòng, hủy…).</summary>
public enum BookingAction { RecordDeposit, CancelUnpaid, Claim, Reassign, ConfirmOnBehalf, RejectOnBehalf, CheckIn, CheckOut, MarkNoShow, CancelHomestayUnavailable, SubmitCancel }
/// <summary>Điều kiện tìm đơn: hàng đợi, mã, khách, homestay, khu vực, người phụ trách, ngày nhận phòng, trạng thái.</summary>
public sealed record BookingSearchRequest
{
    public PageRequest Page { get; init; } = new();
    public BookingQueue Queue { get; init; } = BookingQueue.Mine;
    public string? Code { get; init; }
    public string? Phone { get; init; }
    public string? CustomerName { get; init; }
    public int? HomestayId { get; init; }
    public int? AreaId { get; init; }
    public int? AssignedEmployeeId { get; init; }
    public DateOnly? CheckInFrom { get; init; }
    public DateOnly? CheckInTo { get; init; }
    public BookingStatus? Status { get; init; }
    public bool OnlyExpiringHolds { get; init; }
    public bool OnlyOverdueConfirmation { get; init; }
    public bool AssignedToMeOnly { get; init; }
}
/// <summary>Một đơn đặt phòng hiển thị trên danh sách.</summary>
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
    public DateTime? ConfirmDeadlineAtUtc { get; init; }
    public bool IsConfirmationOverdue { get; init; }
    public string ContactName { get; init; } = "";
    public string ContactPhone { get; init; } = "";
    public string? HomestayPhone { get; init; }
    public string OwnerPhone { get; init; } = "";
    public byte[] RowVer { get; init; } = [];
}
/// <summary>Một khoản thanh toán của đơn (cọc, hoàn, bồi thường…) hiển thị trong chi tiết đơn.</summary>
public sealed record BookingPaymentItem(int Id, string VoucherNo, PaymentType Type, decimal Amount, PaymentMethod Method,
    PaymentStatus Status, int RecordedByUserId, string RecordedByName, DateTime RecordedAtUtc, int? VerifiedByUserId,
    string? VerifiedByName, DateTime? VerifiedAtUtc, Guid? EvidenceFileId, bool HasEvidence, string? TransferContent)
{
    public DateTime? TransferredAtUtc { get; init; }
}
/// <summary>Một dòng lịch sử đổi trạng thái của đơn.</summary>
public sealed record BookingHistoryItem(long Id, BookingStatus? FromStatus, BookingStatus ToStatus, ActorType ActorType,
    string? ActorName, bool OnBehalfOfOwner, string? Note, DateTime ChangedAtUtc);
/// <summary>Một dòng nhật ký thao tác trên đơn.</summary>
public sealed record BookingAuditItem(long Id, string ActionCode, string? ActorName, string? OldValues, string? NewValues, string? Note, DateTime CreatedAtUtc);
/// <summary>Một yêu cầu thay đổi (đổi ngày, đổi loại phòng, hủy) gắn với đơn.</summary>
public sealed record BookingChangeItem(int Id, ChangeRequestType RequestType, ChangeRequestStatus Status, string Reason,
    DateTime RequestedAtUtc, DateTime? PaymentDeadlineAtUtc);
/// <summary>Toàn bộ chi tiết một đơn: thông tin đơn, khách, thanh toán, lịch sử, nhật ký, hướng dẫn chuyển khoản và nhận phòng.</summary>
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
/// <summary>Dữ liệu nhập để tạo đơn giữ phòng mới.</summary>
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
/// <summary>Yêu cầu nhân viên tự nhận phụ trách một đơn chưa giao.</summary>
public sealed record ClaimBookingRequest(int BookingId, byte[] RowVer);
/// <summary>Yêu cầu giao lại đơn cho nhân viên khác.</summary>
public sealed record ReassignBookingRequest(int BookingId, int AssignedEmployeeId, byte[] RowVer);
/// <summary>Yêu cầu hủy đơn chưa thanh toán cọc kèm lý do.</summary>
public sealed record CancelUnpaidBookingRequest(int BookingId, string Reason, byte[] RowVer);
/// <summary>Hướng dẫn chuyển khoản cọc gửi khách: số tiền, ngân hàng nhận, nội dung chuyển khoản và hạn chót.</summary>
public sealed record BookingTransferInstructions(int BookingId, string Code, decimal AmountDue, FujiBankDetails Bank,
    string TransferContent, DateTime HoldExpiresAtUtc);
/// <summary>Kết quả tìm nhanh: các đơn và khách hàng khớp từ khóa.</summary>
public sealed record QuickSearchResult(IReadOnlyList<BookingListItem> Bookings, IReadOnlyList<CustomerItem> Customers);
/// <summary>Số đơn trong một hàng đợi, hiển thị trên bảng điều khiển.</summary>
public sealed record DashboardQueueCount(BookingQueue Queue, int Count);
/// <summary>Một việc gấp trên bảng điều khiển (đơn nào, việc gì, hạn khi nào).</summary>
public sealed record DashboardUrgentItem(int? BookingId, string? BookingCode, string? CustomerName, string Work,
    DateTime DeadlineAtUtc, int? WorkTaskId);
/// <summary>Số liệu bảng điều khiển của nhân viên: số đơn theo hàng đợi và các việc gấp.</summary>
public sealed record EmployeeDashboard(DateTime ServerNowUtc, DateOnly Today, IReadOnlyList<DashboardQueueCount> MyQueues,
    int ExpiringHolds, int OverdueConfirmations, int ArrivalsToday, int DeparturesToday, int DueTasks,
    int Unassigned, int UnreadNotifications, IReadOnlyList<DashboardUrgentItem> UrgentItems);
