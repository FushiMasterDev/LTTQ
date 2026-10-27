using Homestay.DAL.Infrastructure;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.DTO.Payments;
using Microsoft.Data.SqlClient;

namespace Homestay.DAL.Repositories;

public sealed partial class PaymentRepository
{
    private const string ReviewFrom = """
         FROM dbo.Payments p JOIN dbo.Bookings b ON b.Id=p.BookingId JOIN dbo.Homestays h ON h.Id=b.HomestayId
         JOIN dbo.Customers c ON c.Id=b.CustomerId JOIN dbo.Users u ON u.Id=p.RecordedByUserId
         LEFT JOIN dbo.Employees e ON e.UserId=u.Id
        """;
    private const string ReviewColumns = """
        p.Id,p.VoucherNo,p.BookingId,b.Code AS BookingCode,c.FullName AS CustomerName,h.Name AS HomestayName,
        p.Type,p.Status,p.Method,p.Amount,b.DepositAmount-b.DiscountAmount AS AmountDue,p.TransferredAt,p.RecordedAt,
        p.RecordedByUserId,COALESCE(e.FullName,u.Username) AS RecordedByName,p.EvidenceFileId,p.PayerOrPayeeName,
        p.PayerOrPayeeAccount,p.TransferContent,p.RowVer,b.Status AS BookingStatus,b.CancelReason,
        p.BankRef,p.VerifiedAt,p.VerifiedByUserId,p.RejectReason,p.Note
        """;
    private static PaymentQueueItem MapReview(SqlDataReader r, int userId) => new()
    {
        Id = r.GetInt32("Id"), VoucherNo = r.GetString("VoucherNo"), BookingId = r.GetInt32("BookingId"),
        BookingCode = r.GetString("BookingCode"), CustomerName = r.GetString("CustomerName"), HomestayName = r.GetString("HomestayName"),
        BookingStatus = r.GetEnum<BookingStatus>("BookingStatus"), CancelReason = r.GetNullableString("CancelReason"),
        BankRef = r.GetNullableString("BankRef"), PaidAtUtc = r.GetEnum<PaymentType>("Type") == PaymentType.Refund ? r.GetNullableUtcDateTime("VerifiedAt") : null,
        VerifiedByUserId = r.GetNullableInt32("VerifiedByUserId"), RejectReason = r.GetNullableString("RejectReason"), Note = r.GetNullableString("Note"),
        Type = r.GetEnum<PaymentType>("Type"), Status = r.GetEnum<PaymentStatus>("Status"), Method = r.GetEnum<PaymentMethod>("Method"),
        Amount = r.GetDecimal("Amount"), AmountDue = r.GetDecimal("AmountDue"), TransferredAtUtc = r.GetNullableUtcDateTime("TransferredAt"),
        RecordedAtUtc = r.GetUtcDateTime("RecordedAt"), RecordedByUserId = r.GetInt32("RecordedByUserId"), RecordedByName = r.GetString("RecordedByName"),
        IsRecordedByCurrentUser = r.GetInt32("RecordedByUserId") == userId,
        EvidenceFileId = r.IsDBNull(r.GetOrdinal("EvidenceFileId")) ? null : r.GetGuid(r.GetOrdinal("EvidenceFileId")),
        PayerOrPayeeName = r.GetNullableString("PayerOrPayeeName"), TransferContent = r.GetNullableString("TransferContent"),
        RefundAccount = r.GetEnum<PaymentType>("Type") == PaymentType.Refund ? r.GetNullableString("PayerOrPayeeAccount") : null,
        MaskedPayerAccount = MaskAccount(r.GetNullableString("PayerOrPayeeAccount")), RowVer = r.GetRowVersion("RowVer")
    };
    private static string? MaskAccount(string? value) => string.IsNullOrEmpty(value) ? value : new string('•', Math.Max(4, value.Length - 4)) + value[^Math.Min(4, value.Length)..];

