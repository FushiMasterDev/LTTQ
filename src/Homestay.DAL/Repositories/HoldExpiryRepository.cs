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

    // Caller holds RoomType:{id}. OUTPUT is the only source for ledger/history/audit writes.
    public async Task<IReadOnlyList<int>> ExpireAsync(UnitOfWork db, int roomTypeId, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            DECLARE @expired TABLE(Id int PRIMARY KEY);
            UPDATE b SET Status='Expired',UpdatedAt=@now OUTPUT inserted.Id INTO @expired
            FROM dbo.Bookings b
            WHERE b.RoomTypeId=@type AND b.Status='AwaitingPayment' AND b.HoldExpiresAt<@now
                AND NOT EXISTS(SELECT 1 FROM dbo.Payments p WHERE p.BookingId=b.Id AND p.Type='Deposit' AND p.Status='Pending');
            DELETE rn FROM dbo.RoomNights rn JOIN @expired e ON e.Id=rn.BookingId;
            INSERT dbo.BookingStatusHistories(BookingId,FromStatus,ToStatus,ActorType,ActorUserId,Note,ChangedAt)
            SELECT Id,'AwaitingPayment','Expired','System',NULL,N'Hết thời gian giữ phòng',@now FROM @expired;
            SELECT Id FROM @expired ORDER BY Id;
            """);
        command.Parameters.AddInt32("@type", roomTypeId);
        command.Parameters.AddUtcDateTime("@now", now);
        return await db.QueryAsync(command, r => r.GetInt32("Id"), ct).ConfigureAwait(false);
    }
}
