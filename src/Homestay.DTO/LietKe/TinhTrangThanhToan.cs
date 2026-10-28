namespace Homestay.DTO.Enums;

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
