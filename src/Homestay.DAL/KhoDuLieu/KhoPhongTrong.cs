using Homestay.DAL.Infrastructure;
using Homestay.DTO.Availability;
using Homestay.DTO.Catalog;
using Homestay.DTO.Enums;

namespace Homestay.DAL.Repositories;

/// <summary>Một homestay/loại phòng ứng viên khi tìm phòng trống.</summary>
public sealed record AvailabilityCandidate(int HomestayId, string HomestayName, int AreaId, string AreaName,
    int RoomTypeId, string RoomTypeName, int CategoryId, string CategoryName, int MaxGuests, decimal CommissionRate);
/// <summary>Một phòng cụ thể của loại phòng ứng viên và còn trống hay không.</summary>
public sealed record CandidateRoom(int RoomTypeId, int RoomId, string RoomCode, bool IsAvailable);
/// <summary>Bảng giá áp dụng cho một loại phòng ứng viên.</summary>
public sealed record CandidatePrice(int RoomTypeId, RoomPriceVersion Version);
/// <summary>Toàn bộ dữ liệu thô cho việc tìm phòng trống: ứng viên, phòng, giá, ngày lễ.</summary>
public sealed record AvailabilityData(IReadOnlyList<AvailabilityCandidate> Candidates, IReadOnlyList<CandidateRoom> Rooms,
    IReadOnlyList<CandidatePrice> Prices, IReadOnlySet<DateOnly> Holidays, IReadOnlyList<ApplicableCancellationPolicy> Policies);

