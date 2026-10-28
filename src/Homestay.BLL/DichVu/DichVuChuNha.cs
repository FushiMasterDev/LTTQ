using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Common;
using Homestay.DTO.Owners;
namespace Homestay.BLL.Services;

/// <summary>Dịch vụ chủ nhà (phần đọc): xem danh sách, chi tiết, tra cứu chủ nhà theo quyền và phạm vi khu vực, che bớt thông tin nhạy cảm.</summary>
public sealed partial class OwnerService(Authorizer authorizer, DataScope dataScope, ConnectionFactory factory, OwnerRepository repository)
{
    public Task<PagedResult<OwnerItem>> ListAsync(OwnerSearchRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.OwnerEdit); ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.Page(request.Page, "Id", "FullName", "Phone", "Status");
        if (request.Status is { } status && !Enum.IsDefined(status)) throw new BusinessException("Trạng thái chủ không hợp lệ.");
        var scope = dataScope.OwnerFilter();
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        {
            var page = await repository.ListAsync(db, request, scope.Condition, scope.AddParametersTo, token).ConfigureAwait(false);
            return page with { Items = page.Items.Select(Mask).ToArray() };
        }, ct), cancellationToken);
    }
    public Task<OwnerItem> GetAsync(int ownerId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.OwnerEdit); var scope = dataScope.OwnerFilter();
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
            Mask(await repository.GetAsync(db, ownerId, scope.Condition, scope.AddParametersTo, token).ConfigureAwait(false) ?? throw new NotFoundException()), ct), cancellationToken);
    }
    public Task<IReadOnlyList<Lookup>> LookupAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.OwnerEdit); var scope = dataScope.OwnerFilter();
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync((db, token) => repository.LookupAsync(db, scope.Condition, scope.AddParametersTo, token), ct), cancellationToken);
    }
    internal OwnerItem Mask(OwnerItem item) => item with
    {
        IdNumber = authorizer.Can(Permission.OwnerIdentityView) ? item.IdNumber : StaffInputRules.MaskIdentity(item.IdNumber),
        BankAccountNo = authorizer.Can(Permission.OwnerBankAccountChange) ? item.BankAccountNo : ReceiptRules.MaskAccount(item.BankAccountNo),
        IsSensitiveMasked = !authorizer.Can(Permission.OwnerIdentityView) || !authorizer.Can(Permission.OwnerBankAccountChange)
    };
}
