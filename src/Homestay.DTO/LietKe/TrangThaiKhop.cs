namespace Homestay.DTO.Enums;

/// <summary>Trạng thái khớp giao dịch ngân hàng với đơn: chưa khớp, khớp tự động/thủ công, thiếu tiền, trễ hạn, đã hoàn, bỏ qua.</summary>
public enum MatchStatus
{
    Unmatched,
    Matched,
    ManuallyMatched,
    Underpaid,
    LatePayment,
    Refunded,
    Ignored,
}
