using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Common;
using Homestay.DTO.Contracts;
namespace Homestay.BLL.Services;

public sealed partial class ContractService(Authorizer authorizer, DataScope dataScope, ConnectionFactory factory, ContractRepository repository)
{
    public Task<PagedResult<ContractItem>> ListAsync(ContractSearchRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.ContractManage); ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.Page(request.Page, "Id", "ContractNo", "HomestayName", "StartDate", "EndDate", "Status");
        if (request.HomestayId is <= 0 || request.Status is { } status && !Enum.IsDefined(status)) throw new BusinessException("Bộ lọc hợp đồng không hợp lệ.");
        var scope = dataScope.HomestayFilter(financialRead: true);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync((db, token) => repository.ListAsync(db, request, scope.Condition, scope.AddParametersTo, token), ct), cancellationToken);
    }
    public Task<ContractItem> GetAsync(int contractId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.ContractManage); var scope = dataScope.HomestayFilter(financialRead: true);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
            await repository.GetAsync(db, contractId, scope.Condition, scope.AddParametersTo, token).ConfigureAwait(false) ?? throw new NotFoundException(), ct), cancellationToken);
    }
    public Task<IReadOnlyList<Lookup>> ListHomestaysAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.ContractManage); var scope = dataScope.HomestayFilter(financialRead: true);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync((db, token) => new HomestayRepository().LookupAsync(db, scope.Condition, scope.AddParametersTo, token), ct), cancellationToken);
    }
}
