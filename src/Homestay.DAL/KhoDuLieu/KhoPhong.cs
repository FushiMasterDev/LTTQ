using Homestay.DAL.Infrastructure;
using Homestay.DTO.Enums;
using Homestay.DTO.Rooms;
namespace Homestay.DAL.Repositories;

/// <summary>Truy vấn đọc phòng: loại phòng, phòng, bảng giá và lịch khóa phòng.</summary>
public sealed partial class RoomRepository
{
    public async Task<IReadOnlyList<RoomTypeItem>> ListTypesAsync(DbSession db, int home, CancellationToken ct)
    {
        using var c = db.CreateCommand("SELECT t.*,rc.Name AS CategoryName,h.RowVer FROM dbo.RoomTypes t JOIN dbo.Homestays h ON h.Id=t.HomestayId JOIN dbo.RoomCategories rc ON rc.Id=t.CategoryId WHERE h.Id=@id ORDER BY t.Name,t.Id;"); c.Parameters.AddInt32("@id", home);
        return await db.QueryAsync(c, r => new RoomTypeItem(r.GetInt32("Id"), r.GetInt32("HomestayId"), r.GetInt32("CategoryId"), r.GetString("CategoryName"), r.GetString("Name"), r.GetInt32("MaxGuests"), r.GetNullableInt32("AreaM2"), r.GetNullableString("Description"), r.GetBoolean("IsActive"), r.GetRowVersion("RowVer")), ct).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<RoomItem>> ListRoomsAsync(DbSession db, int home, CancellationToken ct)
    {
        using var c = db.CreateCommand("SELECT r.*,h.RowVer FROM dbo.Rooms r JOIN dbo.RoomTypes t ON t.Id=r.RoomTypeId JOIN dbo.Homestays h ON h.Id=t.HomestayId WHERE h.Id=@id ORDER BY t.Name,r.RoomCode,r.Id;"); c.Parameters.AddInt32("@id", home);
        return await db.QueryAsync(c, r => new RoomItem(r.GetInt32("Id"), r.GetInt32("RoomTypeId"), r.GetString("RoomCode"), r.GetBoolean("IsActive"), r.GetNullableString("Note"), r.GetRowVersion("RowVer")), ct).ConfigureAwait(false);
    }
    public async Task<RoomPriceList> ListPricesAsync(DbSession db, int home, DateTime now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now.AddHours(7));
        using var c = db.CreateCommand("""
            SELECT p.RoomTypeId,p.EffectiveFrom,MAX(CASE WHEN p.DayType='Weekday' THEN p.Price END) AS WeekdayPrice,
                MAX(CASE WHEN p.DayType='Weekend' THEN p.Price END) AS WeekendPrice,MAX(CASE WHEN p.DayType='Holiday' THEN p.Price END) AS HolidayPrice,h.RowVer
            FROM dbo.RoomTypePrices p JOIN dbo.RoomTypes t ON t.Id=p.RoomTypeId JOIN dbo.Homestays h ON h.Id=t.HomestayId
            WHERE h.Id=@id GROUP BY p.RoomTypeId,p.EffectiveFrom,h.RowVer ORDER BY p.RoomTypeId,p.EffectiveFrom DESC;
            """); c.Parameters.AddInt32("@id", home);
        var rows = await db.QueryAsync(c, r => new RoomPriceVersion(r.GetInt32("RoomTypeId"), r.GetDateOnly("EffectiveFrom"), r.GetNullableDecimal("WeekdayPrice"), r.GetNullableDecimal("WeekendPrice"), r.GetNullableDecimal("HolidayPrice"), r.GetDateOnly("EffectiveFrom") <= today, r.GetRowVersion("RowVer")), ct).ConfigureAwait(false);
        return new(now, today, rows);
    }
    public async Task<IReadOnlyList<RoomBlockItem>> ListBlocksAsync(DbSession db, RoomBlockSearchRequest request, CancellationToken ct)
    {
        // Blocks alone have simple half-open date overlap. Occupied booking/calendar logic stays in AvailabilityRepository.
        using var c = db.CreateCommand("""
            SELECT b.*,h.RowVer FROM dbo.RoomBlocks b JOIN dbo.Rooms r ON r.Id=b.RoomId JOIN dbo.RoomTypes t ON t.Id=r.RoomTypeId
            JOIN dbo.Homestays h ON h.Id=t.HomestayId WHERE h.Id=@id AND b.StartDate<@to AND b.EndDate>@from ORDER BY b.StartDate,b.Id;
            """); c.Parameters.AddInt32("@id", request.HomestayId); c.Parameters.AddDate("@from", request.FromDate); c.Parameters.AddDate("@to", request.ToDate);
        return await db.QueryAsync(c, r => new RoomBlockItem(r.GetInt32("Id"), r.GetInt32("RoomId"), r.GetDateOnly("StartDate"), r.GetDateOnly("EndDate"), r.GetEnum<RoomBlockReason>("Reason"), r.GetNullableString("Note"), r.GetInt32("CreatedByUserId"), r.GetRowVersion("RowVer")), ct).ConfigureAwait(false);
    }
}
