using Homestay.BLL.Infrastructure;
using Homestay.DTO.Common;
namespace Homestay.BLL.Services;

public sealed partial class BookingService
{
    private ScopeSql FilterHomestayScope()
    {
        var homes = dataScope.HomestayFilter(financialRead: true); var bookings = dataScope.BookingFilter();
        return new($"({homes.Condition}) OR EXISTS(SELECT 1 FROM dbo.Bookings b WHERE b.HomestayId=h.Id AND ({bookings.Condition}))", homes.Parameters.Concat(bookings.Parameters).ToArray());
    }
    public Task<IReadOnlyList<Lookup>> ListFilterHomestaysAsync(int? areaId = null, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingView);
        if (areaId is <= 0) throw new BusinessException("Khu vực không hợp lệ.");
        var scope = FilterHomestayScope();
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync((db, token) => repository.FilterHomestaysAsync(db, areaId, scope.Condition, scope.AddParametersTo, token), ct), cancellationToken);
    }
    public Task<IReadOnlyList<Lookup>> ListFilterAreasAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingView); var scope = FilterHomestayScope();
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync((db, token) => repository.FilterAreasAsync(db, scope.Condition, scope.AddParametersTo, token), ct), cancellationToken);
    }
    public Task<IReadOnlyList<Lookup>> ListFilterAssigneesAsync(int? areaId = null, int? homestayId = null, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingView);
        if (areaId is <= 0 || homestayId is <= 0) throw new BusinessException("Bộ lọc người phụ trách không hợp lệ.");
        var scope = dataScope.BookingFilter();
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync((db, token) => repository.FilterAssigneesAsync(db, areaId, homestayId, scope.Condition, scope.AddParametersTo, token), ct), cancellationToken);
    }
}
