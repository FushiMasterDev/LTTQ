using Homestay.BLL.Infrastructure;
using Homestay.BLL.Jobs;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Enums;
using Homestay.DTO.Homestays;
using Homestay.DTO.Rooms;

namespace Homestay.BLL.Services;

public sealed partial class RoomService
{
    private ListingChildWriter Writer => new(authorizer, dataScope, factory, clock);
    private Task<int> HomeAsync(UnitOfWork db, int id, string kind, CancellationToken ct) => LocateHomeAsync(db, id, kind, ct);
    private async Task<int> LocateHomeAsync(UnitOfWork db, int id, string kind, CancellationToken ct) =>
        (await repository.LocateAsync(db, id, kind, ct).ConfigureAwait(false) ?? throw new NotFoundException()).HomestayId;
    private async Task<RoomTypeItem> TypeAsync(DbSession db, int home, int id, CancellationToken ct) =>
        (await repository.ListTypesAsync(db, home, ct).ConfigureAwait(false)).SingleOrDefault(t => t.Id == id) ?? throw new NotFoundException();
    private async Task<RoomItem> RoomAsync(DbSession db, int home, int id, CancellationToken ct) =>
        (await repository.ListRoomsAsync(db, home, ct).ConfigureAwait(false)).SingleOrDefault(r => r.Id == id) ?? throw new NotFoundException();
    private async Task RequireCategoryAsync(UnitOfWork db, int category, CancellationToken ct)
    {
        if (!await repository.CategoryActiveAsync(db, category, ct).ConfigureAwait(false))
            throw new BusinessException("Danh mục phòng không tồn tại hoặc đã ngừng sử dụng. Vui lòng chọn lại.");
    }
    private static void Capacity(int guests, int? area)
    {
        if (guests <= 0 || area is <= 0) throw new BusinessException("Sức chứa và diện tích phòng phải lớn hơn 0. Vui lòng nhập lại.");
    }
    private async Task KeepSellableAsync(UnitOfWork db, HomestayItem home, CancellationToken ct)
    {
        if (home.Status == HomestayStatus.Active && !await repository.HasSellableRoomAsync(db, home.Id, ct).ConfigureAwait(false))
            throw new BusinessException("Homestay đang hoạt động phải còn ít nhất một phòng bật thuộc loại phòng bật. Vui lòng giữ lại phòng bán.");
    }
    public Task<RoomTypeItem> CreateTypeAsync(CreateRoomTypeRequest request, CancellationToken cancellationToken = default)
    {
        Writer.Require(Permission.RoomManage); ArgumentNullException.ThrowIfNull(request); Capacity(request.MaxGuests, request.AreaM2);
        var v = request with { Name = ListingWriteRules.Required(request.Name, 100, "tên loại phòng"), Description = ListingWriteRules.Text(request.Description, 1000) };
        return Writer.ExecuteAsync((_, _) => Task.FromResult(v.HomestayId), v.RowVer, Permission.RoomManage, "Room.CreateType", async (db, _, now, ct) =>
        {
            await RequireCategoryAsync(db, v.CategoryId, ct).ConfigureAwait(false);
            var id = await repository.InsertTypeAsync(db, v, now, ct).ConfigureAwait(false);
            return (id, null, (object?)await TypeAsync(db, v.HomestayId, id, ct).ConfigureAwait(false));
        }, (db, home, id, _, ct) => TypeAsync(db, home, id, ct), cancellationToken);
    }
    public Task<RoomTypeItem> UpdateTypeAsync(UpdateRoomTypeRequest request, CancellationToken cancellationToken = default)
    {
        Writer.Require(Permission.RoomManage); ArgumentNullException.ThrowIfNull(request); Capacity(request.MaxGuests, request.AreaM2);
        var v = request with { Name = ListingWriteRules.Required(request.Name, 100, "tên loại phòng"), Description = ListingWriteRules.Text(request.Description, 1000) };
        return Writer.ExecuteAsync((db, ct) => HomeAsync(db, v.RoomTypeId, "Type", ct), v.RowVer, Permission.RoomManage, "Room.UpdateType", async (db, home, now, ct) =>
        {
            await db.AcquireAppLockAsync($"RoomType:{v.RoomTypeId}", 5000, ct).ConfigureAwait(false);
            var old = await TypeAsync(db, home.Id, v.RoomTypeId, ct).ConfigureAwait(false);
            // Retaining an inactive historical category is allowed; selecting a new one requires an active catalog item.
            if (old.CategoryId != v.CategoryId) await RequireCategoryAsync(db, v.CategoryId, ct).ConfigureAwait(false);
            await repository.UpdateTypeAsync(db, v, now, ct).ConfigureAwait(false);
            return (old.Id, (object?)old, (object?)await TypeAsync(db, home.Id, old.Id, ct).ConfigureAwait(false));
        }, (db, home, id, _, ct) => TypeAsync(db, home, id, ct), cancellationToken);
    }
    public Task<RoomTypeItem> SetTypeActiveAsync(SetRoomTypeActiveRequest request, CancellationToken cancellationToken = default)
    {
        Writer.Require(Permission.RoomManage); ArgumentNullException.ThrowIfNull(request);
        return Writer.ExecuteAsync((db, ct) => HomeAsync(db, request.RoomTypeId, "Type", ct), request.RowVer, Permission.RoomManage, "Room.SetTypeActive", async (db, home, now, ct) =>
        {
            await ListingChildWriter.LockTypesAsync(db, home.Id, ct).ConfigureAwait(false);
            var old = await TypeAsync(db, home.Id, request.RoomTypeId, ct).ConfigureAwait(false);
            await repository.SetActiveAsync(db, old.Id, true, request.IsActive, now, ct).ConfigureAwait(false);
            if (!request.IsActive) await KeepSellableAsync(db, home, ct).ConfigureAwait(false);
            return (old.Id, (object?)old, (object?)new { old.Id, request.IsActive });
        }, (db, home, id, _, ct) => TypeAsync(db, home, id, ct), cancellationToken);
    }
    public Task<RoomItem> CreateRoomAsync(CreateRoomRequest request, CancellationToken cancellationToken = default)
    {
        Writer.Require(Permission.RoomManage); ArgumentNullException.ThrowIfNull(request);
        var v = request with { RoomCode = ListingWriteRules.Required(request.RoomCode, 20, "mã phòng"), Note = ListingWriteRules.Text(request.Note, 255) };
        return Writer.ExecuteAsync((db, ct) => HomeAsync(db, v.RoomTypeId, "Type", ct), v.RowVer, Permission.RoomManage, "Room.Create", async (db, home, now, ct) =>
        {
            await db.AcquireAppLockAsync($"RoomType:{v.RoomTypeId}", 5000, ct).ConfigureAwait(false);
            _ = await TypeAsync(db, home.Id, v.RoomTypeId, ct).ConfigureAwait(false);
            var id = await repository.InsertRoomAsync(db, v, now, ct).ConfigureAwait(false);
            return (id, null, (object?)await RoomAsync(db, home.Id, id, ct).ConfigureAwait(false));
        }, (db, home, id, _, ct) => RoomAsync(db, home, id, ct), cancellationToken);
    }
    public Task<RoomItem> UpdateRoomAsync(UpdateRoomRequest request, CancellationToken cancellationToken = default)
    {
        Writer.Require(Permission.RoomManage); ArgumentNullException.ThrowIfNull(request);
        var v = request with { RoomCode = ListingWriteRules.Required(request.RoomCode, 20, "mã phòng"), Note = ListingWriteRules.Text(request.Note, 255) };
        return Writer.ExecuteAsync((db, ct) => HomeAsync(db, v.RoomId, "Room", ct), v.RowVer, Permission.RoomManage, "Room.Update", async (db, home, now, ct) =>
        {
            var old = await RoomAsync(db, home.Id, v.RoomId, ct).ConfigureAwait(false);
            await db.AcquireAppLockAsync($"RoomType:{old.RoomTypeId}", 5000, ct).ConfigureAwait(false);
            await repository.UpdateRoomAsync(db, v, now, ct).ConfigureAwait(false);
            return (old.Id, (object?)old, (object?)await RoomAsync(db, home.Id, old.Id, ct).ConfigureAwait(false));
        }, (db, home, id, _, ct) => RoomAsync(db, home, id, ct), cancellationToken);
    }
    public Task<RoomItem> SetRoomActiveAsync(SetRoomActiveRequest request, CancellationToken cancellationToken = default)
    {
        Writer.Require(Permission.RoomManage); ArgumentNullException.ThrowIfNull(request);
        return Writer.ExecuteAsync((db, ct) => HomeAsync(db, request.RoomId, "Room", ct), request.RowVer, Permission.RoomManage, "Room.SetActive", async (db, home, now, ct) =>
        {
            await ListingChildWriter.LockTypesAsync(db, home.Id, ct).ConfigureAwait(false);
            var old = await RoomAsync(db, home.Id, request.RoomId, ct).ConfigureAwait(false);
            await repository.SetActiveAsync(db, old.Id, false, request.IsActive, now, ct).ConfigureAwait(false);
            if (!request.IsActive) await KeepSellableAsync(db, home, ct).ConfigureAwait(false);
            return (old.Id, (object?)old, (object?)new { old.Id, request.IsActive });
        }, (db, home, id, _, ct) => RoomAsync(db, home, id, ct), cancellationToken);
    }
    public Task<RoomPriceList> AddPriceAsync(AddRoomPriceRequest request, CancellationToken cancellationToken = default)
    {
        Writer.Require(Permission.RoomPriceAdd); ArgumentNullException.ThrowIfNull(request);
        return PricesAsync(request.RoomTypeId, request.EffectiveFrom, request.WeekdayPrice, request.WeekendPrice, request.HolidayPrice, request.RowVer, false, cancellationToken);
    }
    public Task<RoomPriceList> UpdatePriceAsync(UpdateRoomPriceRequest request, CancellationToken cancellationToken = default)
    {
        Writer.Require(Permission.RoomPriceAdd); ArgumentNullException.ThrowIfNull(request);
        return PricesAsync(request.RoomTypeId, request.EffectiveFrom, request.WeekdayPrice, request.WeekendPrice, request.HolidayPrice, request.RowVer, true, cancellationToken);
    }
    private Task<RoomPriceList> PricesAsync(int type, DateOnly from, decimal weekday, decimal weekend, decimal holiday, byte[] version, bool update, CancellationToken cancellationToken)
    {
        if (new[] { weekday, weekend, holiday }.Any(p => p <= 0 || p > 999999999999999999m || decimal.Truncate(p) != p))
            throw new BusinessException("Giá phòng phải là số đồng nguyên dương, tối đa 18 chữ số. Vui lòng nhập lại đủ ba mức giá.");
        return Writer.ExecuteAsync((db, ct) => HomeAsync(db, type, "Type", ct), version, Permission.RoomPriceAdd, update ? "Price.Update" : "Price.Add", async (db, home, _, ct) =>
        {
            await db.AcquireAppLockAsync($"RoomType:{type}", 5000, ct).ConfigureAwait(false);
            await TypeAsync(db, home.Id, type, ct).ConfigureAwait(false);
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false); var today = DateOnly.FromDateTime(now.AddHours(7));
            if (update && from <= today) throw new BusinessException("Giá đã hiệu lực không được sửa. Vui lòng thêm phiên bản giá mới.");
            if (!update && from < today) throw new BusinessException("Ngày hiệu lực giá phải từ hôm nay Việt Nam trở đi. Vui lòng chọn lại.");
            var before = (await repository.ListPricesAsync(db, home.Id, now, ct).ConfigureAwait(false)).Versions.SingleOrDefault(p => p.RoomTypeId == type && p.EffectiveFrom == from);
            if (update && before is null) throw new NotFoundException();
            if (await repository.WritePricesAsync(db, type, from, weekday, weekend, holiday, update, dataScope.CurrentSession.RequireSession().UserId, now, ct).ConfigureAwait(false) != 3)
                throw new BusinessException("Phiên bản giá phải có đủ ba mức giá. Vui lòng tải lại bảng giá.");
            return (type, (object?)before, (object?)new { RoomTypeId = type, EffectiveFrom = from, WeekdayPrice = weekday, WeekendPrice = weekend, HolidayPrice = holiday });
        }, (db, home, _, _, ct) => ReadPricesAsync(db, home, ct), cancellationToken);
    }
    private async Task<RoomPriceList> ReadPricesAsync(UnitOfWork db, int home, CancellationToken ct) =>
        await repository.ListPricesAsync(db, home, await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
    public Task<RoomBlockItem> BlockAsync(BlockRoomRequest request, CancellationToken cancellationToken = default)
    {
        Writer.Require(Permission.RoomBlockManage); ArgumentNullException.ThrowIfNull(request);
        if (request.EndDate <= request.StartDate) throw new BusinessException("Ngày kết thúc khóa phòng phải sau ngày bắt đầu. Vui lòng chọn lại.");
        if (!Enum.IsDefined(request.Reason)) throw new BusinessException("Lý do khóa phòng không hợp lệ. Vui lòng chọn lại.");
        var v = request with { Note = ListingWriteRules.Text(request.Note, 255) };
        return Writer.ExecuteAsync((db, ct) => HomeAsync(db, v.RoomId, "Room", ct), v.RowVer, Permission.RoomBlockManage, "Block.Create", async (db, home, _, ct) =>
        {
            var room = await RoomAsync(db, home.Id, v.RoomId, ct).ConfigureAwait(false);
            await db.AcquireAppLockAsync($"RoomType:{room.RoomTypeId}", 5000, ct).ConfigureAwait(false);
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            await new HoldExpiry(new HoldExpiryRepository(), new AuditWriter(), Environment.MachineName).ExpireAsync(db, room.RoomTypeId, now, ct).ConfigureAwait(false);
            var conflict = await repository.FindBlockConflictAsync(db, room.Id, v.StartDate, v.EndDate, now, ct).ConfigureAwait(false);
            if (conflict?.BookingCode is { } code) throw new BusinessException($"Phòng đang được đơn {code} chiếm trong khoảng ngày này. Vui lòng xử lý đơn trước khi khóa.");
            if (conflict is not null) throw new BusinessException("Phòng đã bị khóa trong khoảng ngày này. Vui lòng kiểm tra lịch phòng.");
            var id = await repository.InsertBlockAsync(db, v, dataScope.CurrentSession.RequireSession().UserId, now, ct).ConfigureAwait(false);
            return (id, null, (object?)new { RoomBlockId = id, v.RoomId, v.StartDate, v.EndDate, Reason = v.Reason.ToString(), v.Note });
        }, async (db, home, id, _, ct) => await repository.GetBlockAsync(db, home, id, ct).ConfigureAwait(false) ?? throw new NotFoundException(), cancellationToken);
    }
    public Task<RoomBlockChangeResult> UnblockAsync(UnblockRoomRequest request, CancellationToken cancellationToken = default)
    {
        Writer.Require(Permission.RoomBlockManage); ArgumentNullException.ThrowIfNull(request); var reason = ListingWriteRules.Reason(request.Reason);
        return Writer.ExecuteAsync((db, ct) => HomeAsync(db, request.RoomBlockId, "Block", ct), request.RowVer, Permission.RoomBlockManage, "Block.Remove", async (db, home, _, ct) =>
        {
            var old = await repository.GetBlockAsync(db, home.Id, request.RoomBlockId, ct).ConfigureAwait(false) ?? throw new NotFoundException();
            var room = await RoomAsync(db, home.Id, old.RoomId, ct).ConfigureAwait(false);
            await db.AcquireAppLockAsync($"RoomType:{room.RoomTypeId}", 5000, ct).ConfigureAwait(false);
            if (await repository.DeleteBlockAsync(db, old.Id, ct).ConfigureAwait(false) == 0) throw new ConcurrencyException();
            return (old.Id, (object?)old, (object?)new { RemovedRoomBlockId = old.Id });
        }, async (db, home, id, _, ct) => new RoomBlockChangeResult(home, id,
            (await new HomestayRepository().GetAsync(db, home, "1=1", _ => { }, ct).ConfigureAwait(false) ?? throw new NotFoundException()).RowVer), cancellationToken, reason);
    }
}
