using Homestay.DAL.Infrastructure;
using Homestay.DTO.Common;
using Microsoft.Data.SqlClient;

namespace Homestay.DAL.Repositories;

/// <summary>Người đang phụ trách đơn, dùng khi kiểm tra quyền thao tác vòng đời.</summary>
public sealed record BookingAssignmentContext(int? AssignedEmployeeId);

/// <summary>Truy vấn vòng đời đơn: người phụ trách, danh sách người có thể giao, đổi phòng cho các đêm, phòng dùng khi xác nhận.</summary>
public sealed partial class BookingRepository
{
    private const string AssignableEmployee = "e.EmploymentStatus='Active' AND u.Status='Active' AND e.Position IN ('Staff','Manager')";
    private const string AssignableArea = """
        (e.HasAllAreas=1 OR EXISTS(SELECT 1 FROM dbo.EmployeeAreas ea
            JOIN dbo.Bookings ab ON ab.Id=@booking JOIN dbo.Homestays ah ON ah.Id=ab.HomestayId
            WHERE ea.EmployeeId=e.Id AND ea.AreaId=ah.AreaId))
        """;
    public async Task<BookingAssignmentContext?> GetAssignmentContextAsync(DbSession db, int bookingId, string condition,
        Action<SqlCommand> addScope, CancellationToken ct)
    {
        using var command = db.CreateCommand($"SELECT b.AssignedEmployeeId FROM dbo.Bookings b JOIN dbo.Homestays h ON h.Id=b.HomestayId WHERE b.Id=@booking AND ({condition});");
        command.Parameters.AddInt32("@booking", bookingId); addScope(command);
        return (await db.QueryAsync(command, r => new BookingAssignmentContext(r.GetNullableInt32("AssignedEmployeeId")), ct).ConfigureAwait(false)).SingleOrDefault();
    }
    public async Task<IReadOnlyList<Lookup>> ListAssigneesAsync(DbSession db, int bookingId, CancellationToken ct)
    {
        using var command = db.CreateCommand($"""
            SELECT e.Id,e.EmployeeCode,e.FullName FROM dbo.Employees e JOIN dbo.Users u ON u.Id=e.UserId
            WHERE {AssignableEmployee} AND {AssignableArea}
            AND NOT EXISTS(SELECT 1 FROM dbo.Bookings currentBooking WHERE currentBooking.Id=@booking AND currentBooking.AssignedEmployeeId=e.Id)
            ORDER BY e.EmployeeCode,e.Id;
            """);
        command.Parameters.AddInt32("@booking", bookingId);
        return await db.QueryAsync(command, r => new Lookup { Id = r.GetInt32("Id"), Code = r.GetString("EmployeeCode"),
            Name = r.GetString("EmployeeCode") + " · " + r.GetString("FullName") }, ct).ConfigureAwait(false);
    }
    public async Task MoveRoomNightsAsync(UnitOfWork db, int bookingId, int roomId, CancellationToken ct)
    {
        using var command = db.CreateCommand("UPDATE dbo.RoomNights SET RoomId=@room WHERE BookingId=@booking;");
        command.Parameters.AddInt32("@booking", bookingId); command.Parameters.AddInt32("@room", roomId);
        await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Lookup>> ListConfirmationRoomsAsync(DbSession db, int bookingId, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand($"""
            SELECT r.Id,r.RoomCode FROM dbo.Rooms r JOIN dbo.Bookings original ON original.RoomTypeId=r.RoomTypeId
            WHERE original.Id=@booking AND r.IsActive=1 AND r.Id<>original.RoomId AND NOT EXISTS(
                SELECT 1 FROM dbo.RoomNights rn LEFT JOIN dbo.Bookings b ON b.Id=rn.BookingId
                LEFT JOIN dbo.BookingChangeRequests cr ON cr.Id=rn.ChangeRequestId
                WHERE rn.RoomId=r.Id AND rn.[Date]>=original.CheckIn AND rn.[Date]<original.CheckOut
                    AND {StaffReadSql.Occupies}) ORDER BY r.RoomCode,r.Id;
            """);
        command.Parameters.AddInt32("@booking", bookingId); command.Parameters.AddUtcDateTime("@now", now);
        return await db.QueryAsync(command, r => new Lookup { Id = r.GetInt32("Id"), Name = r.GetString("RoomCode"), Code = r.GetString("RoomCode") }, ct).ConfigureAwait(false);
    }
}
