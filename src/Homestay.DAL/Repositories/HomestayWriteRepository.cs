using System.Data;
using Homestay.DAL.Infrastructure;
using Homestay.DTO.Catalog;
using Homestay.DTO.Enums;
using Homestay.DTO.Homestays;
using Microsoft.Data.SqlClient;

namespace Homestay.DAL.Repositories;

public sealed partial class HomestayRepository
{
    public async Task<HomestayItem?> GetForUpdateAsync(UnitOfWork db, int id, string scope, Action<SqlCommand> addScope, CancellationToken ct)
    {
        using var c = db.CreateCommand($"SELECT h.Id FROM dbo.Homestays h WITH (UPDLOCK,HOLDLOCK) WHERE h.Id=@id AND ({scope});");
        c.Parameters.AddInt32("@id", id); addScope(c);
        if (await db.ScalarAsync(c, ct).ConfigureAwait(false) is null) return null;
        return await GetAsync(db, id, scope, addScope, ct).ConfigureAwait(false);
    }
    public async Task<bool> AreaExistsAsync(UnitOfWork db, int id, string scope, Action<SqlCommand> addScope, CancellationToken ct)
    {
        using var c = db.CreateCommand($"SELECT a.Id FROM dbo.Areas a WITH (HOLDLOCK) WHERE a.Id=@id AND ({scope});");
        c.Parameters.AddInt32("@id", id); addScope(c);
        return await db.ScalarAsync(c, ct).ConfigureAwait(false) is not null;
    }
    private static void Values(SqlCommand c, CreateHomestayRequest v)
    {
        c.Parameters.AddInt32("@owner", v.OwnerId); c.Parameters.AddInt32("@area", v.AreaId);
        c.Parameters.AddNVarChar("@name", 200, v.Name); c.Parameters.AddNVarChar("@address", 300, v.Address);
        c.Parameters.AddVarChar("@phone", 16, v.ContactPhone); c.Parameters.AddNVarChar("@description", 4000, v.Description);
        c.Parameters.AddNVarChar("@guide", 2000, v.CheckinGuide);
        c.Parameters.Add(new SqlParameter("@checkin", SqlDbType.Time) { Value = v.CheckinTime.ToTimeSpan(), Scale = 0 });
        c.Parameters.Add(new SqlParameter("@checkout", SqlDbType.Time) { Value = v.CheckoutTime.ToTimeSpan(), Scale = 0 });
    }
    public async Task<int> InsertAsync(UnitOfWork db, CreateHomestayRequest values, int user, DateTime now, CancellationToken ct)
    {
        using var c = db.CreateCommand("""
            INSERT dbo.Homestays(OwnerId,AreaId,Name,Address,ContactPhone,Description,CheckinGuide,CheckinTime,CheckoutTime,Status,CreatedByUserId,CreatedAt)
            OUTPUT inserted.Id VALUES(@owner,@area,@name,@address,@phone,@description,@guide,@checkin,@checkout,'Draft',@user,@now);
            """);
        Values(c, values); c.Parameters.AddInt32("@user", user); c.Parameters.AddUtcDateTime("@now", now);
        return (int)(await db.ScalarAsync(c, ct).ConfigureAwait(false))!;
    }
    // The only UPDATE of Homestays: used for edits, child token bumps and every status transition.
    public async Task<int> WriteAsync(UnitOfWork db, HomestayItem old, CreateHomestayRequest? values,
        HomestayStatus status, string? reason, int employee, DateTime now, CancellationToken ct)
    {
        using var c = db.CreateCommand("""
            UPDATE dbo.Homestays SET OwnerId=@owner,AreaId=@area,Name=@name,Address=@address,ContactPhone=@phone,
                Description=@description,CheckinGuide=@guide,CheckinTime=@checkin,CheckoutTime=@checkout,
                Status=@to,StatusReason=@reason,UpdatedAt=@now,
                SubmittedAt=CASE WHEN @from='Draft' AND @to='PendingReview' THEN @now ELSE SubmittedAt END,
                ApprovedByEmployeeId=CASE WHEN @from<>@to AND @to='Active' THEN @employee ELSE ApprovedByEmployeeId END,
                ApprovedAt=CASE WHEN @from<>@to AND @to='Active' THEN @now ELSE ApprovedAt END
            WHERE Id=@id AND Status=@from AND RowVer=@version;
            """);
        Values(c, values ?? new() { OwnerId = old.OwnerId, AreaId = old.AreaId, Name = old.Name, Address = old.Address,
            ContactPhone = old.ContactPhone, Description = old.Description, CheckinGuide = old.CheckinGuide, CheckinTime = old.CheckinTime, CheckoutTime = old.CheckoutTime });
        c.Parameters.AddInt32("@id", old.Id); c.Parameters.AddRowVersion("@version", old.RowVer);
        c.Parameters.AddEnum("@from", 20, (HomestayStatus?)old.Status); c.Parameters.AddEnum("@to", 20, (HomestayStatus?)status);
        c.Parameters.AddNVarChar("@reason", 500, reason); c.Parameters.AddInt32("@employee", employee); c.Parameters.AddUtcDateTime("@now", now);
        return await db.ExecuteAsync(c, ct).ConfigureAwait(false);
    }
    public async Task<bool> AmenityActiveAsync(UnitOfWork db, int id, CancellationToken ct)
    {
        using var c = db.CreateCommand("SELECT Id FROM dbo.Amenities WITH (HOLDLOCK) WHERE Id=@id AND IsActive=1;"); c.Parameters.AddInt32("@id", id);
        return await db.ScalarAsync(c, ct).ConfigureAwait(false) is not null;
    }
    public async Task SetAmenitiesAsync(UnitOfWork db, int home, IReadOnlyList<int> ids, CancellationToken ct)
    {
        using var clear = db.CreateCommand("DELETE dbo.HomestayAmenities WHERE HomestayId=@id;"); clear.Parameters.AddInt32("@id", home);
        await db.ExecuteAsync(clear, ct).ConfigureAwait(false);
        foreach (var id in ids)
        {
            using var c = db.CreateCommand("INSERT dbo.HomestayAmenities(HomestayId,AmenityId) VALUES(@home,@id);");
            c.Parameters.AddInt32("@home", home); c.Parameters.AddInt32("@id", id); await db.ExecuteAsync(c, ct).ConfigureAwait(false);
        }
    }
    public async Task<bool> RoomTypeBelongsAsync(UnitOfWork db, int home, int type, CancellationToken ct)
    {
        using var c = db.CreateCommand("SELECT Id FROM dbo.RoomTypes WITH (HOLDLOCK) WHERE Id=@id AND HomestayId=@home;");
        c.Parameters.AddInt32("@id", type); c.Parameters.AddInt32("@home", home);
        return await db.ScalarAsync(c, ct).ConfigureAwait(false) is not null;
    }
    public async Task<bool> DuplicateFileAsync(UnitOfWork db, int home, bool document, byte[] sha, DocumentType? type, CancellationToken ct)
    {
        using var c = db.CreateCommand(document
            ? "SELECT TOP(1) d.Id FROM dbo.VerificationDocuments d JOIN dbo.StoredFiles f ON f.Id=d.FileId AND f.Category='Document' WHERE d.HomestayId=@home AND d.IsSuperseded=0 AND d.DocumentType=@type AND f.Sha256=@sha;"
            : "SELECT TOP(1) i.Id FROM dbo.HomestayImages i JOIN dbo.StoredFiles f ON f.Id=i.FileId AND f.Category='HomestayImage' WHERE i.HomestayId=@home AND f.Sha256=@sha;");
        c.Parameters.AddInt32("@home", home); c.Parameters.AddVarBinary("@sha", sha); c.Parameters.AddEnum("@type", 30, type);
        return await db.ScalarAsync(c, ct).ConfigureAwait(false) is not null;
    }
    private static void ImageValues(SqlCommand c, int home, int? type, string? caption, int sort, bool cover, DateTime now)
    {
        c.Parameters.AddInt32("@home", home); c.Parameters.AddInt32("@type", type); c.Parameters.AddNVarChar("@caption", 200, caption);
        c.Parameters.AddInt32("@sort", sort); c.Parameters.AddBoolean("@cover", cover); c.Parameters.AddUtcDateTime("@now", now);
    }
    public async Task ClearCoverAsync(UnitOfWork db, int home, DateTime now, CancellationToken ct)
    {
        using var c = db.CreateCommand("UPDATE dbo.HomestayImages SET IsCover=0,UpdatedAt=@now WHERE HomestayId=@home AND IsCover=1;");
        c.Parameters.AddInt32("@home", home); c.Parameters.AddUtcDateTime("@now", now); await db.ExecuteAsync(c, ct).ConfigureAwait(false);
    }
    public async Task<int> InsertImageAsync(UnitOfWork db, int home, Guid file, int? type, string? caption, int sort, bool cover, DateTime now, CancellationToken ct)
    {
        using var c = db.CreateCommand("INSERT dbo.HomestayImages(HomestayId,FileId,RoomTypeId,Caption,SortOrder,IsCover,CreatedAt) OUTPUT inserted.Id VALUES(@home,@file,@type,@caption,@sort,@cover,@now);");
        ImageValues(c, home, type, caption, sort, cover, now); c.Parameters.AddGuid("@file", file);
        return (int)(await db.ScalarAsync(c, ct).ConfigureAwait(false))!;
    }
    public async Task UpdateImageAsync(UnitOfWork db, int home, int image, int? type, string? caption, int sort, bool cover, DateTime now, CancellationToken ct)
    {
        using var c = db.CreateCommand("UPDATE dbo.HomestayImages SET RoomTypeId=@type,Caption=@caption,SortOrder=@sort,IsCover=@cover,UpdatedAt=@now WHERE Id=@id AND HomestayId=@home;");
        ImageValues(c, home, type, caption, sort, cover, now); c.Parameters.AddInt32("@id", image); await db.ExecuteAsync(c, ct).ConfigureAwait(false);
    }
    public async Task RemoveImageAsync(UnitOfWork db, int home, int image, CancellationToken ct)
    {
        using var c = db.CreateCommand("DELETE dbo.HomestayImages WHERE Id=@id AND HomestayId=@home;");
        c.Parameters.AddInt32("@id", image); c.Parameters.AddInt32("@home", home); await db.ExecuteAsync(c, ct).ConfigureAwait(false);
    }
    public async Task SupersedeDocumentsAsync(UnitOfWork db, int home, DocumentType? type, int? document, DateTime now, CancellationToken ct)
    {
        using var c = db.CreateCommand("UPDATE dbo.VerificationDocuments SET IsSuperseded=1,UpdatedAt=@now WHERE HomestayId=@home AND IsSuperseded=0 AND (@type IS NULL OR DocumentType=@type) AND (@id IS NULL OR Id=@id);");
        c.Parameters.AddInt32("@home", home); c.Parameters.AddInt32("@id", document); c.Parameters.AddEnum("@type", 30, type); c.Parameters.AddUtcDateTime("@now", now);
        await db.ExecuteAsync(c, ct).ConfigureAwait(false);
    }
    public async Task<int> InsertDocumentAsync(UnitOfWork db, int home, DocumentType type, Guid file, DateOnly? expiry, int user, DateTime now, CancellationToken ct)
    {
        using var c = db.CreateCommand("INSERT dbo.VerificationDocuments(HomestayId,DocumentType,FileId,ExpiryDate,UploadedByUserId,CreatedAt) OUTPUT inserted.Id VALUES(@home,@type,@file,@expiry,@user,@now);");
        c.Parameters.AddInt32("@home", home); c.Parameters.AddEnum("@type", 30, (DocumentType?)type); c.Parameters.AddGuid("@file", file);
        c.Parameters.AddDate("@expiry", expiry); c.Parameters.AddInt32("@user", user); c.Parameters.AddUtcDateTime("@now", now);
        return (int)(await db.ScalarAsync(c, ct).ConfigureAwait(false))!;
    }
    public async Task<int> InsertPolicyAsync(UnitOfWork db, int home, CreateCancellationPolicyRequest policy, DateTime now, CancellationToken ct)
    {
        if (policy.Activate)
        {
            using var disable = db.CreateCommand("UPDATE dbo.CancellationPolicies SET IsActive=0,UpdatedAt=@now WHERE HomestayId=@home AND Scope=@scope AND IsActive=1;");
            disable.Parameters.AddInt32("@home", home); disable.Parameters.AddEnum("@scope", 10, (CancellationScope?)policy.Scope); disable.Parameters.AddUtcDateTime("@now", now);
            await db.ExecuteAsync(disable, ct).ConfigureAwait(false);
        }
        using var c = db.CreateCommand("INSERT dbo.CancellationPolicies(HomestayId,Scope,Name,IsActive,CreatedAt) OUTPUT inserted.Id VALUES(@home,@scope,@name,@active,@now);");
        c.Parameters.AddInt32("@home", home); c.Parameters.AddEnum("@scope", 10, (CancellationScope?)policy.Scope);
        c.Parameters.AddNVarChar("@name", 100, policy.Name); c.Parameters.AddBoolean("@active", policy.Activate); c.Parameters.AddUtcDateTime("@now", now);
        var id = (int)(await db.ScalarAsync(c, ct).ConfigureAwait(false))!;
        foreach (var rule in policy.Rules)
        {
            using var r = db.CreateCommand("INSERT dbo.CancellationRules(PolicyId,MinDaysBefore,RefundPercent,CreatedAt) VALUES(@id,@days,@percent,@now);");
            r.Parameters.AddInt32("@id", id); r.Parameters.AddInt32("@days", rule.MinDaysBefore); r.Parameters.AddInt32("@percent", rule.RefundPercent); r.Parameters.AddUtcDateTime("@now", now);
            await db.ExecuteAsync(r, ct).ConfigureAwait(false);
        }
        return id;
    }
    public async Task DisablePolicyAsync(UnitOfWork db, int home, int policy, DateTime now, CancellationToken ct)
    {
        using var c = db.CreateCommand("UPDATE dbo.CancellationPolicies SET IsActive=0,UpdatedAt=@now WHERE HomestayId=@home AND Id=@id;");
        c.Parameters.AddInt32("@home", home); c.Parameters.AddInt32("@id", policy); c.Parameters.AddUtcDateTime("@now", now); await db.ExecuteAsync(c, ct).ConfigureAwait(false);
    }
    public async Task<bool> HasActiveContractAsync(UnitOfWork db, int home, CancellationToken ct)
    {
        using var c = db.CreateCommand("SELECT TOP(1) Id FROM dbo.Contracts WITH (HOLDLOCK) WHERE HomestayId=@home AND Status='Active';");
        c.Parameters.AddInt32("@home", home); return await db.ScalarAsync(c, ct).ConfigureAwait(false) is not null;
    }
}
