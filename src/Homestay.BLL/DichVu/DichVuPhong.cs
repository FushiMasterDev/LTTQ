using Homestay.BLL.Infrastructure;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Availability;
using Homestay.DTO.Rooms;
namespace Homestay.BLL.Services;

/// <summary>Dịch vụ phòng (phần đọc): loại phòng, phòng, bảng giá, lịch khóa phòng và lịch phòng của homestay.</summary>
public sealed partial class RoomService(Authorizer authorizer, DataScope dataScope, ConnectionFactory factory, RoomRepository repository, DbClock clock,
    AvailabilityService availability)
{
    private async Task EnsureAsync(DbSession db, int home, CancellationToken ct)
    {
        var scope = dataScope.HomestayFilter();
        _ = await new HomestayRepository().GetAsync(db, home, scope.Condition, scope.AddParametersTo, ct).ConfigureAwait(false) ?? throw new NotFoundException();
    }
    public Task<IReadOnlyList<RoomTypeItem>> ListTypesAsync(int homestayId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.HomestayView);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        { await EnsureAsync(db, homestayId, token).ConfigureAwait(false); return await repository.ListTypesAsync(db, homestayId, token).ConfigureAwait(false); }, ct), cancellationToken);
    }
    public Task<IReadOnlyList<RoomItem>> ListRoomsAsync(int homestayId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.HomestayView);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        { await EnsureAsync(db, homestayId, token).ConfigureAwait(false); return await repository.ListRoomsAsync(db, homestayId, token).ConfigureAwait(false); }, ct), cancellationToken);
    }
    public Task<RoomPriceList> ListPricesAsync(int homestayId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.HomestayView);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        { await EnsureAsync(db, homestayId, token).ConfigureAwait(false); return await repository.ListPricesAsync(db, homestayId, await clock.GetUtcNowAsync(db, token).ConfigureAwait(false), token).ConfigureAwait(false); }, ct), cancellationToken);
    }
    public Task<IReadOnlyList<RoomBlockItem>> ListBlocksAsync(RoomBlockSearchRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.RoomCalendarView); ArgumentNullException.ThrowIfNull(request);
        if (request.HomestayId <= 0 || request.ToDate <= request.FromDate) throw new BusinessException("Khoảng ngày khóa phòng không hợp lệ.");
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        { await EnsureAsync(db, request.HomestayId, token).ConfigureAwait(false); return await repository.ListBlocksAsync(db, request, token).ConfigureAwait(false); }, ct), cancellationToken);
    }
    // F06 occupancy, expired holds and change requests use the existing calendar without a second predicate.
    public Task<RoomCalendar> GetCalendarAsync(RoomCalendarRequest request, CancellationToken cancellationToken = default) => availability.GetCalendarAsync(request, cancellationToken);
}