/// <summary>Truy vấn tìm phòng trống và lịch phòng theo khoảng ngày.</summary>
public sealed class AvailabilityRepository
{
    public async Task<AvailabilityData> LoadAsync(DbSession db, AvailabilitySearchRequest request, int? roomTypeId, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand($"""
            SELECT h.Id AS HomestayId, h.Name AS HomestayName, h.AreaId, a.Name AS AreaName,
                rt.Id AS RoomTypeId, rt.Name AS RoomTypeName, rt.CategoryId, rc.Name AS CategoryName, rt.MaxGuests, c.CommissionRate
            INTO #types
            FROM dbo.RoomTypes rt JOIN dbo.Homestays h ON h.Id=rt.HomestayId
            JOIN dbo.Areas a ON a.Id=h.AreaId JOIN dbo.RoomCategories rc ON rc.Id=rt.CategoryId
            JOIN dbo.Contracts c ON c.HomestayId=h.Id AND c.Status='Active' AND c.EndDate>=@out
            WHERE h.Status='Active' AND rt.IsActive=1 AND rt.MaxGuests>=@guests
                AND (@area IS NULL OR h.AreaId=@area) AND (@type IS NULL OR rt.Id=@type)
                AND (@category IS NULL OR rt.CategoryId=@category)
                AND NOT EXISTS (SELECT 1 FROM OPENJSON(@amenities) x
                    WHERE NOT EXISTS (SELECT 1 FROM dbo.HomestayAmenities ha WHERE ha.HomestayId=h.Id AND ha.AmenityId=CONVERT(int,x.value)))
                AND EXISTS (SELECT 1 FROM dbo.Rooms r WHERE r.RoomTypeId=rt.Id AND r.IsActive=1);
            SELECT * FROM #types ORDER BY HomestayName, RoomTypeName, RoomTypeId;
            SELECT r.RoomTypeId, r.Id AS RoomId, r.RoomCode,
                CAST(CASE WHEN EXISTS (SELECT 1 FROM dbo.RoomNights rn
                    LEFT JOIN dbo.Bookings b ON b.Id=rn.BookingId LEFT JOIN dbo.BookingChangeRequests cr ON cr.Id=rn.ChangeRequestId
                    WHERE rn.RoomId=r.Id AND rn.[Date]>=@in AND rn.[Date]<@out AND {StaffReadSql.Occupies}) THEN 0 ELSE 1 END AS bit) AS IsAvailable
            FROM dbo.Rooms r JOIN #types t ON t.RoomTypeId=r.RoomTypeId WHERE r.IsActive=1 ORDER BY r.RoomCode, r.Id;
            SELECT p.RoomTypeId, p.DayType, p.Price, p.EffectiveFrom FROM dbo.RoomTypePrices p
            JOIN #types t ON t.RoomTypeId=p.RoomTypeId WHERE p.EffectiveFrom<@out;
            SELECT [Date] FROM dbo.Holidays WHERE [Date]>=@in AND [Date]<@out;
            SELECT p.Id, p.HomestayId, p.Name, p.Scope, p.IsActive, r.MinDaysBefore, r.RefundPercent
            FROM dbo.CancellationPolicies p LEFT JOIN dbo.CancellationRules r ON r.PolicyId=p.Id
            WHERE p.IsActive=1 AND (p.HomestayId IS NULL OR EXISTS (SELECT 1 FROM #types t WHERE t.HomestayId=p.HomestayId))
            ORDER BY p.Id, r.MinDaysBefore DESC;
            """);
        command.Parameters.AddDate("@in", request.CheckIn);
        command.Parameters.AddDate("@out", request.CheckOut);
        command.Parameters.AddInt32("@area", request.AreaId);
        command.Parameters.AddInt32("@category", request.CategoryId);
        command.Parameters.AddInt32("@type", roomTypeId);
        command.Parameters.AddInt32("@guests", request.Guests);
        command.Parameters.AddNVarChar("@amenities", -1, System.Text.Json.JsonSerializer.Serialize(request.AmenityIds.Distinct()));
        command.Parameters.AddUtcDateTime("@now", now);
        return await db.RunAsync(command, async (cmd, token) =>
        {
            var reader = await cmd.ExecuteReaderAsync(token).ConfigureAwait(false);
            await using var scope = reader.ConfigureAwait(false);
            var candidates = await StaffReadSql.ReadSetAsync(reader, r => new AvailabilityCandidate(r.GetInt32("HomestayId"), r.GetString("HomestayName"),
                r.GetInt32("AreaId"), r.GetString("AreaName"), r.GetInt32("RoomTypeId"), r.GetString("RoomTypeName"), r.GetInt32("CategoryId"),
                r.GetString("CategoryName"), r.GetInt32("MaxGuests"), r.GetDecimal("CommissionRate")), token).ConfigureAwait(false);
            await reader.NextResultAsync(token).ConfigureAwait(false);
            var rooms = await StaffReadSql.ReadSetAsync(reader, r => new CandidateRoom(r.GetInt32("RoomTypeId"), r.GetInt32("RoomId"), r.GetString("RoomCode"), r.GetBoolean("IsAvailable")), token).ConfigureAwait(false);
            await reader.NextResultAsync(token).ConfigureAwait(false);
            var prices = await StaffReadSql.ReadSetAsync(reader, r => new CandidatePrice(r.GetInt32("RoomTypeId"), new(r.GetEnum<DayType>("DayType"), r.GetDateOnly("EffectiveFrom"), r.GetDecimal("Price"))), token).ConfigureAwait(false);
            await reader.NextResultAsync(token).ConfigureAwait(false);
            var holidays = await StaffReadSql.ReadSetAsync(reader, r => r.GetDateOnly("Date"), token).ConfigureAwait(false);
            await reader.NextResultAsync(token).ConfigureAwait(false);
            var policies = await StaffReadSql.ReadSetAsync(reader, r => (Id: r.GetInt32("Id"), HomestayId: r.GetNullableInt32("HomestayId"), Name: r.GetString("Name"),
                Scope: r.GetEnum<CancellationScope>("Scope"), IsActive: r.GetBoolean("IsActive"), Days: r.GetNullableInt32("MinDaysBefore"), Percent: r.GetNullableInt32("RefundPercent")), token).ConfigureAwait(false);
            return new AvailabilityData(candidates, rooms, prices, holidays.ToHashSet(), policies.GroupBy(p => p.Id).Select(g =>
                new ApplicableCancellationPolicy(g.Key, g.First().HomestayId, g.First().Name, g.First().Scope, g.First().IsActive,
                    g.Where(p => p.Days.HasValue).Select(p => new CancellationRuleItem(p.Days!.Value, p.Percent!.Value)).ToArray())).ToArray());
        }, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<RoomCalendarRow>> CalendarAsync(DbSession db, RoomCalendarRequest request, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand($"""
            WITH dates AS (SELECT @from AS [Date], 1 AS n UNION ALL SELECT DATEADD(day,1,[Date]), n+1 FROM dates WHERE n<@days)
            SELECT h.Id AS HomestayId, h.Name AS HomestayName, rt.Id AS RoomTypeId, rt.Name AS RoomTypeName, r.Id AS RoomId, r.RoomCode, d.[Date],
                CASE WHEN {StaffReadSql.Occupies} THEN CASE
                    WHEN rn.RoomBlockId IS NOT NULL THEN CASE WHEN rb.Reason='OtherChannel' THEN 'OtherChannelBlock' ELSE 'MaintenanceBlock' END
                    WHEN rn.ChangeRequestId IS NOT NULL THEN 'ChangeRequestHold'
                    WHEN b.Status IN ('AwaitingPayment','PaymentSubmitted') THEN 'Held'
                    WHEN b.Status IN ('Deposited','Confirmed') THEN 'Booked'
                    WHEN b.Status='CheckedIn' THEN 'Occupied' ELSE 'Stayed' END
                WHEN r.IsActive=0 OR rt.IsActive=0 THEN 'OffSale' ELSE 'Available' END AS State,
                CASE WHEN {StaffReadSql.Occupies} THEN COALESCE(b.Id,cr.BookingId) END AS BookingId,
                CASE WHEN {StaffReadSql.Occupies} THEN COALESCE(b.Code,cb.Code) END AS BookingCode,
                CASE WHEN {StaffReadSql.Occupies} THEN c.FullName END AS CustomerName,
                CASE WHEN b.Status='AwaitingPayment' AND {StaffReadSql.Occupies}
                        AND NOT EXISTS(SELECT 1 FROM dbo.Payments p WHERE p.BookingId=b.Id AND p.Type='Deposit' AND p.Status='Pending') THEN b.HoldExpiresAt
                    WHEN rn.ChangeRequestId IS NOT NULL AND {StaffReadSql.Occupies} THEN cr.PaymentDeadlineAt END AS HoldExpiresAt,
                CASE WHEN {StaffReadSql.Occupies} THEN rn.RoomBlockId END AS RoomBlockId,
                CASE WHEN {StaffReadSql.Occupies} THEN rn.ChangeRequestId END AS ChangeRequestId
            FROM dbo.Rooms r JOIN dbo.RoomTypes rt ON rt.Id=r.RoomTypeId JOIN dbo.Homestays h ON h.Id=rt.HomestayId CROSS JOIN dates d
            LEFT JOIN dbo.RoomNights rn ON rn.RoomId=r.Id AND rn.[Date]=d.[Date]
            LEFT JOIN dbo.Bookings b ON b.Id=rn.BookingId LEFT JOIN dbo.RoomBlocks rb ON rb.Id=rn.RoomBlockId
            LEFT JOIN dbo.BookingChangeRequests cr ON cr.Id=rn.ChangeRequestId LEFT JOIN dbo.Bookings cb ON cb.Id=cr.BookingId
            LEFT JOIN dbo.Customers c ON c.Id=COALESCE(b.CustomerId,cb.CustomerId)
            WHERE (@home IS NULL OR h.Id=@home) AND (@area IS NULL OR h.AreaId=@area)
            ORDER BY h.Name, h.Id, rt.Name, rt.Id, r.RoomCode, r.Id, d.[Date] OPTION (MAXRECURSION 31);
            """);
        command.Parameters.AddDate("@from", request.FromDate);
        command.Parameters.AddInt32("@days", request.Days);
        command.Parameters.AddInt32("@home", request.HomestayId);
        command.Parameters.AddInt32("@area", request.AreaId);
        command.Parameters.AddUtcDateTime("@now", now);
        var rows = await db.QueryAsync(command, r => (HomestayId: r.GetInt32("HomestayId"), HomestayName: r.GetString("HomestayName"), RoomTypeId: r.GetInt32("RoomTypeId"),
            RoomTypeName: r.GetString("RoomTypeName"), RoomId: r.GetInt32("RoomId"), RoomCode: r.GetString("RoomCode"),
            Cell: new RoomCalendarCell(r.GetDateOnly("Date"), r.GetEnum<RoomCalendarState>("State"), r.GetNullableInt32("BookingId"), r.GetNullableString("BookingCode"),
                r.GetNullableString("CustomerName"), r.GetNullableUtcDateTime("HoldExpiresAt"), r.GetNullableInt32("RoomBlockId"), r.GetNullableInt32("ChangeRequestId"))), ct).ConfigureAwait(false);
        return rows.GroupBy(r => r.RoomId).Select(g => new RoomCalendarRow(g.First().HomestayId, g.First().HomestayName, g.First().RoomTypeId,
            g.First().RoomTypeName, g.Key, g.First().RoomCode, g.Select(r => r.Cell).ToArray())).ToArray();
    }
}
