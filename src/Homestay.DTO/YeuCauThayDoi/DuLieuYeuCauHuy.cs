using Homestay.DTO.Bookings;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;

namespace Homestay.DTO.ChangeRequests;

/// <summary>Điều kiện tìm yêu cầu thay đổi: trạng thái, đơn.</summary>
public sealed record ChangeRequestSearchRequest
{
    public PageRequest Page { get; init; } = new();
    public ChangeRequestStatus? Status { get; init; }
    public int? BookingId { get; init; }
}
/// <summary>Một yêu cầu hủy đơn hiển thị trên danh sách chờ duyệt.</summary>
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
/// <summary>Chi tiết yêu cầu hủy kèm chi tiết đơn để quản lý xét duyệt.</summary>
public sealed record CancelRequestDetail(CancelRequestItem Request, BookingDetail Booking);
/// <summary>Yêu cầu nhân viên gửi đề nghị hủy đơn kèm lý do.</summary>
public sealed record SubmitCancelRequest(int BookingId, string Reason, byte[] RowVer);
/// <summary>Yêu cầu quản lý duyệt đề nghị hủy đơn.</summary>
public sealed record ReviewCancelRequest(int RequestId, byte[] RowVer, byte[] BookingRowVer, string? Note = null);
/// <summary>Yêu cầu quản lý từ chối đề nghị hủy đơn kèm lý do.</summary>
public sealed record RejectCancelRequest(int RequestId, string Reason, byte[] RowVer, byte[] BookingRowVer);
