using Homestay.DTO.Enums;

namespace Homestay.DTO.Finance;

public sealed record SettlementCalculationInput(int BookingId, string BookingCode, BookingStatus Status,
    DateOnly CheckOut, bool Replaced, decimal TotalAmount, decimal AmountDue, decimal DepositAmount,
    decimal? RefundAmount, decimal ChangeFeeAmount, decimal DiscountAmount, decimal CommissionRate,
    bool HasVerifiedDeposit);
public sealed record SettlementLine(int BookingId, string BookingCode, SettlementItemType ItemType,
    decimal RoomAmount, decimal DepositAmount, decimal RefundAmount, decimal RetainedAmount,
    decimal CommissionAmount, decimal DiscountAmount, decimal PayoutAmount, decimal CommissionRate)
{ public DateOnly CheckOut { get; init; } }
public sealed record SettlementTotals(decimal TotalRoomAmount, decimal TotalRetained, decimal TotalCommission,
    decimal TotalDiscount, decimal PayoutAmount);
public sealed record SettlementDraftResult(int Id, int HomestayId, int Year, int Month, int PeriodNo,
    SettlementStatus Status, SettlementTotals Totals, IReadOnlyList<SettlementLine> Items,
    IReadOnlyList<string> UnfinalizedBookingCodes, byte[] RowVer)
{ public string HomestayName { get; init; } = ""; public string OwnerName { get; init; } = ""; public string? OwnerFeedback { get; init; } public IReadOnlyList<SettlementAuditItem> History { get; init; } = [];
  public string? OwnerBankName { get; init; } public string? OwnerBankAccountNo { get; init; } public string? OwnerBankAccountName { get; init; } }
public sealed record SettlementListItem(int Id, int HomestayId, string HomestayName, string OwnerName,
    int Year, int Month, int PeriodNo, SettlementStatus Status, SettlementTotals Totals,
    DateTime? SentAtUtc, DateTime? PaidAtUtc, byte[] RowVer);
public sealed record BankTransactionItem(int Id, string AccountNumber, string BankReference, decimal Amount,
    string? TransferContent, string? SenderName, string? SenderAccount, DateTime TransactionTimeUtc,
    MatchStatus MatchStatus, int? MatchedBookingId, string? BookingCode, string? Note, byte[] RowVer);
public sealed record RecordBankTransactionRequest(string AccountNumber, string BankReference, decimal Amount,
    string? TransferContent, string? SenderName, string? SenderAccount, DateTime TransactionTimeUtc);
public sealed record MatchBankTransactionRequest(int TransactionId, int BookingId, byte[] RowVer);
public sealed record ProcessBankTransactionRequest(int TransactionId, byte[] RowVer, string Note);
public sealed record RefundBankTransactionRequest(int TransactionId, byte[] RowVer, string RefundBankRef, DateTime RefundedAtUtc, string? Note);
public sealed record CashBookItem(string SourceType, int SourceId, DateTime EntryAtUtc, string ReferenceNo,
    string EntryType, decimal IncomeAmount, decimal ExpenseAmount, string? PartyName, string? RecordedByUsername,
    int? BookingId, int? HomestayId, string? Note);
public sealed record PayableItem(string SourceType, int SourceId, DateTime PayableAtUtc, string ReferenceNo,
    string PayableType, decimal Amount, string? PartyName, int? BookingId, int? HomestayId, string? Note);
public sealed record AccountantDashboard(int UnmatchedTransactions, int UnderpaidTransactions,
    int PendingDeposits, int PendingRefunds, int SentSettlements, decimal OutstandingPayables);
public sealed record RecordSettlementPaidRequest(int SettlementId, byte[] RowVer, string? BankReference);
public sealed record SettlementFeedbackRequest(int SettlementId, byte[] RowVer, string Feedback);
public sealed record SettlementAuditItem(DateTime AtUtc,string Action,string? ActorName,string? Note);
public sealed record SettlementReasonRequest(int SettlementId, byte[] RowVer, string Reason);
