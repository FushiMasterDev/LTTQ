using Homestay.DAL.Infrastructure;
using Homestay.DTO.Contracts;

namespace Homestay.DAL.Repositories;

/// <summary>Truy vấn ghi hợp đồng: xác định homestay, thêm, đổi trạng thái và kiểm tra còn đơn đang ở không.</summary>
public sealed partial class ContractRepository
{
    public async Task<int?> LocateHomeAsync(DbSession db, int id, CancellationToken ct)
    {
        using var c = db.CreateCommand("SELECT HomestayId FROM dbo.Contracts WHERE Id=@id;"); c.Parameters.AddInt32("@id", id);
        return (int?)await db.ScalarAsync(c, ct).ConfigureAwait(false);
    }
    public async Task<int> InsertAsync(UnitOfWork db, CreateContractRequest v, int employee, DateTime now, CancellationToken ct)
    {
        using var c = db.CreateCommand("INSERT dbo.Contracts(HomestayId,ContractNo,StartDate,EndDate,CommissionRate,Status,SignedByEmployeeId,CreatedAt) OUTPUT inserted.Id VALUES(@home,@no,@start,@end,@rate,'Active',@employee,@now);");
        c.Parameters.AddInt32("@home", v.HomestayId); c.Parameters.AddVarChar("@no", 30, v.ContractNo); c.Parameters.AddDate("@start", v.StartDate); c.Parameters.AddDate("@end", v.EndDate);
        c.Parameters.AddRate("@rate", v.CommissionRate); c.Parameters.AddInt32("@employee", employee); c.Parameters.AddUtcDateTime("@now", now);
        return (int)(await db.ScalarAsync(c, ct).ConfigureAwait(false))!;
    }
    public async Task<int> ChangeAsync(UnitOfWork db, int id, DateOnly end, bool terminate, string? reason, DateTime now, CancellationToken ct)
    {
        using var c = db.CreateCommand("""
            UPDATE dbo.Contracts SET EndDate=@end,Status=CASE WHEN @terminate=1 THEN 'Terminated' ELSE Status END,
                TerminatedAt=CASE WHEN @terminate=1 THEN @now ELSE TerminatedAt END,
                TerminateReason=CASE WHEN @terminate=1 THEN @reason ELSE TerminateReason END,UpdatedAt=@now
            WHERE Id=@id AND Status='Active';
            """);
        c.Parameters.AddInt32("@id", id); c.Parameters.AddDate("@end", end); c.Parameters.AddBoolean("@terminate", terminate);
        c.Parameters.AddNVarChar("@reason", 500, reason); c.Parameters.AddUtcDateTime("@now", now);
        return await db.ExecuteAsync(c, ct).ConfigureAwait(false);
    }
    public async Task<bool> HasOccupyingBookingsAsync(UnitOfWork db, int home, DateTime now, CancellationToken ct)
    {
        // Same booking half of HomestayRepository.CanEndAsync, including change holds and excluding Completed.
        using var c = db.CreateCommand($"""
            SELECT TOP(1) rn.RoomId FROM dbo.RoomNights rn JOIN dbo.Rooms r ON r.Id=rn.RoomId JOIN dbo.RoomTypes t ON t.Id=r.RoomTypeId
            LEFT JOIN dbo.Bookings b ON b.Id=rn.BookingId LEFT JOIN dbo.BookingChangeRequests cr ON cr.Id=rn.ChangeRequestId
            WHERE t.HomestayId=@home AND rn.RoomBlockId IS NULL AND (b.Id IS NULL OR b.Status<>'Completed') AND {StaffReadSql.Occupies};
            """);
        c.Parameters.AddInt32("@home", home); c.Parameters.AddUtcDateTime("@now", now);
        return await db.ScalarAsync(c, ct).ConfigureAwait(false) is not null;
    }
}
