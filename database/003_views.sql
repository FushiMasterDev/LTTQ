SET ANSI_NULLS ON;
GO
SET QUOTED_IDENTIFIER ON;
GO
SET ANSI_PADDING ON;
GO
SET ANSI_WARNINGS ON;
GO
SET ARITHABORT ON;
GO
SET CONCAT_NULL_YIELDS_NULL ON;
GO
SET NUMERIC_ROUNDABORT OFF;
GO
SET XACT_ABORT ON;
GO
IF EXISTS (SELECT 1 FROM dbo.SchemaVersions WHERE [Version] = 3)
    SET NOEXEC ON;
GO
BEGIN TRANSACTION;
GO
-- Hoàn tiền ưu tiên hơn thu cọc; gom từng nguồn trước để mỗi đơn chỉ có một dòng.
CREATE VIEW dbo.vw_BookingPaymentState
AS
WITH PaymentTotals AS
(
    SELECT BookingId,
        MAX(CASE WHEN Type = 'Deposit' AND Status = 'Pending' THEN 1 ELSE 0 END) AS HasPendingDeposit,
        MAX(CASE WHEN Type = 'Deposit' AND Status = 'Verified' THEN 1 ELSE 0 END) AS HasVerifiedDeposit,
        MAX(CASE WHEN Type = 'Refund' AND Status = 'Pending' THEN 1 ELSE 0 END) AS HasPendingRefund,
        MAX(CASE WHEN Type = 'Refund' AND Status = 'Verified' THEN 1 ELSE 0 END) AS HasVerifiedRefund,
        SUM(CASE WHEN Type = 'Deposit' AND Status = 'Verified' THEN Amount ELSE 0 END) AS VerifiedDepositAmount,
        SUM(CASE WHEN Type = 'AdditionalDeposit' AND Status = 'Verified' THEN Amount ELSE 0 END) AS VerifiedAdditionalDepositAmount,
        SUM(CASE WHEN Type = 'Refund' AND Status = 'Verified' THEN Amount ELSE 0 END) AS VerifiedRefundAmount
    FROM dbo.Payments
    GROUP BY BookingId
), OpenChanges AS
(
    SELECT BookingId, MAX(CASE WHEN Status = 'AwaitingPayment' THEN 1 ELSE 0 END) AS HasAwaitingChange
    FROM dbo.BookingChangeRequests
    GROUP BY BookingId
), States AS
(
    SELECT b.Id AS BookingId, b.Code AS BookingCode, b.Status AS BookingStatus,
        b.DepositAmount - b.DiscountAmount AS AmountDue,
        ISNULL(p.VerifiedDepositAmount, 0) AS VerifiedDepositAmount,
        ISNULL(p.VerifiedAdditionalDepositAmount, 0) AS VerifiedAdditionalDepositAmount,
        ISNULL(p.VerifiedRefundAmount, 0) AS VerifiedRefundAmount,
        CASE
            WHEN p.HasPendingRefund = 1 THEN 'RefundPending'
            WHEN p.HasVerifiedRefund = 1 THEN 'Refunded'
            WHEN c.HasAwaitingChange = 1 THEN 'AdditionalDepositPending'
            WHEN b.Status IN ('Cancelled', 'NoShow') AND p.HasVerifiedDeposit = 1 AND b.RefundAmount = 0 THEN 'DepositRetained'
            WHEN p.HasPendingDeposit = 1 THEN 'DepositPending'
            WHEN p.HasVerifiedDeposit = 1 THEN 'DepositVerified'
            WHEN b.Status IN ('Expired', 'Cancelled') THEN 'NoFinancialActivity'
            ELSE 'DepositNotCollected'
        END AS PaymentState
    FROM dbo.Bookings AS b
    LEFT JOIN PaymentTotals AS p ON p.BookingId = b.Id
    LEFT JOIN OpenChanges AS c ON c.BookingId = b.Id
)
SELECT BookingId, BookingCode, BookingStatus, AmountDue,
    VerifiedDepositAmount, VerifiedAdditionalDepositAmount, VerifiedRefundAmount, PaymentState,
    CASE PaymentState
        WHEN 'RefundPending' THEN N'Chờ hoàn tiền'
        WHEN 'Refunded' THEN
            CASE WHEN VerifiedRefundAmount >= AmountDue THEN N'Đã hoàn tiền (đủ)' ELSE N'Đã hoàn tiền (một phần)' END
        WHEN 'AdditionalDepositPending' THEN N'Chờ thu cọc bổ sung'
        WHEN 'DepositRetained' THEN N'Giữ cọc (không hoàn)'
        WHEN 'DepositPending' THEN N'Chờ xác minh cọc'
        WHEN 'DepositVerified' THEN N'Đã thu cọc'
        WHEN 'NoFinancialActivity' THEN N'Không phát sinh'
        ELSE N'Chưa thu cọc'
    END AS PaymentStateName,
    CASE WHEN PaymentState = 'Refunded'
        THEN CASE WHEN VerifiedRefundAmount >= AmountDue THEN 'Full' ELSE 'Partial' END
        ELSE NULL
    END AS RefundExtent
