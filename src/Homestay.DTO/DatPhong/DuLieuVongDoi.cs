using Homestay.DTO.Availability;

namespace Homestay.DTO.Bookings;

public enum RefundReason { CustomerCancellation, OwnerRejected, HomestayUnavailable, NoShow, Completed, UnpaidCancellation }

// CancelledOn is a Vietnam calendar date, supplied by the caller of the pure calculator.
public sealed record RefundCalculationRequest(DateOnly CheckIn, DateOnly CancelledOn, decimal AmountDue,
    bool HasCollectedDeposit, ApplicableCancellationPolicy Policy)
{
    public RefundReason Reason { get; init; } = RefundReason.CustomerCancellation;
    public decimal? TotalAmount { get; init; }
    public decimal? CommissionRate { get; init; }
}
public sealed record RefundCalculation(int CancellationPolicyId, int DaysBefore, int RefundPercent,
    decimal RefundAmount, decimal RetainedAmount, decimal? CommissionAmount, decimal? OwnerPayoutAmount);
public sealed record BookingRefundPreview(int BookingId, DateTime ServerNowUtc, DateOnly CancellationDate,
    byte[] RowVer, RefundCalculation Calculation);
public sealed record ConfirmOnBehalfRequest(int BookingId, string Note, byte[] RowVer, int? RoomId = null);
public sealed record RejectOnBehalfRequest(int BookingId, string Reason, byte[] RowVer);
public sealed record StayActionRequest(int BookingId, string Note, byte[] RowVer);
public sealed record CancelHomestayUnavailableRequest(int BookingId, string Reason, byte[] RowVer);
