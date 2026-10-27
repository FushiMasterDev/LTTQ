namespace Homestay.DTO.Payments;

// Pending Out/Refund only. All returned display/audit account values stay masked.
public sealed record UpdateRefundAccountRequest(int PaymentId, string PayeeAccount, string? PayeeName, byte[] RowVer);
public sealed record RefundAccountResult(int PaymentId, string? PayeeName, string MaskedPayeeAccount, byte[] RowVer);