FROM States;
GO
CREATE VIEW dbo.vw_CashBook
AS
SELECT CAST('Payment' AS VARCHAR(20)) AS SourceType, p.Id AS SourceId,
    p.VerifiedAt AS EntryAt, CAST(p.VoucherNo AS VARCHAR(100)) AS ReferenceNo,
    CAST(p.Type AS VARCHAR(30)) AS EntryType,
    CAST(CASE WHEN p.Direction = 'In' THEN p.Amount ELSE 0 END AS DECIMAL(18,0)) AS IncomeAmount,
    CAST(CASE WHEN p.Direction = 'Out' THEN p.Amount ELSE 0 END AS DECIMAL(18,0)) AS ExpenseAmount,
    CAST(COALESCE(p.PayerOrPayeeName, c.FullName) AS NVARCHAR(200)) AS PartyName,
    p.RecordedByUserId AS RecordedByUserId, u.Username AS RecordedByUsername,
    p.BookingId, b.HomestayId, p.Note
FROM dbo.Payments AS p
INNER JOIN dbo.Bookings AS b ON b.Id = p.BookingId
INNER JOIN dbo.Customers AS c ON c.Id = b.CustomerId
INNER JOIN dbo.Users AS u ON u.Id = p.RecordedByUserId
WHERE p.Status = 'Verified'
UNION ALL
SELECT 'Settlement', s.Id, s.PaidAt,
    COALESCE(s.PaidBankRef, CONCAT('SET-', s.Id)), 'OwnerPayout',
    CAST(0 AS DECIMAL(18,0)), s.PayoutAmount,
    o.FullName, s.PaidByUserId, u.Username,
    CAST(NULL AS INT), s.HomestayId, s.OwnerFeedback
FROM dbo.Settlements AS s
INNER JOIN dbo.Homestays AS h ON h.Id = s.HomestayId
INNER JOIN dbo.Owners AS o ON o.Id = h.OwnerId
INNER JOIN dbo.Users AS u ON u.Id = s.PaidByUserId
WHERE s.Status = 'Paid'
UNION ALL
SELECT 'BankTransaction', bt.Id, bt.RefundedAt, bt.RefundBankRef, 'UnmatchedRefund',
    CAST(0 AS DECIMAL(18,0)), bt.Amount,
    bt.SenderName, bt.ProcessedByUserId, u.Username,
    bt.MatchedBookingId, b.HomestayId, bt.Note
FROM dbo.BankTransactions AS bt
LEFT JOIN dbo.Bookings AS b ON b.Id = bt.MatchedBookingId
LEFT JOIN dbo.Users AS u ON u.Id = bt.ProcessedByUserId
WHERE bt.MatchStatus = 'Refunded';
GO
CREATE VIEW dbo.vw_Payables
AS
SELECT CAST('Settlement' AS VARCHAR(20)) AS SourceType, s.Id AS SourceId,
    s.SentAt AS PayableAt,
    CAST(CONCAT('SET-', s.Id) AS VARCHAR(100)) AS ReferenceNo,
    CAST('OwnerPayout' AS VARCHAR(30)) AS PayableType,
    s.PayoutAmount AS Amount,
    CAST(o.FullName AS NVARCHAR(200)) AS PartyName,
    CAST(NULL AS INT) AS BookingId, s.HomestayId,
    s.SentByUserId AS RecordedByUserId, s.OwnerFeedback AS Note
FROM dbo.Settlements AS s
INNER JOIN dbo.Homestays AS h ON h.Id = s.HomestayId
INNER JOIN dbo.Owners AS o ON o.Id = h.OwnerId
WHERE s.Status = 'Sent'
UNION ALL
SELECT 'Payment', p.Id, p.RecordedAt, p.VoucherNo, p.Type,
    p.Amount, COALESCE(p.PayerOrPayeeName, c.FullName),
    p.BookingId, b.HomestayId, p.RecordedByUserId, p.Note
FROM dbo.Payments AS p
INNER JOIN dbo.Bookings AS b ON b.Id = p.BookingId
INNER JOIN dbo.Customers AS c ON c.Id = b.CustomerId
WHERE p.Direction = 'Out' AND p.Type IN ('Refund', 'Compensation') AND p.Status = 'Pending'
UNION ALL
-- Note chưa có cấu trúc: chỉ nhận diện chuyển dư khi số tiền vượt nghĩa vụ của phiếu đã khớp.
SELECT 'BankTransaction', bt.Id, bt.TransactionTime, bt.BankReference, 'ExcessTransfer',
    CAST(bt.Amount - due.ExpectedAmount AS DECIMAL(18,0)),
    bt.SenderName, p.BookingId, b.HomestayId, bt.CreatedByUserId, bt.Note
FROM dbo.BankTransactions AS bt
INNER JOIN dbo.Payments AS p ON p.BankTransactionId = bt.Id
INNER JOIN dbo.Bookings AS b ON b.Id = p.BookingId
LEFT JOIN dbo.BookingChangeRequests AS cr ON cr.Id = p.ChangeRequestId
CROSS APPLY
(
    SELECT CASE WHEN p.Type = 'AdditionalDeposit' THEN cr.AdditionalDeposit
        ELSE b.DepositAmount - b.DiscountAmount END AS ExpectedAmount
) AS due
WHERE bt.MatchStatus IN ('Matched', 'ManuallyMatched')
    AND bt.RefundedAt IS NULL
    AND p.Direction = 'In' AND p.Status = 'Verified'
    AND bt.Note IS NOT NULL AND LEN(LTRIM(RTRIM(bt.Note))) > 0
    AND due.ExpectedAmount IS NOT NULL AND bt.Amount > due.ExpectedAmount;
GO
INSERT INTO dbo.SchemaVersions ([Version], ScriptName, AppliedAt)
VALUES (3, N'003_views.sql', SYSUTCDATETIME());
GO
COMMIT TRANSACTION;
GO
SET NOEXEC OFF;
GO
