namespace Homestay.DTO.Enums;

/// <summary>Tình trạng tiền của một đơn (tổng hợp): chờ cọc, đã xác nhận cọc, chờ hoàn, đã hoàn, giữ cọc…</summary>
public enum PaymentState
{
    RefundPending,
    Refunded,
    AdditionalDepositPending,
    DepositRetained,
    DepositPending,
    DepositVerified,
    NoFinancialActivity,
    DepositNotCollected,
}
