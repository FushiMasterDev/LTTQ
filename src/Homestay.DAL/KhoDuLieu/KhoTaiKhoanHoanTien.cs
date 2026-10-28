using Homestay.DAL.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Homestay.DAL.Repositories;

public sealed record RefundAccountSnapshot(int Id, int BookingId, string Direction, string Type, string Status, string? Account, string? Name, byte[] RowVer);

public sealed partial class PaymentRepository
{
    public async Task<RefundAccountSnapshot?> GetAccountAsync(DbSession db, int id, string scope, Action<SqlCommand> addScope, CancellationToken ct)
    {
        using var c = db.CreateCommand($"""
            SELECT p.Id,p.BookingId,p.Direction,p.Type,p.Status,p.PayerOrPayeeAccount,p.PayerOrPayeeName,p.RowVer
            FROM dbo.Payments p JOIN dbo.Bookings b ON b.Id=p.BookingId JOIN dbo.Homestays h ON h.Id=b.HomestayId
            WHERE p.Id=@id AND ({scope});
            """);
        c.Parameters.AddInt32("@id", id); addScope(c);
        return (await db.QueryAsync(c, r => new RefundAccountSnapshot(r.GetInt32("Id"), r.GetInt32("BookingId"), r.GetString("Direction"), r.GetString("Type"),
            r.GetString("Status"), r.GetNullableString("PayerOrPayeeAccount"), r.GetNullableString("PayerOrPayeeName"), r.GetRowVersion("RowVer")), ct).ConfigureAwait(false)).SingleOrDefault();
    }
    public async Task<int> UpdateRefundAccountAsync(UnitOfWork db, int id, byte[] version, string account, string? name, DateTime now, CancellationToken ct)
    {
        using var c = db.CreateCommand("""
            UPDATE dbo.Payments SET PayerOrPayeeAccount=@account,PayerOrPayeeName=@name,UpdatedAt=@now
            WHERE Id=@id AND RowVer=@version AND Direction='Out' AND Type='Refund' AND Status='Pending';
            """);
        c.Parameters.AddInt32("@id", id); c.Parameters.AddRowVersion("@version", version); c.Parameters.AddVarChar("@account", 30, account);
        c.Parameters.AddNVarChar("@name", 100, name); c.Parameters.AddUtcDateTime("@now", now);
        return await db.ExecuteAsync(c, ct).ConfigureAwait(false);
    }
}
