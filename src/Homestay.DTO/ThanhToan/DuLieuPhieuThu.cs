using Homestay.DTO.Bookings;
using Homestay.DTO.Enums;

namespace Homestay.DTO.Payments;

/// <summary>Dữ liệu nhập phiếu thu cọc: số tiền, thời điểm chuyển, người chuyển, nội dung, ảnh chứng từ.</summary>
public sealed record RecordReceiptRequest
{
    public int BookingId { get; init; }
    public string BookingCode { get; init; } = "";
    public decimal Amount { get; init; }
    public DateTime TransferredAtUtc { get; init; }
    public PaymentMethod Method { get; init; }
    public string? PayerName { get; init; }
    public string? PayerAccount { get; init; }
    public string? TransferContent { get; init; }
    public byte[]? EvidenceBytes { get; init; }
    public string? EvidenceFileName { get; init; }
    public byte[] RowVer { get; init; } = [];
}
/// <summary>Kết quả ghi phiếu thu: đơn sau khi cập nhật, phiếu vừa tạo và cảnh báo nếu thiếu tiền.</summary>
public sealed record RecordReceiptResult(BookingDetail Booking, BookingPaymentItem Receipt)
{
    public string VoucherNo => Receipt.VoucherNo;
    public BookingStatus Status => Booking.Booking.Status;
    public bool IsInsufficient { get; init; }
    public string? Warning { get; init; }
}
