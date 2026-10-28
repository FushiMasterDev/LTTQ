using Homestay.DTO.Enums;

namespace Homestay.DTO.Finance;

/// <summary>Dữ liệu một đơn đưa vào công thức đối soát (trạng thái, tiền phòng, tiền giữ lại, hoa hồng…).</summary>
public sealed record SettlementCalculationInput(int BookingId, string BookingCode, BookingStatus Status,
    DateOnly CheckOut, bool Replaced, decimal TotalAmount, decimal AmountDue, decimal DepositAmount,
    decimal? RefundAmount, decimal ChangeFeeAmount, decimal DiscountAmount, decimal CommissionRate,
    bool HasVerifiedDeposit);
/// <summary>Một dòng kết quả đối soát cho một đơn: loại dòng, tiền phòng, hoa hồng, tiền trả chủ.</summary>
public sealed record SettlementLine(int BookingId, string BookingCode, SettlementItemType ItemType,
    decimal RoomAmount, decimal DepositAmount, decimal RefundAmount, decimal RetainedAmount,
    decimal CommissionAmount, decimal DiscountAmount, decimal PayoutAmount, decimal CommissionRate)
{ public DateOnly CheckOut { get; init; } }
/// <summary>Các tổng tiền của bảng đối soát: tiền phòng, tiền giữ lại, hoa hồng, tiền phải trả chủ.</summary>
public sealed record SettlementTotals(decimal TotalRoomAmount, decimal TotalRetained, decimal TotalCommission,
    decimal TotalDiscount, decimal PayoutAmount);
/// <summary>Bảng đối soát (nháp hoặc đã gửi) đầy đủ: kỳ, chủ nhà, tài khoản nhận, các dòng và tổng tiền.</summary>
public sealed record SettlementDraftResult(int Id, int HomestayId, int Year, int Month, int PeriodNo,
    SettlementStatus Status, SettlementTotals Totals, IReadOnlyList<SettlementLine> Items,
    IReadOnlyList<string> UnfinalizedBookingCodes, byte[] RowVer)
{ public string HomestayName { get; init; } = ""; public string OwnerName { get; init; } = ""; public string? OwnerFeedback { get; init; } public IReadOnlyList<SettlementAuditItem> History { get; init; } = [];
  public string? OwnerBankName { get; init; } public string? OwnerBankAccountNo { get; init; } public string? OwnerBankAccountName { get; init; } }
/// <summary>Một bảng đối soát hiển thị trên danh sách.</summary>
public sealed record SettlementListItem(int Id, int HomestayId, string HomestayName, string OwnerName,
    int Year, int Month, int PeriodNo, SettlementStatus Status, SettlementTotals Totals,
    DateTime? SentAtUtc, DateTime? PaidAtUtc, byte[] RowVer);
/// <summary>Một giao dịch trên sao kê ngân hàng và trạng thái khớp với đơn.</summary>
public sealed record BankTransactionItem(int Id, string AccountNumber, string BankReference, decimal Amount,
    string? TransferContent, string? SenderName, string? SenderAccount, DateTime TransactionTimeUtc,
    MatchStatus MatchStatus, int? MatchedBookingId, string? BookingCode, string? Note, byte[] RowVer);
/// <summary>Dữ liệu nhập để ghi một giao dịch ngân hàng.</summary>
public sealed record RecordBankTransactionRequest(string AccountNumber, string BankReference, decimal Amount,
    string? TransferContent, string? SenderName, string? SenderAccount, DateTime TransactionTimeUtc);
/// <summary>Yêu cầu khớp thủ công giao dịch ngân hàng với một đơn.</summary>
public sealed record MatchBankTransactionRequest(int TransactionId, int BookingId, byte[] RowVer);
/// <summary>Yêu cầu xử lý giao dịch ngân hàng (ví dụ bỏ qua) kèm ghi chú.</summary>
public sealed record ProcessBankTransactionRequest(int TransactionId, byte[] RowVer, string Note);
/// <summary>Yêu cầu ghi nhận đã chuyển trả lại một giao dịch không khớp.</summary>
public sealed record RefundBankTransactionRequest(int TransactionId, byte[] RowVer, string RefundBankRef, DateTime RefundedAtUtc, string? Note);
/// <summary>Một dòng sổ quỹ: thu/chi từ nguồn nào, số tham chiếu, số tiền.</summary>
public sealed record CashBookItem(string SourceType, int SourceId, DateTime EntryAtUtc, string ReferenceNo,
    string EntryType, decimal IncomeAmount, decimal ExpenseAmount, string? PartyName, string? RecordedByUsername,
    int? BookingId, int? HomestayId, string? Note);
/// <summary>Một khoản phải chi (hoàn tiền khách, trả chủ nhà) còn chờ thanh toán.</summary>
public sealed record PayableItem(string SourceType, int SourceId, DateTime PayableAtUtc, string ReferenceNo,
    string PayableType, decimal Amount, string? PartyName, int? BookingId, int? HomestayId, string? Note);
/// <summary>Số liệu tổng quan cho kế toán: giao dịch chưa khớp, thiếu tiền, phiếu chờ duyệt, khoản chờ chi…</summary>
public sealed record AccountantDashboard(int UnmatchedTransactions, int UnderpaidTransactions,
    int PendingDeposits, int PendingRefunds, int SentSettlements, decimal OutstandingPayables);
/// <summary>Yêu cầu ghi nhận đã chuyển tiền đối soát cho chủ nhà.</summary>
public sealed record RecordSettlementPaidRequest(int SettlementId, byte[] RowVer, string? BankReference);
/// <summary>Yêu cầu ghi phản hồi của chủ nhà về bảng đối soát.</summary>
public sealed record SettlementFeedbackRequest(int SettlementId, byte[] RowVer, string Feedback);
/// <summary>Một dòng lịch sử thao tác trên bảng đối soát.</summary>
public sealed record SettlementAuditItem(DateTime AtUtc,string Action,string? ActorName,string? Note);
/// <summary>Yêu cầu thao tác trên bảng đối soát cần lý do (ví dụ mở lại).</summary>
public sealed record SettlementReasonRequest(int SettlementId, byte[] RowVer, string Reason);
