namespace Homestay.DTO.Payments;

// Pending Out/Refund only. All returned display/audit account values stay masked.
/// <summary>Yêu cầu cập nhật tài khoản nhận hoàn tiền của khách.</summary>
public sealed record UpdateRefundAccountRequest(int PaymentId, string PayeeAccount, string? PayeeName, byte[] RowVer);
/// <summary>Kết quả cập nhật tài khoản nhận hoàn (số tài khoản đã che bớt).</summary>
public sealed record RefundAccountResult(int PaymentId, string? PayeeName, string MaskedPayeeAccount, byte[] RowVer);
