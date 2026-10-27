using System.Globalization;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.BLL.Security;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Enums;
using Homestay.DTO.Owners;

namespace Homestay.BLL.Services;

public sealed partial class OwnerService
{
    private WriteOperationRunner Writes => new(factory, dataScope.CurrentSession, new WriteSessionValidator(new AuthRepository()));
    private void RequireWrite(Permission permission)
    {
        authorizer.Require(permission);
        if (authorizer.GetAccess(permission) == AccessLevel.ReadOnly) throw new ForbiddenException();
    }
    public Task<OwnerItem> CreateAsync(CreateOwnerRequest request, CancellationToken cancellationToken = default)
    {
        RequireWrite(Permission.OwnerEdit);
        var values = ListingWriteRules.Owner(request);
        return Writes.ExecuteAsync(async (db, ct) =>
        {
            var now = await new DbClock(factory).GetUtcNowAsync(db, ct).ConfigureAwait(false);
            var id = await repository.InsertAsync(db, values, dataScope.CurrentSession.RequireSession().EmployeeId, now, ct).ConfigureAwait(false);
            var next = await RawAsync(db, id, ct).ConfigureAwait(false);
            await AuditAsync(db, "Owner.Create", null, next, null, ct).ConfigureAwait(false);
            return Mask(next);
        }, cancellationToken);
    }
    public Task<OwnerItem> UpdateAsync(UpdateOwnerRequest request, CancellationToken cancellationToken = default)
    {
        RequireWrite(Permission.OwnerEdit); ArgumentNullException.ThrowIfNull(request);
        var values = ListingWriteRules.Owner(request.Values);
        if (request.ClearIdNumber && request.Values.IdNumber is not null)
            throw new BusinessException("Chọn xóa hoặc nhập CCCD mới. Vui lòng kiểm tra lại.");
        return ChangeAsync(request.OwnerId, request.RowVer, "Owner.Update", null, (old, _) =>
        {
            if (authorizer.GetAccess(Permission.OwnerEdit) == AccessLevel.DraftOnly && old.Status != OwnerStatus.Pending) throw new ForbiddenException();
            if (request.Values.BankName is not null && values.BankName != old.BankName
                || request.Values.BankAccountNo is not null && values.BankAccountNo != old.BankAccountNo
                || request.Values.BankAccountName is not null && values.BankAccountName != old.BankAccountName)
                throw new BusinessException("Vui lòng dùng ChangeBankAsync để đổi tài khoản ngân hàng sau khi Quản lý xác minh.");
            return old with { FullName = values.FullName, Phone = values.Phone, Email = values.Email, Address = values.Address,
                IdNumber = request.ClearIdNumber ? null : values.IdNumber ?? old.IdNumber, Note = values.Note };
        }, cancellationToken);
    }
    public Task<OwnerItem> ChangeBankAsync(ChangeOwnerBankRequest request, CancellationToken cancellationToken = default)
    {
        RequireWrite(Permission.OwnerBankAccountChange); ArgumentNullException.ThrowIfNull(request);
        var note = ListingWriteRules.Reason(request.VerificationNote);
        var bank = ListingWriteRules.Required(request.BankName, 100, "ngân hàng");
        var account = ListingWriteRules.Ascii(request.BankAccountNo, 30, "Số tài khoản") ?? throw new BusinessException("Vui lòng nhập số tài khoản ngân hàng.");
        var name = ListingWriteRules.Required(request.BankAccountName, 100, "tên tài khoản");
        return ChangeAsync(request.OwnerId, request.RowVer, "Owner.ChangeBank", note,
            (old, _) => old with { BankName = bank, BankAccountNo = account, BankAccountName = name }, cancellationToken);
    }
    public Task<OwnerItem> ChangeStatusAsync(OwnerStatusRequest request, CancellationToken cancellationToken = default)
    {
        RequireWrite(Permission.OwnerApprove); ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.Status)) throw new BusinessException("Trạng thái chủ không hợp lệ.");
        var note = request.Reason is null ? null : ListingWriteRules.Reason(request.Reason);
        return ChangeAsync(request.OwnerId, request.RowVer, "Owner.ChangeStatus", note, (old, now) =>
        {
            if ((old.Status, request.Status) is not ((OwnerStatus.Pending, OwnerStatus.Approved) or (OwnerStatus.Pending, OwnerStatus.Rejected)
                or (OwnerStatus.Approved, OwnerStatus.Suspended) or (OwnerStatus.Suspended, OwnerStatus.Approved) or (OwnerStatus.Rejected, OwnerStatus.Pending)))
                throw new BusinessException("Không thể chuyển trạng thái chủ này. Vui lòng tải lại hồ sơ.");
            if (old.Status != OwnerStatus.Pending || request.Status != OwnerStatus.Approved) _ = ListingWriteRules.Reason(note);
            if (request.Status != OwnerStatus.Approved) return old with { Status = request.Status };
            _ = ListingWriteRules.Owner(new() { FullName = old.FullName, Phone = old.Phone, IdNumber = old.IdNumber });
            if (string.IsNullOrWhiteSpace(old.BankName) || string.IsNullOrWhiteSpace(old.BankAccountNo) || string.IsNullOrWhiteSpace(old.BankAccountName))
                throw new BusinessException("Vui lòng bổ sung đủ ngân hàng, số tài khoản và tên tài khoản trước khi duyệt chủ.");
            return old with { Status = OwnerStatus.Approved, ApprovedByEmployeeId = dataScope.CurrentSession.RequireSession().EmployeeId, ApprovedAtUtc = now };
            // D11-2: no cascade to Homestays.
        }, cancellationToken);
    }
    private async Task<OwnerItem> RawAsync(DbSession db, int id, CancellationToken ct) =>
        await repository.GetAsync(db, id, "1=1", _ => { }, ct).ConfigureAwait(false) ?? throw new NotFoundException();
    private Task<OwnerItem> ChangeAsync(int id, byte[] version, string action, string? note, Func<OwnerItem, DateTime, OwnerItem> change, CancellationToken cancellationToken)
    {
        if (version is not { Length: 32 }) throw new BusinessException("Thiếu phiên bản dữ liệu. Vui lòng tải lại rồi thao tác lại.");
        var token = version.ToArray();
        return Writes.ExecuteAsync(async (db, ct) =>
        {
            var scope = dataScope.OwnerFilter();
            var old = await repository.GetForUpdateAsync(db, id, scope.Condition, scope.AddParametersTo, ct).ConfigureAwait(false) ?? throw new NotFoundException();
            if (!old.RowVer.SequenceEqual(token)) throw new ConcurrencyException();
            var now = await new DbClock(factory).GetUtcNowAsync(db, ct).ConfigureAwait(false);
            await repository.UpdateAsync(db, change(old, now), now, ct).ConfigureAwait(false);
            var next = await RawAsync(db, id, ct).ConfigureAwait(false);
            await AuditAsync(db, action, old, next, note, ct).ConfigureAwait(false);
            return Mask(next);
        }, cancellationToken);
    }
    private Task AuditAsync(UnitOfWork db, string action, OwnerItem? old, OwnerItem next, string? note, CancellationToken ct)
    {
        string? Redact(string? text)
        {
            foreach (var secret in new[] { old?.IdNumber, next.IdNumber, old?.BankAccountNo, next.BankAccountNo, old?.BankAccountName, next.BankAccountName })
                if (!string.IsNullOrEmpty(secret)) text = text?.Replace(secret, "[Đã ẩn]", StringComparison.Ordinal);
            return text;
        }
        object? Safe(OwnerItem? v) => v is null ? null : new { FullName = Redact(v.FullName), v.Phone, Email = Redact(v.Email), Address = Redact(v.Address),
            IdNumber = StaffInputRules.MaskIdentity(v.IdNumber), BankName = Redact(v.BankName), BankAccountNo = ReceiptRules.MaskAccount(v.BankAccountNo),
            BankAccountName = v.BankAccountName is null ? null : "[Đã ẩn]", Status = v.Status.ToString(), v.ApprovedByEmployeeId, v.ApprovedAtUtc, v.CreatedByEmployeeId, Note = Redact(v.Note) };
        return new AuditWriter().WriteAsync(db, new(dataScope.CurrentSession.RequireSession().UserId, action, "Owners", next.Id.ToString(CultureInfo.InvariantCulture), Safe(old), Safe(next), Redact(note), Environment.MachineName), ct);
    }
}