    public async Task<PagedResult<PaymentQueueItem>> ListPendingAsync(DbSession db, PaymentQueueRequest request, bool refunds,
        int userId, string condition, Action<SqlCommand> addScope, CancellationToken ct)
    {
        var where = $"""
             WHERE ({condition}) AND p.Status='Pending' AND p.Type=@type AND p.Direction=@direction
             AND (@booking IS NULL OR p.BookingId=@booking) AND (@method IS NULL OR p.Method=@method)
             AND (@search=N'' OR p.VoucherNo LIKE @search ESCAPE '~' OR b.Code LIKE @search ESCAPE '~'
                 OR c.FullName COLLATE Vietnamese_CI_AI LIKE @search ESCAPE '~' OR h.Name COLLATE Vietnamese_CI_AI LIKE @search ESCAPE '~')
            """;
        void Parameters(SqlCommand command)
        {
            addScope(command);
            command.Parameters.AddVarChar("@type", 20, refunds ? "Refund" : "Deposit");
            command.Parameters.AddVarChar("@direction", 3, refunds ? "Out" : "In");
            command.Parameters.AddInt32("@booking", request.BookingId);
            command.Parameters.AddEnum("@method", 20, request.Method);
            command.Parameters.AddNVarChar("@search", 500, StaffReadSql.Like(request.Page.SearchText));
        }
        using var count = db.CreateCommand("SELECT COUNT(*)" + ReviewFrom + where); Parameters(count);
        var total = (int)(await db.ScalarAsync(count, ct).ConfigureAwait(false))!;
        var sort = request.Page.SortColumn switch
        {
            "VoucherNo" => "p.VoucherNo", "BookingCode" => "b.Code", "Amount" => "p.Amount", _ => "p.RecordedAt"
        };
        using var command = db.CreateCommand($"SELECT {ReviewColumns}{ReviewFrom}{where} ORDER BY {sort} {(request.Page.SortDescending ? "DESC" : "ASC")},p.Id OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY;");
        Parameters(command); StaffReadSql.PageParameters(command, request.Page);
        return new() { TotalCount = total, PageNumber = request.Page.PageNumber, PageSize = request.Page.PageSize,
            Items = await db.QueryAsync(command, r => MapReview(r, userId), ct).ConfigureAwait(false) };
    }
    public async Task<PaymentQueueItem?> GetReceiptAsync(DbSession db, int paymentId, int userId,
        string condition, Action<SqlCommand> addScope, CancellationToken ct)
    {
        using var command = db.CreateCommand($"SELECT {ReviewColumns}{ReviewFrom} WHERE p.Id=@id AND p.Direction='In' AND p.Type='Deposit' AND ({condition});");
        command.Parameters.AddInt32("@id", paymentId); addScope(command);
        return (await db.QueryAsync(command, r => MapReview(r, userId), ct).ConfigureAwait(false)).SingleOrDefault();
    }
    public async Task<PaymentQueueItem?> GetRefundAsync(DbSession db, int paymentId, int userId,
        string condition, Action<SqlCommand> addScope, CancellationToken ct)
    {
        using var command = db.CreateCommand($"SELECT {ReviewColumns}{ReviewFrom} WHERE p.Id=@id AND p.Direction='Out' AND p.Type='Refund' AND ({condition});");
        command.Parameters.AddInt32("@id", paymentId); addScope(command);
        return (await db.QueryAsync(command, r => MapReview(r, userId), ct).ConfigureAwait(false)).SingleOrDefault();
    }
    public async Task<int> GetReceiptRejectCountAsync(DbSession db, int bookingId, CancellationToken ct)
    {
        using var command = db.CreateCommand("SELECT ReceiptRejectCount FROM dbo.Bookings WHERE Id=@id;");
        command.Parameters.AddInt32("@id", bookingId);
        return Convert.ToInt32(await db.ScalarAsync(command, ct).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
    }
}
