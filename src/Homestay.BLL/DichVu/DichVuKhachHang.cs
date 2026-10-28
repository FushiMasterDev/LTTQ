using static Homestay.BLL.Rules.CustomerInputRules;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Common;
using Homestay.DTO.Customers;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Services;

public sealed class CustomerService(Authorizer authorizer, ICurrentSession currentSession, ConnectionFactory factory, WriteOperationRunner writes,
    CustomerRepository repository, DbClock clock, AuditWriter audit, string machineName)
{
    public Task<PagedResult<CustomerItem>> SearchAsync(CustomerSearchRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.CustomerManage);
        ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.Page(request.Page, "Id", "FullName", "Phone", "Email");
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        {
            var result = await repository.SearchAsync(db, request, token).ConfigureAwait(false);
            return result with { Items = result.Items.Select(Mask).ToArray() };
        }, ct), cancellationToken);
    }
    public Task<CustomerItem?> FindByPhoneAsync(string phone, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.CustomerManage);
        var normalized = PhoneNumber.Normalize(phone);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        {
            var item = await repository.FindByPhoneAsync(db, normalized, token).ConfigureAwait(false);
            return item is null ? null : Mask(item);
        }, ct), cancellationToken);
    }
    public Task<CustomerDetail> GetAsync(int customerId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.CustomerManage);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync((db, token) => DetailAsync(db, customerId, token), ct), cancellationToken);
    }
    public Task<CustomerDetail> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        RequireWrite();
        ArgumentNullException.ThrowIfNull(request);
        var normalized = Normalize(request);
        return writes.ExecuteAsync(async (db, ct) =>
        {
            // Cùng khóa cho tạo nhanh/tạo khách/đổi SĐT. Hai máy tạo cùng SĐT nhận cùng bản ghi, không ghi đè hồ sơ cũ.
            await db.AcquireAppLockAsync("CustomerPhone:" + normalized.Phone, 10000, ct).ConfigureAwait(false);
            var existing = await repository.FindByPhoneAsync(db, normalized.Phone, ct).ConfigureAwait(false);
            if (existing is not null) return await DetailAsync(db, existing.Id, ct).ConfigureAwait(false);
            var id = await repository.CreateAsync(db, normalized, currentSession.RequireSession().EmployeeId, await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            var customer = await repository.GetAsync(db, id, false, ct).ConfigureAwait(false) ?? throw new NotFoundException();
            await AuditAsync(db, "Customer.Create", id, null, customer, ct).ConfigureAwait(false);
            return await DetailAsync(db, id, ct).ConfigureAwait(false);
        }, cancellationToken);
    }
    public Task<CustomerDetail> UpdateAsync(UpdateCustomerRequest request, CancellationToken cancellationToken = default)
    {
        RequireWrite();
        ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.RowVer(request.RowVer);
        if (request.ClearIdNumber && request.IdNumber is not null) throw new BusinessException("Chọn xóa hoặc nhập CCCD mới. Vui lòng kiểm tra lại.");
        var normalized = request with { FullName = Name(request.FullName), Email = Email(request.Email), IdNumber = Identity(request.IdNumber), Note = Note(request.Note) };
        return writes.ExecuteAsync(async (db, ct) =>
        {
            var old = await repository.GetAsync(db, normalized.CustomerId, true, ct).ConfigureAwait(false) ?? throw new NotFoundException();
            var identity = normalized.ClearIdNumber ? null : normalized.IdNumber ?? old.IdNumber;
            if (await repository.UpdateAsync(db, normalized, identity, await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false), ct).ConfigureAwait(false) == 0) throw new ConcurrencyException();
            var next = await repository.GetAsync(db, old.Id, false, ct).ConfigureAwait(false) ?? throw new NotFoundException();
            await AuditAsync(db, "Customer.Update", old.Id, old, next, ct).ConfigureAwait(false);
            return await DetailAsync(db, old.Id, ct).ConfigureAwait(false);
        }, cancellationToken);
    }
    public Task<CustomerDetail> ChangePhoneAsync(ChangeCustomerPhoneRequest request, CancellationToken cancellationToken = default)
    {
        RequireWrite();
        ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.RowVer(request.RowVer);
        var normalized = request with { Phone = PhoneNumber.Normalize(request.Phone) };
        return writes.ExecuteAsync(async (db, ct) =>
        {
            await db.AcquireAppLockAsync("CustomerPhone:" + normalized.Phone, 10000, ct).ConfigureAwait(false);
            var old = await repository.GetAsync(db, normalized.CustomerId, true, ct).ConfigureAwait(false) ?? throw new NotFoundException();
            if (old.HasBookings) authorizer.Require(Permission.CustomerPhoneChange);
            if (await repository.ChangePhoneAsync(db, normalized, await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false), ct).ConfigureAwait(false) == 0) throw new ConcurrencyException();
            var next = await repository.GetAsync(db, old.Id, false, ct).ConfigureAwait(false) ?? throw new NotFoundException();
            await AuditAsync(db, "Customer.ChangePhone", old.Id, old, next, ct).ConfigureAwait(false);
            return await DetailAsync(db, old.Id, ct).ConfigureAwait(false);
        }, cancellationToken);
    }
    private void RequireWrite()
    {
        authorizer.Require(Permission.CustomerManage);
        if (authorizer.GetAccess(Permission.CustomerManage) == AccessLevel.ReadOnly) throw new ForbiddenException();
    }
    private CustomerItem Mask(CustomerItem item) => authorizer.Can(Permission.CustomerIdentityView) ? item : item with { IdNumber = StaffInputRules.MaskIdentity(item.IdNumber), IsIdentityMasked = true };
    private async Task<CustomerDetail> DetailAsync(DbSession db, int id, CancellationToken ct) => new(
        Mask(await repository.GetAsync(db, id, false, ct).ConfigureAwait(false) ?? throw new NotFoundException()),
        await repository.RecentBookingsAsync(db, id, ct).ConfigureAwait(false), []);
    private Task AuditAsync(UnitOfWork db, string action, int id, CustomerItem? old, CustomerItem next, CancellationToken ct)
    {
        // Tuyệt đối không đưa IdNumber gốc vào JSON, kể cả người thao tác là Quản lý.
        static object? Safe(CustomerItem? c) => c is null ? null : new { c.FullName, c.Phone, c.Email, IdNumber = StaffInputRules.MaskIdentity(c.IdNumber), c.Note };
        return audit.WriteAsync(db, new(currentSession.RequireSession().UserId, action, "Customers", id.ToString(System.Globalization.CultureInfo.InvariantCulture), Safe(old), Safe(next), null, machineName), ct);
    }
}
