using Homestay.DAL.Infrastructure;

namespace Homestay.DAL.Repositories;

public sealed class HoldExpiryRepository
{
    public async Task<IReadOnlyList<int>> FindRoomTypesAsync(UnitOfWork db, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            SELECT DISTINCT b.RoomTypeId FROM dbo.Bookings b
            WHERE b.Status='AwaitingPayment' AND b.HoldExpiresAt<@now
                AND NOT EXISTS(SELECT 1 FROM dbo.Payments p WHERE p.BookingId=b.Id AND p.Type='Deposit' AND p.Status='Pending')
            ORDER BY b.RoomTypeId;
            """);
        command.Parameters.AddUtcDateTime("@now", now);
        return await db.QueryAsync(command, r => r.GetInt32("RoomTypeId"), ct).ConfigureAwait(false);
    }

    // Caller holds RoomType:{id}; lock eligible rows, then use the same conditional status writer as user actions.
    public async Task<IReadOnlyList<(int Id, byte[] RowVer)>> FindExpiredAsync(UnitOfWork db, int roomTypeId, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            SELECT b.Id,b.RowVer FROM dbo.Bookings b WITH (UPDLOCK)
            WHERE b.RoomTypeId=@type AND b.Status='AwaitingPayment' AND b.HoldExpiresAt<@now
                AND NOT EXISTS(SELECT 1 FROM dbo.Payments p WHERE p.BookingId=b.Id AND p.Type='Deposit' AND p.Status='Pending')
            ORDER BY b.Id;
            """);
        command.Parameters.AddInt32("@type", roomTypeId);
        command.Parameters.AddUtcDateTime("@now", now);
        return await db.QueryAsync(command, r => (r.GetInt32("Id"), r.GetFieldValue<byte[]>(r.GetOrdinal("RowVer"))), ct).ConfigureAwait(false);
    }
}
