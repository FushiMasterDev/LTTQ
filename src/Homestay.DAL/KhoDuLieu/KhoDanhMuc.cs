using Homestay.DAL.Infrastructure;
using Homestay.DTO.Catalog;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;

namespace Homestay.DAL.Repositories;

/// <summary>Truy vấn các bảng danh mục: khu vực, tiện nghi, loại phòng, ngày lễ, chính sách hủy.</summary>
public sealed class CatalogRepository
{
    public static string TableName(CatalogKind kind) => kind switch
    {
        CatalogKind.Area => "Areas", CatalogKind.Amenity => "Amenities", CatalogKind.RoomCategory => "RoomCategories",
        _ => throw new ArgumentOutOfRangeException(nameof(kind))
    };

    public async Task<IReadOnlyList<AreaItem>> ListAreasAsync(DbSession db, CancellationToken ct)
    {
        using var command = db.CreateCommand("SELECT Id, Name, Province, AreaGroup, IsActive FROM dbo.Areas ORDER BY Province, Name, Id;");
        return await db.QueryAsync(command, r => new AreaItem(r.GetInt32("Id"), r.GetString("Name"), r.GetString("Province"), r.GetEnum<AreaGroup>("AreaGroup"), r.GetBoolean("IsActive")), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CatalogItem>> ListItemsAsync(DbSession db, CatalogKind kind, CancellationToken ct)
    {
        using var command = db.CreateCommand($"SELECT Id, Name, IsActive FROM dbo.{TableName(kind)} ORDER BY Name, Id;");
        return await db.QueryAsync(command, r => new CatalogItem(r.GetInt32("Id"), r.GetString("Name"), r.GetBoolean("IsActive")), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<Lookup>> LookupAsync(DbSession db, CatalogKind kind, CancellationToken ct)
    {
        using var command = db.CreateCommand($"SELECT Id, Name FROM dbo.{TableName(kind)} WHERE IsActive=1 ORDER BY Name, Id;");
        return await db.QueryAsync(command, r => new Lookup { Id = r.GetInt32("Id"), Name = r.GetString("Name") }, ct).ConfigureAwait(false);
    }

    public async Task<int> CreateAreaAsync(UnitOfWork db, AreaRequest request, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("INSERT dbo.Areas (Name, Province, AreaGroup, IsActive, CreatedAt) OUTPUT INSERTED.Id VALUES (@name, @province, @group, 1, @now);");
        command.Parameters.AddNVarChar("@name", 100, request.Name);
        command.Parameters.AddNVarChar("@province", 100, request.Province);
        command.Parameters.AddEnum("@group", 20, (AreaGroup?)request.AreaGroup);
        command.Parameters.AddUtcDateTime("@now", now);
        return (int)(await db.ScalarAsync(command, ct).ConfigureAwait(false))!;
    }

    public async Task<int> UpdateAreaAsync(UnitOfWork db, AreaItem original, AreaRequest request, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            UPDATE dbo.Areas SET Name=@name, Province=@province, AreaGroup=@group, UpdatedAt=@now
            WHERE Id=@id AND Name COLLATE Latin1_General_100_BIN2=@oldName AND Province COLLATE Latin1_General_100_BIN2=@oldProvince AND AreaGroup=@oldGroup AND IsActive=@oldActive;
            """);
        command.Parameters.AddInt32("@id", original.Id);
        command.Parameters.AddNVarChar("@name", 100, request.Name);
        command.Parameters.AddNVarChar("@province", 100, request.Province);
        command.Parameters.AddEnum("@group", 20, (AreaGroup?)request.AreaGroup);
        command.Parameters.AddNVarChar("@oldName", 100, original.Name);
        command.Parameters.AddNVarChar("@oldProvince", 100, original.Province);
        command.Parameters.AddEnum("@oldGroup", 20, (AreaGroup?)original.AreaGroup);
        command.Parameters.AddBoolean("@oldActive", original.IsActive);
        command.Parameters.AddUtcDateTime("@now", now);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<int> CreateItemAsync(UnitOfWork db, CatalogKind kind, string name, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand($"INSERT dbo.{TableName(kind)} (Name, IsActive, CreatedAt) OUTPUT INSERTED.Id VALUES (@name, 1, @now);");
        command.Parameters.AddNVarChar("@name", 100, name);
        command.Parameters.AddUtcDateTime("@now", now);
        return (int)(await db.ScalarAsync(command, ct).ConfigureAwait(false))!;
    }

    public async Task<int> UpdateItemAsync(UnitOfWork db, CatalogKind kind, CatalogItem original, string name, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand($"UPDATE dbo.{TableName(kind)} SET Name=@name, UpdatedAt=@now WHERE Id=@id AND Name COLLATE Latin1_General_100_BIN2=@old AND IsActive=@active;");
        command.Parameters.AddInt32("@id", original.Id);
        command.Parameters.AddNVarChar("@name", 100, name);
        command.Parameters.AddNVarChar("@old", 100, original.Name);
        command.Parameters.AddBoolean("@active", original.IsActive);
        command.Parameters.AddUtcDateTime("@now", now);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<int> SetActiveAsync(UnitOfWork db, CatalogKind kind, int id, bool original, bool active, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand($"UPDATE dbo.{TableName(kind)} SET IsActive=@active, UpdatedAt=@now WHERE Id=@id AND IsActive=@old;");
        command.Parameters.AddInt32("@id", id);
        command.Parameters.AddBoolean("@active", active);
        command.Parameters.AddBoolean("@old", original);
        command.Parameters.AddUtcDateTime("@now", now);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HolidayItem>> ListHolidaysAsync(DbSession db, int year, CancellationToken ct)
    {
        using var command = db.CreateCommand("SELECT [Date], Name FROM dbo.Holidays WHERE [Date]>=@start AND [Date]<=@end ORDER BY [Date];");
        command.Parameters.AddDate("@start", new DateOnly(year, 1, 1));
        command.Parameters.AddDate("@end", new DateOnly(year, 12, 31));
        return await db.QueryAsync(command, r => new HolidayItem(r.GetDateOnly("Date"), r.GetString("Name")), ct).ConfigureAwait(false);
    }

    public async Task<int> CreateHolidayAsync(UnitOfWork db, HolidayItem item, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("INSERT dbo.Holidays ([Date], Name, CreatedAt) VALUES (@date, @name, @now);");
        command.Parameters.AddDate("@date", item.Date);
        command.Parameters.AddNVarChar("@name", 100, item.Name);
        command.Parameters.AddUtcDateTime("@now", now);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<int> RenameHolidayAsync(UnitOfWork db, RenameHolidayRequest request, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("UPDATE dbo.Holidays SET Name=@name, UpdatedAt=@now WHERE [Date]=@date AND Name COLLATE Latin1_General_100_BIN2=@old;");
        command.Parameters.AddDate("@date", request.Date);
        command.Parameters.AddNVarChar("@name", 100, request.Name);
        command.Parameters.AddNVarChar("@old", 100, request.OriginalName);
        command.Parameters.AddUtcDateTime("@now", now);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CancellationPolicyItem>> ListPoliciesAsync(DbSession db, CancellationScope? scope, bool includeInactive, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            SELECT p.Id, p.Name, p.Scope, p.IsActive, p.CreatedAt, r.MinDaysBefore, r.RefundPercent
            FROM dbo.CancellationPolicies p LEFT JOIN dbo.CancellationRules r ON r.PolicyId=p.Id
            WHERE p.HomestayId IS NULL AND (@scope IS NULL OR p.Scope=@scope) AND (@history=1 OR p.IsActive=1)
            ORDER BY p.Scope, p.CreatedAt DESC, p.Id DESC, r.MinDaysBefore DESC;
            """);
        command.Parameters.AddEnum("@scope", 10, scope);
        command.Parameters.AddBoolean("@history", includeInactive);
        // Một câu SELECT giữ chính sách và mốc trong cùng ảnh dữ liệu RCSI.
        var rows = await db.QueryAsync(command, r => (Policy: new CancellationPolicyItem
        {
            Id = r.GetInt32("Id"), Name = r.GetString("Name"), Scope = r.GetEnum<CancellationScope>("Scope"),
            IsActive = r.GetBoolean("IsActive"), CreatedAtUtc = r.GetUtcDateTime("CreatedAt")
        }, Rule: r.GetNullableInt32("MinDaysBefore") is { } days
            ? new CancellationRuleItem(days, r.GetInt32("RefundPercent")) : null), ct).ConfigureAwait(false);
        return rows.GroupBy(row => row.Policy.Id).Select(group => group.First().Policy with
            { Rules = group.Where(row => row.Rule is not null).Select(row => row.Rule!).ToArray() }).ToArray();
    }

    public async Task<int> CreatePolicyAsync(UnitOfWork db, CreateCancellationPolicyRequest request, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("INSERT dbo.CancellationPolicies (HomestayId, Scope, Name, IsActive, CreatedAt) OUTPUT INSERTED.Id VALUES (NULL, @scope, @name, @active, @now);");
        command.Parameters.AddEnum("@scope", 10, (CancellationScope?)request.Scope);
        command.Parameters.AddNVarChar("@name", 100, request.Name);
        command.Parameters.AddBoolean("@active", request.Activate);
        command.Parameters.AddUtcDateTime("@now", now);
        return (int)(await db.ScalarAsync(command, ct).ConfigureAwait(false))!;
    }

    public async Task<int> CreateRuleAsync(UnitOfWork db, int policyId, CancellationRuleItem rule, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("INSERT dbo.CancellationRules (PolicyId, MinDaysBefore, RefundPercent, CreatedAt) VALUES (@policy, @days, @refund, @now);");
        command.Parameters.AddInt32("@policy", policyId);
        command.Parameters.AddInt32("@days", rule.MinDaysBefore);
        command.Parameters.AddInt32("@refund", rule.RefundPercent);
        command.Parameters.AddUtcDateTime("@now", now);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<int> SetPolicyActiveAsync(UnitOfWork db, int id, bool original, bool active, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("UPDATE dbo.CancellationPolicies SET IsActive=@active, UpdatedAt=@now WHERE Id=@id AND HomestayId IS NULL AND IsActive=@old;");
        command.Parameters.AddInt32("@id", id);
        command.Parameters.AddBoolean("@old", original);
        command.Parameters.AddBoolean("@active", active);
        command.Parameters.AddUtcDateTime("@now", now);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SettingItem>> ListSettingsAsync(DbSession db, CancellationToken ct)
    {
        using var command = db.CreateCommand("SELECT SettingKey, SettingValue, Description FROM dbo.SystemSettings ORDER BY SettingKey;");
        return await db.QueryAsync(command, r => new SettingItem { Key = r.GetString("SettingKey"), Value = r.GetString("SettingValue"), Description = r.GetNullableString("Description") }, ct).ConfigureAwait(false);
    }

    public async Task<int> UpdateSettingAsync(UnitOfWork db, UpdateSettingRequest request, int userId, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            UPDATE dbo.SystemSettings SET SettingValue=@value, UpdatedByUserId=@user, UpdatedAt=@now
            WHERE SettingKey=@key AND CONVERT(varbinary(max), SettingValue)=CONVERT(varbinary(max), @old);
            """);
        command.Parameters.AddVarChar("@key", 100, request.Key);
        command.Parameters.AddNVarChar("@value", 500, request.Value);
        command.Parameters.AddNVarChar("@old", 500, request.OriginalValue);
        command.Parameters.AddInt32("@user", userId);
        command.Parameters.AddUtcDateTime("@now", now);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }
}
