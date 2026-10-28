using Homestay.DAL.Infrastructure;
using Homestay.DTO.Enums;
using Homestay.DTO.Rooms;
using Microsoft.Data.SqlClient;

namespace Homestay.DAL.Repositories;

/// <summary>Vị trí của loại phòng/phòng cần ghi (thuộc homestay nào) để kiểm tra phạm vi.</summary>
public sealed record RoomWriteLocation(int HomestayId, int RoomTypeId, int? RoomId);
/// <summary>Thông tin xung đột khi khóa phòng: trùng với đơn nào hoặc lịch khóa nào.</summary>
public sealed record RoomBlockConflict(string? BookingCode, int? RoomBlockId);

/// <summary>Truy vấn ghi phòng: loại phòng, phòng, bật/tắt, bảng giá và lịch khóa phòng.</summary>
public sealed partial class RoomRepository
{
    // Resolve IDs without taking child locks before the parent. Relations cannot be moved by a writer.
    public async Task<RoomWriteLocation?> LocateAsync(DbSession db, int id, string kind, CancellationToken ct)
    {
        var sql = kind switch
        {
            "Type" => "SELECT HomestayId,Id AS RoomTypeId,CAST(NULL AS int) AS RoomId FROM dbo.RoomTypes WHERE Id=@id;",
            "Room" => "SELECT t.HomestayId,t.Id AS RoomTypeId,r.Id AS RoomId FROM dbo.Rooms r JOIN dbo.RoomTypes t ON t.Id=r.RoomTypeId WHERE r.Id=@id;",
            "Block" => "SELECT t.HomestayId,t.Id AS RoomTypeId,r.Id AS RoomId FROM dbo.RoomBlocks b JOIN dbo.Rooms r ON r.Id=b.RoomId JOIN dbo.RoomTypes t ON t.Id=r.RoomTypeId WHERE b.Id=@id;",
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };
        using var c = db.CreateCommand(sql); c.Parameters.AddInt32("@id", id);
        return (await db.QueryAsync(c, r => new RoomWriteLocation(r.GetInt32("HomestayId"), r.GetInt32("RoomTypeId"), r.GetNullableInt32("RoomId")), ct).ConfigureAwait(false)).SingleOrDefault();
    }
    public async Task<bool> CategoryActiveAsync(UnitOfWork db, int id, CancellationToken ct)
    {
        using var c = db.CreateCommand("SELECT Id FROM dbo.RoomCategories WITH (HOLDLOCK) WHERE Id=@id AND IsActive=1;"); c.Parameters.AddInt32("@id", id);
        return await db.ScalarAsync(c, ct).ConfigureAwait(false) is not null;
    }
    private static void TypeValues(SqlCommand c, int category, string name, int guests, int? area, string? description, DateTime now)
    {
        c.Parameters.AddInt32("@category", category); c.Parameters.AddNVarChar("@name", 100, name); c.Parameters.AddInt32("@guests", guests);
        c.Parameters.AddInt32("@area", area); c.Parameters.AddNVarChar("@description", 1000, description); c.Parameters.AddUtcDateTime("@now", now);
    }
    public async Task<int> InsertTypeAsync(UnitOfWork db, CreateRoomTypeRequest v, DateTime now, CancellationToken ct)
    {
        using var c = db.CreateCommand("INSERT dbo.RoomTypes(HomestayId,CategoryId,Name,MaxGuests,AreaM2,Description,IsActive,CreatedAt) OUTPUT inserted.Id VALUES(@home,@category,@name,@guests,@area,@description,1,@now);");
        c.Parameters.AddInt32("@home", v.HomestayId); TypeValues(c, v.CategoryId, v.Name, v.MaxGuests, v.AreaM2, v.Description, now);
        return (int)(await db.ScalarAsync(c, ct).ConfigureAwait(false))!;
    }
    public async Task UpdateTypeAsync(UnitOfWork db, UpdateRoomTypeRequest v, DateTime now, CancellationToken ct)
    {
        using var c = db.CreateCommand("UPDATE dbo.RoomTypes SET CategoryId=@category,Name=@name,MaxGuests=@guests,AreaM2=@area,Description=@description,UpdatedAt=@now WHERE Id=@id;");
        c.Parameters.AddInt32("@id", v.RoomTypeId); TypeValues(c, v.CategoryId, v.Name, v.MaxGuests, v.AreaM2, v.Description, now);
        await db.ExecuteAsync(c, ct).ConfigureAwait(false);
    }
    public async Task<int> InsertRoomAsync(UnitOfWork db, CreateRoomRequest v, DateTime now, CancellationToken ct)
    {
        using var c = db.CreateCommand("INSERT dbo.Rooms(RoomTypeId,RoomCode,Note,IsActive,CreatedAt) OUTPUT inserted.Id VALUES(@type,@code,@note,1,@now);");
        c.Parameters.AddInt32("@type", v.RoomTypeId); c.Parameters.AddNVarChar("@code", 20, v.RoomCode); c.Parameters.AddNVarChar("@note", 255, v.Note); c.Parameters.AddUtcDateTime("@now", now);
        return (int)(await db.ScalarAsync(c, ct).ConfigureAwait(false))!;
    }
    public async Task UpdateRoomAsync(UnitOfWork db, UpdateRoomRequest v, DateTime now, CancellationToken ct)
    {
        using var c = db.CreateCommand("UPDATE dbo.Rooms SET RoomCode=@code,Note=@note,UpdatedAt=@now WHERE Id=@id;");
        c.Parameters.AddInt32("@id", v.RoomId); c.Parameters.AddNVarChar("@code", 20, v.RoomCode); c.Parameters.AddNVarChar("@note", 255, v.Note); c.Parameters.AddUtcDateTime("@now", now);
        await db.ExecuteAsync(c, ct).ConfigureAwait(false);
    }
    public async Task SetActiveAsync(UnitOfWork db, int id, bool type, bool active, DateTime now, CancellationToken ct)
    {
        using var c = db.CreateCommand(type ? "UPDATE dbo.RoomTypes SET IsActive=@active,UpdatedAt=@now WHERE Id=@id;" : "UPDATE dbo.Rooms SET IsActive=@active,UpdatedAt=@now WHERE Id=@id;");
        c.Parameters.AddInt32("@id", id); c.Parameters.AddBoolean("@active", active); c.Parameters.AddUtcDateTime("@now", now); await db.ExecuteAsync(c, ct).ConfigureAwait(false);
    }
    public async Task<bool> HasSellableRoomAsync(UnitOfWork db, int home, CancellationToken ct)
    {
        using var c = db.CreateCommand("SELECT TOP(1) r.Id FROM dbo.Rooms r JOIN dbo.RoomTypes t ON t.Id=r.RoomTypeId WHERE t.HomestayId=@home AND t.IsActive=1 AND r.IsActive=1;");
        c.Parameters.AddInt32("@home", home); return await db.ScalarAsync(c, ct).ConfigureAwait(false) is not null;
    }
    public async Task<int> WritePricesAsync(UnitOfWork db, int type, DateOnly from, decimal weekday, decimal weekend, decimal holiday, bool update, int user, DateTime now, CancellationToken ct)
    {
        var count = 0;
        foreach (var (day, price) in new[] { (DayType.Weekday, weekday), (DayType.Weekend, weekend), (DayType.Holiday, holiday) })
        {
            using var c = db.CreateCommand(update
                ? "UPDATE dbo.RoomTypePrices SET Price=@price,UpdatedAt=@now WHERE RoomTypeId=@type AND EffectiveFrom=@from AND DayType=@day;"
                : "INSERT dbo.RoomTypePrices(RoomTypeId,DayType,Price,EffectiveFrom,CreatedByUserId,CreatedAt) VALUES(@type,@day,@price,@from,@user,@now);");
            c.Parameters.AddInt32("@type", type); c.Parameters.AddDate("@from", from); c.Parameters.AddEnum("@day", 10, (DayType?)day);
            c.Parameters.AddMoney("@price", price); c.Parameters.AddInt32("@user", user); c.Parameters.AddUtcDateTime("@now", now);
            count += await db.ExecuteAsync(c, ct).ConfigureAwait(false);
        }
        return count;
    }
    public async Task<RoomBlockConflict?> FindBlockConflictAsync(UnitOfWork db, int room, DateOnly start, DateOnly end, DateTime now, CancellationToken ct)
    {
        using var c = db.CreateCommand($"""
            SELECT TOP(1) COALESCE(b.Code,cb.Code) AS BookingCode,rn.RoomBlockId
            FROM dbo.RoomNights rn LEFT JOIN dbo.Bookings b ON b.Id=rn.BookingId
            LEFT JOIN dbo.BookingChangeRequests cr ON cr.Id=rn.ChangeRequestId LEFT JOIN dbo.Bookings cb ON cb.Id=cr.BookingId
            WHERE rn.RoomId=@room AND rn.[Date]>=@start AND rn.[Date]<@end AND {StaffReadSql.Occupies}
            ORDER BY rn.[Date];
            """);
        c.Parameters.AddInt32("@room", room); c.Parameters.AddDate("@start", start); c.Parameters.AddDate("@end", end); c.Parameters.AddUtcDateTime("@now", now);
        return (await db.QueryAsync(c, r => new RoomBlockConflict(r.GetNullableString("BookingCode"), r.GetNullableInt32("RoomBlockId")), ct).ConfigureAwait(false)).SingleOrDefault();
    }
    public async Task<int> InsertBlockAsync(UnitOfWork db, BlockRoomRequest v, int user, DateTime now, CancellationToken ct)
    {
        using var c = db.CreateCommand("INSERT dbo.RoomBlocks(RoomId,StartDate,EndDate,Reason,Note,CreatedByUserId,CreatedAt) OUTPUT inserted.Id VALUES(@room,@start,@end,@reason,@note,@user,@now);");
        c.Parameters.AddInt32("@room", v.RoomId); c.Parameters.AddDate("@start", v.StartDate); c.Parameters.AddDate("@end", v.EndDate);
        c.Parameters.AddEnum("@reason", 20, (RoomBlockReason?)v.Reason); c.Parameters.AddNVarChar("@note", 255, v.Note); c.Parameters.AddInt32("@user", user); c.Parameters.AddUtcDateTime("@now", now);
        var id = (int)(await db.ScalarAsync(c, ct).ConfigureAwait(false))!;
        for (var date = v.StartDate; date < v.EndDate; date = date.AddDays(1))
        {
            using var n = db.CreateCommand("INSERT dbo.RoomNights(RoomId,[Date],RoomBlockId,CreatedAt) VALUES(@room,@date,@id,@now);");
            n.Parameters.AddInt32("@room", v.RoomId); n.Parameters.AddDate("@date", date); n.Parameters.AddInt32("@id", id); n.Parameters.AddUtcDateTime("@now", now);
            await db.ExecuteAsync(n, ct).ConfigureAwait(false);
        }
        return id;
    }
    public async Task<RoomBlockItem?> GetBlockAsync(DbSession db, int home, int id, CancellationToken ct)
    {
        using var c = db.CreateCommand("SELECT b.*,h.RowVer FROM dbo.RoomBlocks b JOIN dbo.Rooms r ON r.Id=b.RoomId JOIN dbo.RoomTypes t ON t.Id=r.RoomTypeId JOIN dbo.Homestays h ON h.Id=t.HomestayId WHERE h.Id=@home AND b.Id=@id;");
        c.Parameters.AddInt32("@home", home); c.Parameters.AddInt32("@id", id);
        return (await db.QueryAsync(c, r => new RoomBlockItem(r.GetInt32("Id"), r.GetInt32("RoomId"), r.GetDateOnly("StartDate"), r.GetDateOnly("EndDate"), r.GetEnum<RoomBlockReason>("Reason"), r.GetNullableString("Note"), r.GetInt32("CreatedByUserId"), r.GetRowVersion("RowVer")), ct).ConfigureAwait(false)).SingleOrDefault();
    }
    public async Task<int> DeleteBlockAsync(UnitOfWork db, int id, CancellationToken ct)
    {
        // FK_RoomNights_RoomBlockId ON DELETE CASCADE. Both tables allow DELETE for app_homestay.
        using var c = db.CreateCommand("DELETE dbo.RoomBlocks WHERE Id=@id;"); c.Parameters.AddInt32("@id", id);
        return await db.ExecuteAsync(c, ct).ConfigureAwait(false);
    }
}
