using System.Globalization;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Catalog;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Services;

/// <summary>Dịch vụ danh mục dùng chung: khu vực, tiện nghi, loại phòng, ngày lễ, chính sách hủy và tham số cấu hình hệ thống.</summary>
public sealed class CatalogService(Authorizer authorizer, ICurrentSession currentSession, ConnectionFactory factory, WriteOperationRunner writes,
    CatalogRepository repository, DbClock clock, SettingsProvider settings, AuditWriter audit, string machineName)
{
    public Task<IReadOnlyList<AreaItem>> ListAreasAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.CatalogManage);
        return ReadAsync(repository.ListAreasAsync, cancellationToken);
    }

    public Task<IReadOnlyList<CatalogItem>> ListItemsAsync(CatalogKind kind, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.CatalogManage);
        RequireSimpleKind(kind);
        return ReadAsync((db, ct) => repository.ListItemsAsync(db, kind, ct), cancellationToken);
    }

    public Task<IReadOnlyList<Lookup>> LookupAsync(CatalogKind kind, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.CatalogManage);
        RequireKind(kind);
        return ReadAsync((db, ct) => repository.LookupAsync(db, kind, ct), cancellationToken);
    }

    public Task<int> CreateAreaAsync(AreaRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.CatalogManage);
        RequireAll(Permission.CatalogManage);
        var normalized = NormalizeArea(request);
        return writes.ExecuteAsync(async (db, ct) =>
        {
            var id = await repository.CreateAreaAsync(db, normalized, await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            await AuditAsync(db, "Catalog.Create", "Areas", id.ToString(), null, normalized, ct).ConfigureAwait(false);
            return id;
        }, cancellationToken);
    }

    public Task UpdateAreaAsync(AreaItem original, AreaRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.CatalogManage);
        RequireAll(Permission.CatalogManage);
        ArgumentNullException.ThrowIfNull(original);
        var normalized = NormalizeArea(request);
        return writes.ExecuteAsync(async (db, ct) =>
        {
            EnsureChanged(await repository.UpdateAreaAsync(db, original, normalized, await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false), ct).ConfigureAwait(false));
            await AuditAsync(db, "Catalog.Update", "Areas", original.Id.ToString(), original, normalized, ct).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<int> CreateItemAsync(CatalogKind kind, CatalogNameRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.CatalogManage);
        RequireAll(Permission.CatalogManage);
        RequireSimpleKind(kind);
        ArgumentNullException.ThrowIfNull(request);
        var name = Name(request.Name);
        return writes.ExecuteAsync(async (db, ct) =>
        {
            var id = await repository.CreateItemAsync(db, kind, name, await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            await AuditAsync(db, "Catalog.Create", CatalogRepository.TableName(kind), id.ToString(), null, new { Name = name }, ct).ConfigureAwait(false);
            return id;
        }, cancellationToken);
    }

    public Task UpdateItemAsync(CatalogKind kind, CatalogItem original, CatalogNameRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.CatalogManage);
        RequireAll(Permission.CatalogManage);
        RequireSimpleKind(kind);
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(request);
        var name = Name(request.Name);
        return writes.ExecuteAsync(async (db, ct) =>
        {
            EnsureChanged(await repository.UpdateItemAsync(db, kind, original, name, await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false), ct).ConfigureAwait(false));
            await AuditAsync(db, "Catalog.Update", CatalogRepository.TableName(kind), original.Id.ToString(), original, new { Name = name }, ct).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task SetActiveAsync(CatalogKind kind, int id, bool originalIsActive, bool isActive, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.CatalogManage);
        RequireAll(Permission.CatalogManage);
        RequireKind(kind);
        if (originalIsActive == isActive) throw new BusinessException("Trạng thái chưa thay đổi. Vui lòng chọn trạng thái mới.");
        return writes.ExecuteAsync(async (db, ct) =>
        {
            EnsureChanged(await repository.SetActiveAsync(db, kind, id, originalIsActive, isActive, await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false), ct).ConfigureAwait(false));
            await AuditAsync(db, "Catalog.SetActive", CatalogRepository.TableName(kind), id.ToString(), new { IsActive = originalIsActive }, new { IsActive = isActive }, ct).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<HolidayItem>> ListHolidaysAsync(int year, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.CatalogManage);
        if (year is < 1 or > 9999) throw new BusinessException("Năm không hợp lệ. Vui lòng nhập năm từ 1 đến 9999.");
        return ReadAsync((db, ct) => repository.ListHolidaysAsync(db, year, ct), cancellationToken);
    }

    public Task CreateHolidayAsync(HolidayItem request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.CatalogManage);
        RequireAll(Permission.CatalogManage);
        ArgumentNullException.ThrowIfNull(request);
        var item = request with { Name = Name(request.Name) };
        return writes.ExecuteAsync(async (db, ct) =>
        {
            await repository.CreateHolidayAsync(db, item, await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            await AuditAsync(db, "Holiday.Create", "Holidays", item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), null, item, ct).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task RenameHolidayAsync(RenameHolidayRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.CatalogManage);
        RequireAll(Permission.CatalogManage);
        ArgumentNullException.ThrowIfNull(request);
        var item = request with { Name = Name(request.Name) };
        return writes.ExecuteAsync(async (db, ct) =>
        {
            EnsureChanged(await repository.RenameHolidayAsync(db, item, await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false), ct).ConfigureAwait(false));
            await AuditAsync(db, "Holiday.Rename", "Holidays", item.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), new { Name = item.OriginalName }, new { item.Name }, ct).ConfigureAwait(false);
        }, cancellationToken);
    }

    public Task<IReadOnlyList<CancellationPolicyItem>> ListPoliciesAsync(CancellationScope? scope = null, bool includeInactive = true, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.CancellationPolicyManage);
        if (scope is { } value && !Enum.IsDefined(value)) throw new BusinessException("Phạm vi không hợp lệ. Vui lòng chọn All hoặc Holiday.");
        return ReadAsync((db, ct) => repository.ListPoliciesAsync(db, scope, includeInactive, ct), cancellationToken);
    }

    public Task<int> CreatePolicyAsync(CreateCancellationPolicyRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.CancellationPolicyManage);
        RequireAll(Permission.CancellationPolicyManage);
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Scope)) throw new BusinessException("Phạm vi không hợp lệ. Vui lòng chọn All hoặc Holiday.");
        var normalized = request with { Name = Name(request.Name), Rules = request.Rules?.ToArray() ?? [] };
        CancellationPolicyRules.Validate(normalized.Rules);
        return writes.ExecuteAsync(async (db, ct) =>
        {
            await LockPolicyScopeAsync(db, normalized.Scope, ct).ConfigureAwait(false);
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            if (normalized.Activate) await DisableCurrentPolicyAsync(db, normalized.Scope, now, ct).ConfigureAwait(false);
            var id = await repository.CreatePolicyAsync(db, normalized, now, ct).ConfigureAwait(false);
            foreach (var rule in normalized.Rules) await repository.CreateRuleAsync(db, id, rule, now, ct).ConfigureAwait(false);
            await AuditAsync(db, "CancellationPolicy.Create", "CancellationPolicies", id.ToString(), null, normalized, ct).ConfigureAwait(false);
            return id;
        }, cancellationToken);
    }

    public Task SetPolicyActiveAsync(int policyId, bool originalIsActive, bool isActive, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.CancellationPolicyManage);
        RequireAll(Permission.CancellationPolicyManage);
        if (originalIsActive == isActive) throw new BusinessException("Trạng thái chưa thay đổi. Vui lòng chọn trạng thái mới.");
        return writes.ExecuteAsync(async (db, ct) =>
        {
            var item = (await repository.ListPoliciesAsync(db, null, true, ct).ConfigureAwait(false)).SingleOrDefault(p => p.Id == policyId) ?? throw new NotFoundException();
            await LockPolicyScopeAsync(db, item.Scope, ct).ConfigureAwait(false);
            // Đọc lại sau khóa để không dùng trạng thái lấy trước một transaction khác.
            item = (await repository.ListPoliciesAsync(db, item.Scope, true, ct).ConfigureAwait(false)).Single(p => p.Id == policyId);
            if (item.IsActive != originalIsActive) throw new ConcurrencyException();
            if (!isActive && item.Scope == CancellationScope.All)
                throw new BusinessException("Không được tắt chính sách All đang dùng duy nhất. Vui lòng bật phiên bản All thay thế.");
            if (isActive) CancellationPolicyRules.Validate(item.Rules);
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            if (isActive) await DisableCurrentPolicyAsync(db, item.Scope, now, ct).ConfigureAwait(false);
            EnsureChanged(await repository.SetPolicyActiveAsync(db, item.Id, originalIsActive, isActive, now, ct).ConfigureAwait(false));
            await AuditAsync(db, "CancellationPolicy.SetActive", "CancellationPolicies", item.Id.ToString(), new { IsActive = originalIsActive }, new { IsActive = isActive }, ct).ConfigureAwait(false);
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<SettingItem>> ListSettingsAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.SettingsManage);
        var items = await ReadAsync(repository.ListSettingsAsync, cancellationToken).ConfigureAwait(false);
        return items.Select(item =>
        {
            var definition = SettingsProvider.GetDefinition(item.Key);
            return item with { ValueType = definition.ValueType, Minimum = definition.Minimum, Maximum = definition.Maximum, Format = definition.Format };
        }).ToArray();
    }

    public Task UpdateSettingAsync(UpdateSettingRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.SettingsManage);
        RequireAll(Permission.SettingsManage);
        ArgumentNullException.ThrowIfNull(request);
        SettingsProvider.Validate(request.Key, request.Value);
        if (request.OriginalValue is null || request.OriginalValue.Length > 500) throw new BusinessException("Thiếu giá trị gốc hợp lệ. Vui lòng tải lại tham số.");
        return writes.ExecuteAsync(async (db, ct) =>
        {
            await db.AcquireAppLockAsync("SystemSettings", 10000, ct).ConfigureAwait(false);
            var values = new Dictionary<string, string>(await settings.GetAllAsync(ct, db).ConfigureAwait(false), StringComparer.Ordinal)
                { [request.Key] = request.Value };
            ValidateSettingPairs(values);
            EnsureChanged(await repository.UpdateSettingAsync(db, request, currentSession.RequireSession().UserId,
                await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false), ct).ConfigureAwait(false));
            await AuditAsync(db, "Setting.Update", "SystemSettings", request.Key, new { Value = request.OriginalValue }, new { request.Value }, ct).ConfigureAwait(false);
        }, cancellationToken);
    }

    private Task<T> ReadAsync<T>(Func<DbSession, CancellationToken, Task<T>> operation, CancellationToken ct) =>
        BusinessErrorTranslator.ExecuteAsync(token => factory.ReadAsync(operation, token), ct);
    private Task AuditAsync(UnitOfWork db, string action, string table, string id, object? oldValue, object? newValue, CancellationToken ct) =>
        audit.WriteAsync(db, new(currentSession.RequireSession().UserId, action, table, id, oldValue, newValue, null, machineName), ct);
    private void RequireAll(Permission permission)
    {
        if (authorizer.GetAccess(permission) != AccessLevel.All) throw new ForbiddenException();
    }
    private static void RequireKind(CatalogKind kind)
    {
        if (!Enum.IsDefined(kind)) throw new BusinessException("Danh mục không hợp lệ. Vui lòng chọn lại danh mục.");
    }
    private static void RequireSimpleKind(CatalogKind kind)
    {
        RequireKind(kind);
        if (kind == CatalogKind.Area) throw new BusinessException("Khu vực cần tỉnh và nhóm khu vực. Vui lòng dùng chức năng khu vực.");
    }
    private static string Name(string? value)
    {
        var name = value?.Trim() ?? "";
        if (name.Length is < 1 or > 100) throw new BusinessException("Tên phải có từ 1 đến 100 ký tự. Vui lòng nhập lại.");
        return name;
    }
    private static AreaRequest NormalizeArea(AreaRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.AreaGroup)) throw new BusinessException("Nhóm khu vực không hợp lệ. Vui lòng chọn lại.");
        return request with { Name = Name(request.Name), Province = Name(request.Province) };
    }
    private static void EnsureChanged(int count)
    {
        if (count == 0) throw new ConcurrencyException();
    }
    private static Task LockPolicyScopeAsync(UnitOfWork db, CancellationScope scope, CancellationToken ct) =>
        db.AcquireAppLockAsync("CancellationPolicy:General:" + scope, 10000, ct);
    private async Task DisableCurrentPolicyAsync(UnitOfWork db, CancellationScope scope, DateTime now, CancellationToken ct)
    {
        foreach (var item in await repository.ListPoliciesAsync(db, scope, false, ct).ConfigureAwait(false))
        {
            EnsureChanged(await repository.SetPolicyActiveAsync(db, item.Id, true, false, now, ct).ConfigureAwait(false));
            await AuditAsync(db, "CancellationPolicy.SetActive", "CancellationPolicies", item.Id.ToString(), new { IsActive = true }, new { IsActive = false }, ct).ConfigureAwait(false);
        }
    }
    private static void ValidateSettingPairs(IReadOnlyDictionary<string, string> values)
    {
        foreach (var key in new[] { "Contract.MinCommission", "Contract.MaxCommission", "Voucher.MinAmount", "Voucher.MaxAmount", "WorkingHours.Start", "WorkingHours.End" })
            if (values.TryGetValue(key, out var currentValue)) SettingsProvider.Validate(key, currentValue);
        foreach (var (minKey, maxKey) in new[] { ("Contract.MinCommission", "Contract.MaxCommission"), ("Voucher.MinAmount", "Voucher.MaxAmount") })
            if (values.TryGetValue(minKey, out var min) && values.TryGetValue(maxKey, out var max) &&
                decimal.Parse(min, CultureInfo.InvariantCulture) > decimal.Parse(max, CultureInfo.InvariantCulture))
                throw new BusinessException($"{minKey} không được lớn hơn {maxKey}. Vui lòng sửa giá trị tương ứng.");
        if (values.TryGetValue("WorkingHours.Start", out var start) && values.TryGetValue("WorkingHours.End", out var end) &&
            TimeOnly.ParseExact(start, "HH:mm", CultureInfo.InvariantCulture) >= TimeOnly.ParseExact(end, "HH:mm", CultureInfo.InvariantCulture))
            throw new BusinessException("Giờ bắt đầu phải trước giờ kết thúc. Vui lòng sửa giờ làm việc.");
    }
}
