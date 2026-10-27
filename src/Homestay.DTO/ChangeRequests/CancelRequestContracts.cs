using Homestay.DTO.Bookings;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;

namespace Homestay.DTO.ChangeRequests;

public sealed record ChangeRequestSearchRequest
{
    public PageRequest Page { get; init; } = new();
    public ChangeRequestStatus? Status { get; init; }
    public int? BookingId { get; init; }
}
public sealed record CancelRequestItem
{
    public int Id { get; init; }
    public int BookingId { get; init; }
    public string BookingCode { get; init; } = "";
    public string CustomerName { get; init; } = "";
    public string HomestayName { get; init; } = "";
    public ChangeRequestType RequestType => ChangeRequestType.Cancel;
    public ChangeRequestStatus Status { get; init; }
    public string Reason { get; init; } = "";
    public decimal? QuotedRefundAmount { get; init; }
    public int RequestedByUserId { get; init; }
    public string RequestedByName { get; init; } = "";
    public DateTime RequestedAtUtc { get; init; }
    public int? DecidedByUserId { get; init; }
    public string? DecidedByName { get; init; }
    public DateTime? DecidedAtUtc { get; init; }
    public string? DecisionNote { get; init; }
    public bool IsRequestedByCurrentUser { get; init; }
    public bool CanReview { get; init; }
    public byte[] RowVer { get; init; } = [];
}
public sealed record CancelRequestDetail(CancelRequestItem Request, BookingDetail Booking);
public sealed record SubmitCancelRequest(int BookingId, string Reason, byte[] RowVer);
public sealed record ReviewCancelRequest(int RequestId, byte[] RowVer, byte[] BookingRowVer, string? Note = null);
public sealed record RejectCancelRequest(int RequestId, string Reason, byte[] RowVer, byte[] BookingRowVer);
