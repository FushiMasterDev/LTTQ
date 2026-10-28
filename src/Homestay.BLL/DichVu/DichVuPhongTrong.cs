using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Availability;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Services;

public sealed class AvailabilityService(Authorizer authorizer, ICurrentSession currentSession, ConnectionFactory factory,
    AvailabilityRepository repository, DbClock clock, SettingsProvider settings)
{
    public Task<IReadOnlyList<AvailabilityItem>> SearchAsync(AvailabilitySearchRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.AvailabilitySearch);
        ArgumentNullException.ThrowIfNull(request);
        ValidateFilters(request);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync<IReadOnlyList<AvailabilityItem>>(async (db, token) =>
        {
            var now = await clock.GetUtcNowAsync(db, token).ConfigureAwait(false);
            await ValidateStayAsync(db, request.CheckIn, request.CheckOut, request.Guests, now, token).ConfigureAwait(false);
            var rate = await settings.GetBookingDepositRateAsync(token, db).ConfigureAwait(false);
            var data = await repository.LoadAsync(db, request, null, now, token).ConfigureAwait(false);
            var prices = data.Prices.ToLookup(p => p.RoomTypeId, p => p.Version);
            var rooms = data.Rooms.ToLookup(r => r.RoomTypeId);
            var result = new List<AvailabilityItem>();
            foreach (var candidate in data.Candidates)
            {
                PriceBreakdown pricing;
                try { pricing = PricingCalculator.Calculate(request.CheckIn, request.CheckOut, data.Holidays, prices[candidate.RoomTypeId].ToArray(), rate); }
                catch (MissingRoomPriceException) { continue; }
                if (request.MinAveragePrice is { } min && pricing.AveragePrice < min || request.MaxAveragePrice is { } max && pricing.AveragePrice > max) continue;
                result.Add(Item(candidate, rooms[candidate.RoomTypeId].ToArray(), pricing, data));
            }
            return result;
        }, ct), cancellationToken);
    }

    public Task<BookingQuote> QuoteAsync(QuoteRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.AvailabilitySearch);
        ArgumentNullException.ThrowIfNull(request);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        {
            var now = await clock.GetUtcNowAsync(db, token).ConfigureAwait(false);
            await ValidateStayAsync(db, request.CheckIn, request.CheckOut, request.Guests, now, token).ConfigureAwait(false);
            var data = await repository.LoadAsync(db, new() { CheckIn = request.CheckIn, CheckOut = request.CheckOut, Guests = request.Guests }, request.RoomTypeId, now, token).ConfigureAwait(false);
            var candidate = data.Candidates.SingleOrDefault() ?? throw new BusinessException("Loại phòng không đủ điều kiện bán cho kỳ ở này. Vui lòng chọn lại.");
            var rate = await settings.GetBookingDepositRateAsync(token, db).ConfigureAwait(false);
            var pricing = PricingCalculator.Calculate(request.CheckIn, request.CheckOut, data.Holidays, data.Prices.Select(p => p.Version).ToArray(), rate);
            var bank = new FujiBankDetails(await settings.GetFujiBankNameAsync(token, db).ConfigureAwait(false), await settings.GetFujiBankAccountNoAsync(token, db).ConfigureAwait(false),
                await settings.GetFujiBankAccountNameAsync(token, db).ConfigureAwait(false));
            return new BookingQuote(Item(candidate, data.Rooms, pricing, data), pricing, pricing.DepositAmount,
                currentSession.RequireSession().Position is Position.Manager or Position.Accountant ? candidate.CommissionRate : null,
                data.Rooms.Where(r => r.IsAvailable).Select(r => new AvailableRoom(r.RoomId, r.RoomCode)).ToArray(), bank);
        }, ct), cancellationToken);
    }

    public Task<RoomCalendar> GetCalendarAsync(RoomCalendarRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.RoomCalendarView);
        ArgumentNullException.ThrowIfNull(request);
        if (request.Days is not (14 or 31) || (request.HomestayId.HasValue == request.AreaId.HasValue) || request.HomestayId is <= 0 || request.AreaId is <= 0
            || request.FromDate.DayNumber > DateOnly.MaxValue.DayNumber - request.Days + 1)
            throw new BusinessException("Chọn một homestay hoặc một khu vực và khoảng 14 hoặc 31 ngày hợp lệ.");
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        {
            var now = await clock.GetUtcNowAsync(db, token).ConfigureAwait(false);
            return new RoomCalendar(request.FromDate, request.Days, now, await repository.CalendarAsync(db, request, now, token).ConfigureAwait(false));
        }, ct), cancellationToken);
    }

    private async Task ValidateStayAsync(DbSession db, DateOnly checkIn, DateOnly checkOut, int guests, DateTime now, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(now.AddHours(7));
        var max = await settings.GetBookingMaxNightsAsync(ct, db).ConfigureAwait(false);
        if (checkIn < today || checkOut <= checkIn || checkOut.DayNumber - checkIn.DayNumber > max || guests < 1)
            throw new BusinessException($"Ngày nhận phải từ hôm nay, ngày trả sau ngày nhận, tối đa {max} đêm và số khách phải dương. Vui lòng chọn lại.");
    }
    private static void ValidateFilters(AvailabilitySearchRequest request)
    {
        if (request.AreaId is <= 0 || request.CategoryId is <= 0 || request.MinAveragePrice is < 0 || request.MaxAveragePrice is < 0
            || request.MinAveragePrice > request.MaxAveragePrice || request.AmenityIds is null || request.AmenityIds.Count > 200 || request.AmenityIds.Any(id => id <= 0))
            throw new BusinessException("Bộ lọc không hợp lệ. Vui lòng kiểm tra khu vực, danh mục, khoảng giá và tiện nghi.");
    }
    private static AvailabilityItem Item(AvailabilityCandidate c, IReadOnlyList<CandidateRoom> rooms, PriceBreakdown pricing, AvailabilityData data) => new()
    {
        HomestayId = c.HomestayId,
        HomestayName = c.HomestayName,
        AreaId = c.AreaId,
        AreaName = c.AreaName,
        RoomTypeId = c.RoomTypeId,
        RoomTypeName = c.RoomTypeName,
        CategoryId = c.CategoryId,
        CategoryName = c.CategoryName,
        MaxGuests = c.MaxGuests,
        TotalActiveRooms = rooms.Count,
        AvailableRooms = rooms.Count(r => r.IsAvailable),
        AveragePrice = pricing.AveragePrice,
        TotalAmount = pricing.TotalAmount,
        DepositAmount = pricing.DepositAmount,
        CancellationPolicy = PricingCalculator.SelectCancellationPolicy(c.HomestayId, pricing.Nights.Any(n => n.DayType == DayType.Holiday), data.Policies)
    };
}
