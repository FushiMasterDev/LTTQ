using Homestay.DAL.Infrastructure;
using Homestay.DTO.Enums;
using Homestay.DTO.Finance;
using Microsoft.Data.SqlClient;

namespace Homestay.DAL.Repositories;

public sealed class FinanceRepository
{
    public async Task<IReadOnlyList<BankTransactionItem>> ListBankTransactionsAsync(DbSession db, DateTime from, DateTime to, string? status, string search, CancellationToken ct)
    {
        using var cmd=db.CreateCommand("""
            SELECT TOP (1000) bt.Id,bt.AccountNumber,bt.BankReference,bt.Amount,bt.TransferContent,bt.SenderName,bt.SenderAccount,bt.TransactionTime,bt.MatchStatus,bt.MatchedBookingId,b.Code AS BookingCode,bt.Note,bt.RowVer
            FROM dbo.BankTransactions bt LEFT JOIN dbo.Bookings b ON b.Id=bt.MatchedBookingId
            WHERE bt.TransactionTime>=@from AND bt.TransactionTime<@to AND (@status IS NULL OR bt.MatchStatus=@status)
              AND (@search=N'' OR bt.BankReference LIKE @search ESCAPE '~' OR bt.TransferContent LIKE @search ESCAPE '~' OR bt.SenderName COLLATE Vietnamese_CI_AI LIKE @search ESCAPE '~' OR b.Code LIKE @search ESCAPE '~')
            ORDER BY bt.TransactionTime DESC,bt.Id DESC;
            """);
        cmd.Parameters.AddUtcDateTime("@from",from);cmd.Parameters.AddUtcDateTime("@to",to);cmd.Parameters.AddVarChar("@status",20,status);
        cmd.Parameters.AddNVarChar("@search",500,StaffReadSql.Like(search));
        return await db.QueryAsync(cmd,r=>new BankTransactionItem(r.GetInt32("Id"),r.GetString("AccountNumber"),r.GetString("BankReference"),r.GetDecimal("Amount"),r.GetNullableString("TransferContent"),r.GetNullableString("SenderName"),r.GetNullableString("SenderAccount"),r.GetUtcDateTime("TransactionTime"),r.GetEnum<MatchStatus>("MatchStatus"),r.GetNullableInt32("MatchedBookingId"),r.GetNullableString("BookingCode"),r.GetNullableString("Note"),r.GetRowVersion("RowVer")),ct).ConfigureAwait(false);
    }

    public async Task<int> InsertBankTransactionAsync(UnitOfWork db, RecordBankTransactionRequest x, int user, DateTime now, CancellationToken ct)
    {
        using var c=db.CreateCommand("INSERT dbo.BankTransactions(Source,AccountNumber,BankReference,Amount,TransferContent,SenderName,SenderAccount,TransactionTime,MatchStatus,CreatedByUserId,CreatedAt) VALUES('Manual',@account,@ref,@amount,@content,@name,@sender,@time,'Unmatched',@user,@now);SELECT CONVERT(int,SCOPE_IDENTITY());");
        c.Parameters.AddVarChar("@account",30,x.AccountNumber);c.Parameters.AddVarChar("@ref",100,x.BankReference);c.Parameters.AddMoney("@amount",x.Amount);
        c.Parameters.AddNVarChar("@content",500,x.TransferContent);c.Parameters.AddNVarChar("@name",100,x.SenderName);c.Parameters.AddVarChar("@sender",30,x.SenderAccount);
        c.Parameters.AddUtcDateTime("@time",x.TransactionTimeUtc);c.Parameters.AddInt32("@user",user);c.Parameters.AddUtcDateTime("@now",now);
        return (int)(await db.ScalarAsync(c,ct).ConfigureAwait(false))!;
    }

