using Homestay.DAL.Infrastructure;
using Homestay.DTO.Bookings;
using Homestay.DTO.Catalog;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.DTO.Homestays;
using Microsoft.Data.SqlClient;
namespace Homestay.DAL.Repositories;

/// <summary>Truy vấn đọc homestay: danh sách, chi tiết, khu vực, tiện nghi, ảnh, giấy tờ, chính sách, điều kiện duyệt.</summary>
public sealed partial class HomestayRepository
{
    private const string From = "FROM dbo.Homestays h JOIN dbo.Owners o ON o.Id=h.OwnerId JOIN dbo.Areas a ON a.Id=h.AreaId ";
    private const string Columns = """
        h.*,o.FullName AS OwnerName,a.Name AS AreaName,
        (SELECT COUNT(*) FROM dbo.RoomTypes t WHERE t.HomestayId=h.Id) AS RoomTypeCount,
        (SELECT COUNT(*) FROM dbo.Rooms r JOIN dbo.RoomTypes t ON t.Id=r.RoomTypeId WHERE t.HomestayId=h.Id) AS RoomCount,
        (SELECT c.ContractNo FROM dbo.Contracts c WHERE c.HomestayId=h.Id AND c.Status='Active') AS ActiveContractNo,
        (SELECT c.EndDate FROM dbo.Contracts c WHERE c.HomestayId=h.Id AND c.Status='Active') AS ContractEndDate,
        (SELECT c.CommissionRate FROM dbo.Contracts c WHERE c.HomestayId=h.Id AND c.Status='Active') AS CommissionRate
        """;
    private static HomestayItem Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32("Id"),
        OwnerId = r.GetInt32("OwnerId"),
        OwnerName = r.GetString("OwnerName"),
        AreaId = r.GetInt32("AreaId"),
        AreaName = r.GetString("AreaName"),
        Name = r.GetString("Name"),
        Address = r.GetString("Address"),
        ContactPhone = r.GetNullableString("ContactPhone"),
        Description = r.GetNullableString("Description"),
        CheckinGuide = r.GetNullableString("CheckinGuide"),
        CheckinTime = TimeOnly.FromTimeSpan(r.GetTimeSpan(r.GetOrdinal("CheckinTime"))),
        CheckoutTime = TimeOnly.FromTimeSpan(r.GetTimeSpan(r.GetOrdinal("CheckoutTime"))),
        Status = r.GetEnum<HomestayStatus>("Status"),
        StatusReason = r.GetNullableString("StatusReason"),
        SubmittedAtUtc = r.GetNullableUtcDateTime("SubmittedAt"),
        ApprovedByEmployeeId = r.GetNullableInt32("ApprovedByEmployeeId"),
        ApprovedAtUtc = r.GetNullableUtcDateTime("ApprovedAt"),
        CreatedByUserId = r.GetInt32("CreatedByUserId"),
        RoomTypeCount = r.GetInt32("RoomTypeCount"),
        RoomCount = r.GetInt32("RoomCount"),
        ActiveContractNo = r.GetNullableString("ActiveContractNo"),
        ContractEndDate = r.GetNullableDateOnly("ContractEndDate"),
        CommissionRate = r.GetNullableDecimal("CommissionRate"),
        RowVer = r.GetRowVersion("RowVer")
    };
    public Task<PagedResult<HomestayItem>> ListAsync(DbSession db, HomestaySearchRequest request, string scope, Action<SqlCommand> addScope, CancellationToken ct)
    {
        var where = $" WHERE ({scope}) AND (@status IS NULL OR h.Status=@status) AND (@pending=0 OR h.Status='PendingReview') AND (@area IS NULL OR h.AreaId=@area) AND (@owner IS NULL OR h.OwnerId=@owner) AND (@search=N'' OR h.Name COLLATE Vietnamese_CI_AI LIKE @search ESCAPE '~')";
        void Parameters(SqlCommand c) { addScope(c); c.Parameters.AddEnum("@status", 20, request.Status); c.Parameters.AddBoolean("@pending", request.PendingReviewOnly); c.Parameters.AddInt32("@area", request.AreaId); c.Parameters.AddInt32("@owner", request.OwnerId); c.Parameters.AddNVarChar("@search", 500, StaffReadSql.Like(request.Page.SearchText)); }
        var sort = request.Page.SortColumn switch { "Id" => "h.Id", "OwnerName" => "o.FullName", "AreaName" => "a.Name", "Status" => "h.Status", _ => "h.Name" };
        return ListingReadSql.PageAsync(db, request.Page, Columns, From, where, sort, "h.Id", Parameters, Map, ct);
    }
    public async Task<HomestayItem?> GetAsync(DbSession db, int id, string scope, Action<SqlCommand> addScope, CancellationToken ct)
    {
        using var c = db.CreateCommand($"SELECT {Columns} {From} WHERE h.Id=@id AND ({scope});"); addScope(c); c.Parameters.AddInt32("@id", id);
        return (await db.QueryAsync(c, Map, ct).ConfigureAwait(false)).SingleOrDefault();
    }
    public Task<IReadOnlyList<Lookup>> LookupAsync(DbSession db, string scope, Action<SqlCommand> addScope, CancellationToken ct) =>
        ListingReadSql.LookupAsync(db, $"SELECT h.Id,h.Name,CAST(CASE WHEN h.Status='Active' THEN 1 ELSE 0 END AS bit) AS IsActive FROM dbo.Homestays h WHERE ({scope}) ORDER BY h.Name,h.Id;", addScope, ct);
    public Task<IReadOnlyList<Lookup>> AreasAsync(DbSession db, string scope, Action<SqlCommand> addScope, CancellationToken ct) =>
        ListingReadSql.LookupAsync(db, $"SELECT a.Id,a.Name,a.IsActive FROM dbo.Areas a WHERE ({scope}) ORDER BY a.Name,a.Id;", addScope, ct);
    public Task<IReadOnlyList<Lookup>> AmenitiesAsync(DbSession db, int? home, CancellationToken ct) =>
        ListingReadSql.LookupAsync(db, "SELECT a.Id,a.Name,a.IsActive FROM dbo.Amenities a WHERE (@id IS NULL AND a.IsActive=1) OR EXISTS(SELECT 1 FROM dbo.HomestayAmenities ha WHERE ha.AmenityId=a.Id AND ha.HomestayId=@id) ORDER BY a.Name,a.Id;", c => c.Parameters.AddInt32("@id", home), ct);
    public Task<IReadOnlyList<Lookup>> CategoriesAsync(DbSession db, CancellationToken ct) =>
        ListingReadSql.LookupAsync(db, "SELECT Id,Name,IsActive FROM dbo.RoomCategories WHERE IsActive=1 ORDER BY Name,Id;", _ => { }, ct);
    private static ListingFileMetadata File(SqlDataReader r) => new(r.GetGuid("FileId"), r.GetString("FileName"), r.GetString("ContentType"), r.GetInt32("SizeBytes"), r.GetRowVersion("Sha256"));
    public async Task<IReadOnlyList<HomestayImageItem>> ImagesAsync(DbSession db, int home, CancellationToken ct)
    {
        using var c = db.CreateCommand("SELECT i.*,f.FileName,f.ContentType,f.SizeBytes,f.Sha256 FROM dbo.HomestayImages i JOIN dbo.StoredFiles f ON f.Id=i.FileId AND f.Category='HomestayImage' WHERE i.HomestayId=@id ORDER BY i.SortOrder,i.Id;"); c.Parameters.AddInt32("@id", home);
        return await db.QueryAsync(c, r => new HomestayImageItem(r.GetInt32("Id"), r.GetNullableInt32("RoomTypeId"), r.GetNullableString("Caption"), r.GetInt32("SortOrder"), r.GetBoolean("IsCover"), File(r)), ct).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<VerificationDocumentItem>> DocumentsAsync(DbSession db, int home, CancellationToken ct)
    {
        using var c = db.CreateCommand("SELECT d.*,f.FileName,f.ContentType,f.SizeBytes,f.Sha256 FROM dbo.VerificationDocuments d JOIN dbo.StoredFiles f ON f.Id=d.FileId AND f.Category='Document' WHERE d.HomestayId=@id ORDER BY d.DocumentType,d.CreatedAt DESC,d.Id DESC;"); c.Parameters.AddInt32("@id", home);
        return await db.QueryAsync(c, r => new VerificationDocumentItem(r.GetInt32("Id"), r.GetEnum<DocumentType>("DocumentType"), r.GetNullableDateOnly("ExpiryDate"), r.GetInt32("UploadedByUserId"), r.GetBoolean("IsSuperseded"), File(r)), ct).ConfigureAwait(false);
    }
    public async Task<IReadOnlyList<CancellationPolicyItem>> PoliciesAsync(DbSession db, int home, CancellationToken ct)
    {
        using var c = db.CreateCommand("SELECT p.Id,p.Name,p.Scope,p.IsActive,p.CreatedAt,r.MinDaysBefore,r.RefundPercent FROM dbo.CancellationPolicies p LEFT JOIN dbo.CancellationRules r ON r.PolicyId=p.Id WHERE p.HomestayId=@id ORDER BY p.Id,r.MinDaysBefore DESC;"); c.Parameters.AddInt32("@id", home);
        var rows = await db.QueryAsync(c, r => new { Id = r.GetInt32("Id"), Name = r.GetString("Name"), Scope = r.GetEnum<CancellationScope>("Scope"), Active = r.GetBoolean("IsActive"), Created = r.GetUtcDateTime("CreatedAt"), Days = r.GetNullableInt32("MinDaysBefore"), Percent = r.GetNullableInt32("RefundPercent") }, ct).ConfigureAwait(false);
        return rows.GroupBy(r => r.Id).Select(g => new CancellationPolicyItem { Id = g.Key, Name = g.First().Name, Scope = g.First().Scope, IsActive = g.First().Active, CreatedAtUtc = g.First().Created, Rules = g.Where(r => r.Days.HasValue).Select(r => new CancellationRuleItem(r.Days!.Value, r.Percent!.Value)).ToArray() }).ToArray();
    }
    public async Task<HomestayApprovalChecklist> ApprovalAsync(DbSession db, int home, DateTime now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now.AddHours(7));
        using var c = db.CreateCommand("""
            SELECT CAST(CASE WHEN o.Status='Approved' THEN 1 ELSE 0 END AS bit) AS OwnerApproved,
                CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.Contracts c WHERE c.HomestayId=h.Id AND c.Status='Active' AND c.StartDate<=@today AND c.EndDate>=@today) THEN 1 ELSE 0 END AS bit) AS ActiveContract,
                CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.VerificationDocuments d JOIN dbo.StoredFiles f ON f.Id=d.FileId AND f.Category='Document' WHERE d.HomestayId=h.Id AND d.DocumentType='BusinessLicense' AND d.IsSuperseded=0 AND (d.ExpiryDate IS NULL OR d.ExpiryDate>=@today)) THEN 1 ELSE 0 END AS bit) AS BusinessLicense,
                CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.VerificationDocuments d JOIN dbo.StoredFiles f ON f.Id=d.FileId AND f.Category='Document' WHERE d.HomestayId=h.Id AND d.DocumentType='FireSafety' AND d.IsSuperseded=0 AND (d.ExpiryDate IS NULL OR d.ExpiryDate>=@today)) THEN 1 ELSE 0 END AS bit) AS FireSafety,
                CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.RoomTypes t WHERE t.HomestayId=h.Id AND t.IsActive=1
                    AND EXISTS(SELECT 1 FROM dbo.Rooms r WHERE r.RoomTypeId=t.Id AND r.IsActive=1)
                    AND 3=(SELECT COUNT(DISTINCT p.DayType) FROM dbo.RoomTypePrices p WHERE p.RoomTypeId=t.Id AND p.EffectiveFrom<=@today)) THEN 1 ELSE 0 END AS bit) AS SellableRoom,
                CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.HomestayImages i JOIN dbo.StoredFiles f ON f.Id=i.FileId AND f.Category='HomestayImage' WHERE i.HomestayId=h.Id AND i.IsCover=1) THEN 1 ELSE 0 END AS bit) AS CoverImage,
                CAST(CASE WHEN LEN(LTRIM(RTRIM(h.Address)))>0 THEN 1 ELSE 0 END AS bit) AS Address,
                CAST(CASE WHEN LEN(LTRIM(RTRIM(h.ContactPhone)))>0 THEN 1 ELSE 0 END AS bit) AS ContactPhone,
                CAST(CASE WHEN LEN(LTRIM(RTRIM(h.Description)))>0 THEN 1 ELSE 0 END AS bit) AS Description
            FROM dbo.Homestays h JOIN dbo.Owners o ON o.Id=h.OwnerId WHERE h.Id=@id;
            """); c.Parameters.AddInt32("@id", home); c.Parameters.AddDate("@today", today);
        var definitions = new (string Code, string Yes, string No)[] {
            ("OwnerApproved","Chủ đã được duyệt.","Chủ chưa được duyệt."),
            ("ActiveContract","Hợp đồng đang hiệu lực và phủ hôm nay.","Thiếu hợp đồng Active phủ hôm nay."),
            ("BusinessLicense","Giấy phép kinh doanh còn hạn.","Thiếu giấy phép kinh doanh hiện hành còn hạn."),
            ("FireSafety","Giấy tờ PCCC còn hạn.","Thiếu giấy tờ PCCC hiện hành còn hạn."),
            ("SellableRoom","Có loại phòng bật, phòng bật và đủ ba giá hiệu lực hôm nay.","Thiếu loại phòng bật có phòng bật và đủ ba giá hiệu lực hôm nay."),
            ("CoverImage","Đã có ảnh bìa.","Chưa có ảnh bìa."),
            ("Address","Đã có địa chỉ.","Địa chỉ còn trống."),
            ("ContactPhone","Đã có số điện thoại liên hệ.","Số điện thoại liên hệ còn trống."),
            ("Description","Đã có mô tả.","Mô tả còn trống.") };
        var rows = await db.QueryAsync(c, r => definitions.Select(d => new ApprovalCondition(d.Code, r.GetBoolean(d.Code), r.GetBoolean(d.Code) ? d.Yes : d.No)).ToArray(), ct).ConfigureAwait(false);
        return new(home, now, today, rows.Single());
    }
    public async Task<bool> CanEndAsync(DbSession db, int home, DateTime now, CancellationToken ct)
    {
        using var c = db.CreateCommand($"""
            SELECT CAST(CASE WHEN NOT EXISTS(SELECT 1 FROM dbo.Contracts c WHERE c.HomestayId=@id AND c.Status='Active')
                AND NOT EXISTS(SELECT 1 FROM dbo.RoomNights rn JOIN dbo.Rooms r ON r.Id=rn.RoomId JOIN dbo.RoomTypes t ON t.Id=r.RoomTypeId
                    LEFT JOIN dbo.Bookings b ON b.Id=rn.BookingId LEFT JOIN dbo.BookingChangeRequests cr ON cr.Id=rn.ChangeRequestId
                    WHERE t.HomestayId=@id AND rn.RoomBlockId IS NULL AND (b.Id IS NULL OR b.Status<>'Completed') AND {StaffReadSql.Occupies})
                THEN 1 ELSE 0 END AS bit);
            """); c.Parameters.AddInt32("@id", home); c.Parameters.AddUtcDateTime("@now", now);
        return (bool)(await db.ScalarAsync(c, ct).ConfigureAwait(false))!;
    }
    public async Task<IReadOnlyList<BookingAuditItem>> AuditAsync(DbSession db, int home, int? user, CancellationToken ct)
    {
        // Return no raw child-table logs: those can hold financial/legal data and are not linked safely by RecordId alone.
        using var c = db.CreateCommand("SELECT a.*,COALESCE(e.FullName,u.Username) AS ActorName FROM dbo.AuditLogs a LEFT JOIN dbo.Users u ON u.Id=a.UserId LEFT JOIN dbo.Employees e ON e.UserId=u.Id WHERE a.TableName='Homestays' AND a.RecordId=@record AND (@user IS NULL OR a.UserId=@user) ORDER BY a.CreatedAt,a.Id;");
        c.Parameters.AddVarChar("@record", 50, home.ToString(System.Globalization.CultureInfo.InvariantCulture)); c.Parameters.AddInt32("@user", user);
        return await db.QueryAsync(c, r => new BookingAuditItem(r.GetInt64("Id"), r.GetString("ActionCode"), r.GetNullableString("ActorName"), r.GetNullableString("OldValues"), r.GetNullableString("NewValues"), r.GetNullableString("Note"), r.GetUtcDateTime("CreatedAt")), ct).ConfigureAwait(false);
    }
}
