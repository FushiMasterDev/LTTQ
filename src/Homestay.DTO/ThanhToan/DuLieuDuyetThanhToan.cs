using Homestay.DTO.Bookings;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;

namespace Homestay.DTO.Payments;

/// <summary>Điều kiện lọc hàng chờ phiếu thu/hoàn tiền của kế toán.</summary>
public sealed record PaymentQueueRequest
{
    public PageRequest Page { get; init; } = new();
    public int? BookingId { get; init; }
    public PaymentMethod? Method { get; init; }
}
/// <summary>Một phiếu thu hoặc khoản hoàn trong hàng chờ kế toán.</summary>
public sealed record PaymentQueueItem
{
    public int Id { get; init; }
    public string VoucherNo { get; init; } = "";
    public int BookingId { get; init; }
    public string BookingCode { get; init; } = "";
    public string CustomerName { get; init; } = "";
    public string HomestayName { get; init; } = "";
    public BookingStatus BookingStatus { get; init; }
    public string? CancelReason { get; init; }
    public string? BankRef { get; init; }
    public DateTime? PaidAtUtc { get; init; }
    public int? VerifiedByUserId { get; init; }
    public string? RejectReason { get; init; }
    public string? Note { get; init; }
    public PaymentType Type { get; init; }
    public PaymentStatus Status { get; init; }
    public PaymentMethod Method { get; init; }
    public decimal Amount { get; init; }
    public decimal AmountDue { get; init; }
    public DateTime? TransferredAtUtc { get; init; }
    public DateTime RecordedAtUtc { get; init; }
    public int RecordedByUserId { get; init; }
    public string RecordedByName { get; init; } = "";
    public bool IsRecordedByCurrentUser { get; init; }
    public Guid? EvidenceFileId { get; init; }
    public string? PayerOrPayeeName { get; init; }
    // Only the refund queue includes the full destination account, needed to pay the customer.
    public string? RefundAccount { get; init; }
    public string? MaskedPayerAccount { get; init; }
    public string? TransferContent { get; init; }
    public byte[] RowVer { get; init; } = [];
}
/// <summary>Chi tiết để kế toán duyệt phiếu thu: phiếu, đơn, chứng từ, lần từ chối thứ mấy, có thiếu tiền không.</summary>
public sealed record ReceiptVerificationDetail(PaymentQueueItem Receipt, BookingDetail Booking,
    int ReceiptRejectCount, int MaxReceiptRejections)
{
    public int RejectionNumber => ReceiptRejectCount + 1;
    public bool IsInsufficient => Receipt.Amount < Receipt.AmountDue;
    public bool CanVerify => Receipt.Status == PaymentStatus.Pending && Booking.Booking.Status == BookingStatus.PaymentSubmitted && !Receipt.IsRecordedByCurrentUser && !IsInsufficient;
    public bool CanReject => Receipt.Status == PaymentStatus.Pending;
}
/// <summary>Yêu cầu kế toán xác nhận phiếu thu hợp lệ.</summary>
public sealed record VerifyReceiptRequest(int PaymentId, byte[] RowVer, byte[] BookingRowVer);
/// <summary>Yêu cầu kế toán từ chối phiếu thu kèm lý do.</summary>
public sealed record RejectReceiptRequest(int PaymentId, string Reason, byte[] RowVer, byte[] BookingRowVer);
/// <summary>Yêu cầu ghi nhận đã chuyển tiền hoàn cho khách kèm chứng từ.</summary>
public sealed record RecordRefundPaidRequest
{
    public int PaymentId { get; init; }
    public byte[] RowVer { get; init; } = [];
    public string BankRef { get; init; } = "";
    public DateTime PaidAtUtc { get; init; }
    public byte[]? EvidenceBytes { get; init; }
    public string? EvidenceFileName { get; init; }
}