    public async Task<BankTransactionItem?> GetBankTransactionForUpdateAsync(UnitOfWork db,int id,byte[] version,CancellationToken ct)
    {
        using var c=db.CreateCommand("SELECT bt.Id,bt.AccountNumber,bt.BankReference,bt.Amount,bt.TransferContent,bt.SenderName,bt.SenderAccount,bt.TransactionTime,bt.MatchStatus,bt.MatchedBookingId,b.Code AS BookingCode,bt.Note,bt.RowVer FROM dbo.BankTransactions bt WITH (UPDLOCK) LEFT JOIN dbo.Bookings b ON b.Id=bt.MatchedBookingId WHERE bt.Id=@id AND bt.RowVer=@version;");
        c.Parameters.AddInt32("@id",id);c.Parameters.AddRowVersion("@version",version);
        return (await db.QueryAsync(c,r=>new BankTransactionItem(r.GetInt32("Id"),r.GetString("AccountNumber"),r.GetString("BankReference"),r.GetDecimal("Amount"),r.GetNullableString("TransferContent"),r.GetNullableString("SenderName"),r.GetNullableString("SenderAccount"),r.GetUtcDateTime("TransactionTime"),r.GetEnum<MatchStatus>("MatchStatus"),r.GetNullableInt32("MatchedBookingId"),r.GetNullableString("BookingCode"),r.GetNullableString("Note"),r.GetRowVersion("RowVer")),ct).ConfigureAwait(false)).SingleOrDefault();
    }
    public async Task<int> SetBankStatusAsync(UnitOfWork db,int id,byte[] version,MatchStatus status,int? bookingId,int? user,DateTime? at,string? note,string? refundRef,DateTime? refundedAt,CancellationToken ct)
    {
        using var c=db.CreateCommand("UPDATE dbo.BankTransactions SET MatchStatus=@status,MatchedBookingId=@booking,ProcessedByUserId=@user,ProcessedAt=@at,Note=@note,RefundBankRef=@refund,RefundedAt=@refunded,UpdatedAt=SYSUTCDATETIME() WHERE Id=@id AND RowVer=@version;");
        c.Parameters.AddInt32("@id",id);c.Parameters.AddRowVersion("@version",version);c.Parameters.AddEnum("@status",20,(MatchStatus?)status);c.Parameters.AddInt32("@booking",bookingId);c.Parameters.AddInt32("@user",user);c.Parameters.AddUtcDateTime("@at",at);c.Parameters.AddNVarChar("@note",500,note);c.Parameters.AddVarChar("@refund",100,refundRef);c.Parameters.AddUtcDateTime("@refunded",refundedAt);
        return await db.ExecuteAsync(c,ct).ConfigureAwait(false);
    }
    public async Task<(DateTime? RefundedAt,decimal? ExcessAmount)> GetBankRefundInfoAsync(DbSession db,int id,CancellationToken ct)
    { using var c=db.CreateCommand("SELECT bt.RefundedAt,p.Amount ExcessAmount FROM dbo.BankTransactions bt LEFT JOIN dbo.vw_Payables p ON p.SourceType='BankTransaction' AND p.SourceId=bt.Id AND p.PayableType='ExcessTransfer' WHERE bt.Id=@id;");c.Parameters.AddInt32("@id",id);return (await db.QueryAsync(c,r=>(r.GetNullableUtcDateTime("RefundedAt"),r.GetNullableDecimal("ExcessAmount")),ct).ConfigureAwait(false)).Single(); }
    public async Task<decimal> GetAmountDueAsync(DbSession db,int bookingId,CancellationToken ct)
    { using var c=db.CreateCommand("SELECT DepositAmount-DiscountAmount FROM dbo.Bookings WHERE Id=@id;");c.Parameters.AddInt32("@id",bookingId);return Convert.ToDecimal(await db.ScalarAsync(c,ct).ConfigureAwait(false),System.Globalization.CultureInfo.InvariantCulture); }
    public async Task<int?> GetExistingDepositAsync(DbSession db,int bookingId,CancellationToken ct)
    { using var c=db.CreateCommand("SELECT TOP(1) Id FROM dbo.Payments WHERE BookingId=@id AND Type='Deposit' AND Direction='In' AND Status='Pending' ORDER BY Id DESC;");c.Parameters.AddInt32("@id",bookingId);return await db.ScalarAsync(c,ct).ConfigureAwait(false) as int?; }
    public async Task<decimal?> GetPendingDepositAmountForUpdateAsync(UnitOfWork db,int paymentId,CancellationToken ct)
    { using var c=db.CreateCommand("SELECT Amount FROM dbo.Payments WITH (UPDLOCK) WHERE Id=@id AND Type='Deposit' AND Direction='In' AND Status='Pending' AND BankTransactionId IS NULL;");c.Parameters.AddInt32("@id",paymentId);return await db.ScalarAsync(c,ct).ConfigureAwait(false) as decimal?; }
    public async Task<int> AttachBankAndVerifyPaymentAsync(UnitOfWork db,int paymentId,BankTransactionItem tx,int user,DateTime now,CancellationToken ct)
    { using var c=db.CreateCommand("UPDATE dbo.Payments SET BankTransactionId=@bank,Amount=@amount,TransferredAt=@transferred,PayerOrPayeeName=@name,PayerOrPayeeAccount=@sender,TransferContent=@content,Status='Verified',BankRef=@ref,VerifiedByUserId=@user,VerifiedAt=@now,UpdatedAt=@now WHERE Id=@id AND Type='Deposit' AND Direction='In' AND Status='Pending' AND BankTransactionId IS NULL;");c.Parameters.AddInt32("@bank",tx.Id);c.Parameters.AddMoney("@amount",tx.Amount);c.Parameters.AddUtcDateTime("@transferred",tx.TransactionTimeUtc);c.Parameters.AddNVarChar("@name",100,tx.SenderName);c.Parameters.AddVarChar("@sender",30,tx.SenderAccount);c.Parameters.AddNVarChar("@content",255,tx.TransferContent is {Length:>255} text?text[..255]:tx.TransferContent);c.Parameters.AddVarChar("@ref",100,tx.BankReference);c.Parameters.AddInt32("@user",user);c.Parameters.AddUtcDateTime("@now",now);c.Parameters.AddInt32("@id",paymentId);return await db.ExecuteAsync(c,ct).ConfigureAwait(false); }
    public async Task<int> InsertVerifiedDepositAsync(UnitOfWork db,BankTransactionItem tx,int bookingId,string voucher,int user,DateTime now,CancellationToken ct)
    { using var c=db.CreateCommand("INSERT dbo.Payments(VoucherNo,BookingId,BankTransactionId,Direction,Type,Method,Amount,TransferredAt, PayerOrPayeeName,PayerOrPayeeAccount,TransferContent,Status,RecordedByUserId,RecordedAt,VerifiedByUserId,VerifiedAt,BankRef) VALUES(@voucher,@booking,@bank,'In','Deposit','BankTransfer',@amount,@transferred,@name,@sender,@content,'Verified',@user,@now,@user,@now,@ref);");c.Parameters.AddVarChar("@voucher",20,voucher);c.Parameters.AddInt32("@booking",bookingId);c.Parameters.AddInt32("@bank",tx.Id);c.Parameters.AddMoney("@amount",tx.Amount);c.Parameters.AddUtcDateTime("@transferred",tx.TransactionTimeUtc);c.Parameters.AddNVarChar("@name",100,tx.SenderName);c.Parameters.AddVarChar("@sender",30,tx.SenderAccount);c.Parameters.AddNVarChar("@content",255,tx.TransferContent);c.Parameters.AddInt32("@user",user);c.Parameters.AddUtcDateTime("@now",now);c.Parameters.AddVarChar("@ref",100,tx.BankReference);return await db.ExecuteAsync(c,ct).ConfigureAwait(false); }
    public async Task<IReadOnlyList<CashBookItem>> ListCashBookAsync(DbSession db,DateTime from,DateTime to,CancellationToken ct)
    { using var c=db.CreateCommand("SELECT TOP(2000) SourceType,SourceId,EntryAt,ReferenceNo,EntryType,IncomeAmount,ExpenseAmount,PartyName,RecordedByUsername,BookingId,HomestayId,Note FROM dbo.vw_CashBook WHERE EntryAt>=@from AND EntryAt<@to ORDER BY EntryAt DESC,SourceType,SourceId DESC;");c.Parameters.AddUtcDateTime("@from",from);c.Parameters.AddUtcDateTime("@to",to);return await db.QueryAsync(c,r=>new CashBookItem(r.GetString("SourceType"),r.GetInt32("SourceId"),r.GetUtcDateTime("EntryAt"),r.GetString("ReferenceNo"),r.GetString("EntryType"),r.GetDecimal("IncomeAmount"),r.GetDecimal("ExpenseAmount"),r.GetNullableString("PartyName"),r.GetNullableString("RecordedByUsername"),r.GetNullableInt32("BookingId"),r.GetNullableInt32("HomestayId"),r.GetNullableString("Note")),ct).ConfigureAwait(false); }
    public async Task<IReadOnlyList<PayableItem>> ListPayablesAsync(DbSession db,CancellationToken ct)
    { using var c=db.CreateCommand("SELECT TOP(2000) SourceType,SourceId,PayableAt,ReferenceNo,PayableType,Amount,PartyName,BookingId,HomestayId,Note FROM dbo.vw_Payables ORDER BY PayableAt,SourceType,SourceId;");return await db.QueryAsync(c,r=>new PayableItem(r.GetString("SourceType"),r.GetInt32("SourceId"),r.GetUtcDateTime("PayableAt"),r.GetString("ReferenceNo"),r.GetString("PayableType"),r.GetDecimal("Amount"),r.GetNullableString("PartyName"),r.GetNullableInt32("BookingId"),r.GetNullableInt32("HomestayId"),r.GetNullableString("Note")),ct).ConfigureAwait(false); }
    public async Task<AccountantDashboard> DashboardAsync(DbSession db,CancellationToken ct)
    { using var c=db.CreateCommand("SELECT (SELECT COUNT(*) FROM dbo.BankTransactions WHERE MatchStatus='Unmatched') Unmatched,(SELECT COUNT(*) FROM dbo.BankTransactions WHERE MatchStatus='Underpaid') Underpaid,(SELECT COUNT(*) FROM dbo.Payments WHERE Type='Deposit' AND Direction='In' AND Status='Pending') Deposits,(SELECT COUNT(*) FROM dbo.Payments WHERE Type='Refund' AND Direction='Out' AND Status='Pending') Refunds,(SELECT COUNT(*) FROM dbo.Settlements WHERE Status='Sent') Settlements,COALESCE((SELECT SUM(Amount) FROM dbo.vw_Payables),0) Payables;");return (await db.QueryAsync(c,r=>new AccountantDashboard(r.GetInt32("Unmatched"),r.GetInt32("Underpaid"),r.GetInt32("Deposits"),r.GetInt32("Refunds"),r.GetInt32("Settlements"),r.GetDecimal("Payables")),ct).ConfigureAwait(false)).Single(); }
}
