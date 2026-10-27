using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Contracts;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Services;

public sealed partial class ContractService
{
    private ListingChildWriter Writer => new(authorizer, dataScope, factory, new DbClock(factory));
    private async Task<ContractItem> ReadAsync(DbSession db, int home, int id, CancellationToken ct)
    {
        var item = await repository.GetAsync(db, id, "1=1", _ => { }, ct).ConfigureAwait(false) ?? throw new NotFoundException();
        return item.HomestayId == home ? item : throw new NotFoundException();
    }
    private async Task ValidateAsync(UnitOfWork db, decimal rate, DateOnly start, DateOnly end, CancellationToken ct)
    {
        var settings = new SettingsProvider(factory);
        ContractRules.Validate(rate, start, end, await settings.GetContractMinCommissionAsync(ct, db).ConfigureAwait(false),
            await settings.GetContractMaxCommissionAsync(ct, db).ConfigureAwait(false));
    }
    public Task<ContractItem> CreateAsync(CreateContractRequest request, CancellationToken cancellationToken = default)
    {
        Writer.Require(Permission.ContractManage); ArgumentNullException.ThrowIfNull(request);
        var number = ListingWriteRules.Ascii(request.ContractNo, 30, "Số hợp đồng") ?? throw new BusinessException("Vui lòng nhập số hợp đồng từ 1 đến 30 ký tự ASCII.");
        var v = request with { ContractNo = number };
        return Writer.ExecuteAsync((_, _) => Task.FromResult(v.HomestayId), v.RowVer, Permission.ContractManage, "Contract.Create", async (db, home, now, ct) =>
        {
            await ValidateAsync(db, v.CommissionRate, v.StartDate, v.EndDate, ct).ConfigureAwait(false);
            ContractRules.RequireCurrentDates(v.StartDate, v.EndDate, DateOnly.FromDateTime(now.AddHours(7)));
            if (await new HomestayRepository().HasActiveContractAsync(db, home.Id, ct).ConfigureAwait(false))
                throw new BusinessException(ConstraintMessages.GetMessage("UX_Contracts_OneActive"),
                    new DbConstraintViolationException("UX_Contracts_OneActive", DbConstraintKind.Unique, 2601));
            var id = await repository.InsertAsync(db, v, dataScope.CurrentSession.RequireSession().EmployeeId, now, ct).ConfigureAwait(false);
            return (id, null, (object?)Safe(await ReadAsync(db, home.Id, id, ct).ConfigureAwait(false)));
        }, (db, home, id, _, ct) => ReadAsync(db, home, id, ct), cancellationToken);
    }
    public Task<ContractItem> ActivateAsync(ActivateContractRequest request, CancellationToken cancellationToken = default)
    {
        Writer.Require(Permission.ContractManage); ArgumentNullException.ThrowIfNull(request);
        return ChangeAsync(request.ContractId, request.RowVer, "Contract.Activate", null, null, cancellationToken);
    }
    public Task<ContractItem> ExtendAsync(ExtendContractRequest request, CancellationToken cancellationToken = default)
    {
        Writer.Require(Permission.ContractManage); ArgumentNullException.ThrowIfNull(request);
        return ChangeAsync(request.ContractId, request.RowVer, "Contract.Extend", request.EndDate, null, cancellationToken);
    }
    public Task<ContractItem> TerminateAsync(TerminateContractRequest request, CancellationToken cancellationToken = default)
    {
        Writer.Require(Permission.ContractManage); ArgumentNullException.ThrowIfNull(request);
        return ChangeAsync(request.ContractId, request.RowVer, "Contract.Terminate", null, ListingWriteRules.Reason(request.Reason), cancellationToken);
    }
    private Task<ContractItem> ChangeAsync(int id, byte[] version, string action, DateOnly? end, string? reason, CancellationToken cancellationToken) =>
        Writer.ExecuteAsync(async (db, ct) => await repository.LocateHomeAsync(db, id, ct).ConfigureAwait(false) ?? throw new NotFoundException(),
            version, Permission.ContractManage, action, async (db, home, _, ct) =>
            {
                await ListingChildWriter.LockTypesAsync(db, home.Id, ct).ConfigureAwait(false);
                var old = await ReadAsync(db, home.Id, id, ct).ConfigureAwait(false);
                if (old.Status != ContractStatus.Active) throw new BusinessException("Chỉ được thao tác trên hợp đồng Active. Vui lòng tải lại hợp đồng.");
                var now = await new DbClock(factory).GetUtcNowAsync(db, ct).ConfigureAwait(false);
                if (action == "Contract.Activate")
                {
                    await ValidateAsync(db, old.CommissionRate, old.StartDate, old.EndDate, ct).ConfigureAwait(false);
                    ContractRules.RequireCurrentDates(old.StartDate, old.EndDate, DateOnly.FromDateTime(now.AddHours(7)));
                }
                else if (end is { } newEnd)
                {
                    if (newEnd <= old.EndDate) throw new BusinessException("Ngày kết thúc gia hạn phải sau ngày kết thúc hiện tại. Vui lòng chọn lại.");
                    // Extension keeps the contracted rate, even if settings changed after signing.
                    if (newEnd <= old.StartDate) throw new BusinessException("Ngày kết thúc hợp đồng phải sau ngày bắt đầu.");
                }
                else if (await repository.HasOccupyingBookingsAsync(db, home.Id, now, ct).ConfigureAwait(false))
                    throw new BusinessException("Homestay còn đơn đang chiếm phòng. Vui lòng xử lý các đơn trước khi chấm dứt hợp đồng.");
                if (action != "Contract.Activate" && await repository.ChangeAsync(db, id, end ?? old.EndDate, action == "Contract.Terminate", reason, now, ct).ConfigureAwait(false) == 0)
                    throw new ConcurrencyException();
                return (id, (object?)Safe(old), (object?)Safe(await ReadAsync(db, home.Id, id, ct).ConfigureAwait(false)));
            }, (db, home, contract, _, ct) => ReadAsync(db, home, contract, ct), cancellationToken, reason);
    private static object Safe(ContractItem c) => new { c.Id, c.HomestayId, c.ContractNo, c.StartDate, c.EndDate, c.CommissionRate,
        Status = c.Status.ToString(), c.SignedByEmployeeId, c.TerminatedAtUtc, c.TerminateReason };
}
