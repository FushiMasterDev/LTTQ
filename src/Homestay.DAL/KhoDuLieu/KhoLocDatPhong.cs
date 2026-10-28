using Homestay.DAL.Infrastructure;
using Homestay.DTO.Common;
using Microsoft.Data.SqlClient;
namespace Homestay.DAL.Repositories;

/// <summary>Truy vấn dữ liệu cho bộ lọc đơn: homestay, khu vực, người phụ trách.</summary>
public sealed partial class BookingRepository
{
    public Task<IReadOnlyList<Lookup>> FilterHomestaysAsync(DbSession db, int? areaId, string homes, Action<SqlCommand> addScope, CancellationToken ct) =>
        ListingReadSql.LookupAsync(db, $"SELECT h.Id,h.Name,CAST(CASE WHEN h.Status='Active' THEN 1 ELSE 0 END AS bit) AS IsActive FROM dbo.Homestays h WHERE ({homes}) AND (@area IS NULL OR h.AreaId=@area) ORDER BY h.Name,h.Id;",
            c => { addScope(c); c.Parameters.AddInt32("@area", areaId); }, ct);
    public Task<IReadOnlyList<Lookup>> FilterAreasAsync(DbSession db, string homes, Action<SqlCommand> addScope, CancellationToken ct) =>
        ListingReadSql.LookupAsync(db, $"SELECT a.Id,a.Name,a.IsActive FROM dbo.Areas a WHERE EXISTS(SELECT 1 FROM dbo.Homestays h WHERE h.AreaId=a.Id AND ({homes})) ORDER BY a.Name,a.Id;", addScope, ct);
    public Task<IReadOnlyList<Lookup>> FilterAssigneesAsync(DbSession db, int? areaId, int? homeId, string scope, Action<SqlCommand> addScope, CancellationToken ct) =>
        ListingReadSql.LookupAsync(db, $"""
            SELECT e.Id,CONCAT(e.EmployeeCode,N' · ',e.FullName) AS Name,CAST(CASE WHEN e.EmploymentStatus='Active' THEN 1 ELSE 0 END AS bit) AS IsActive
            FROM dbo.Employees e WHERE EXISTS(SELECT 1 FROM dbo.Bookings b JOIN dbo.Homestays h ON h.Id=b.HomestayId
                WHERE b.AssignedEmployeeId=e.Id AND ({scope}) AND (@area IS NULL OR h.AreaId=@area) AND (@home IS NULL OR h.Id=@home))
            ORDER BY e.EmployeeCode,e.Id;
            """, c => { addScope(c); c.Parameters.AddInt32("@area", areaId); c.Parameters.AddInt32("@home", homeId); }, ct);
}
