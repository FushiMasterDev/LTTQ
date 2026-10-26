using Homestay.BLL.Infrastructure;
using Homestay.DTO.Availability;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Rules;

public sealed class MissingRoomPriceException(DateOnly night, DayType dayType)
    : BusinessException($"Không bán: thiếu giá {dayType} hiệu lực cho đêm {night:dd/MM/yyyy}. Vui lòng bổ sung giá.")
{
    public DateOnly Night { get; } = night;
    public DayType DayType { get; } = dayType;
}

/// <summary>Nguồn công thức duy nhất; không đọc đồng hồ hay database.</summary>
public static class PricingCalculator
{
    public static decimal RoundToThousand(decimal amount) => decimal.Round(amount / 1000m, 0, MidpointRounding.AwayFromZero) * 1000m;

    public static DayType ClassifyNight(DateOnly night, IReadOnlySet<DateOnly> holidays) => holidays.Contains(night)
        ? DayType.Holiday : night.DayOfWeek is DayOfWeek.Friday or DayOfWeek.Saturday ? DayType.Weekend : DayType.Weekday;

    public static PriceBreakdown Calculate(DateOnly checkIn, DateOnly checkOut, IReadOnlySet<DateOnly> holidays,
        IReadOnlyList<RoomPriceVersion> prices, decimal depositRate)
    {
        if (checkOut <= checkIn) throw new BusinessException("Ngày trả phải sau ngày nhận. Vui lòng chọn lại ngày.");
        if (depositRate is <= 0 or > 1) throw new BusinessException("Tỷ lệ cọc phải lớn hơn 0 và không quá 1.");
        var versions = prices.GroupBy(p => p.DayType).ToDictionary(g => g.Key, g => g.OrderByDescending(p => p.EffectiveFrom).ToArray());
        var nights = new List<NightlyPrice>();
        for (var night = checkIn; night < checkOut; night = night.AddDays(1))
        {
            var dayType = ClassifyNight(night, holidays);
            decimal price = 0;
            // Spec 7.6.1: phải đủ cả ba mức giá hiệu lực, kể cả loại ngày không xuất hiện trong kỳ ở.
            foreach (var type in Enum.GetValues<DayType>())
            {
                var version = versions.GetValueOrDefault(type)?.FirstOrDefault(p => p.EffectiveFrom <= night);
                if (version is null || version.Price <= 0) throw new MissingRoomPriceException(night, type);
                if (type == dayType) price = version.Price;
            }
            nights.Add(new(night, night.DayOfWeek, dayType, price));
        }
        var total = nights.Sum(n => n.Price);
        return new(nights.AsReadOnly(), total, total / nights.Count, depositRate, RoundToThousand(total * depositRate));
    }

    public static ApplicableCancellationPolicy SelectCancellationPolicy(int homestayId, bool hasHoliday,
        IReadOnlyList<ApplicableCancellationPolicy> policies)
    {
        var active = policies.Where(p => p.IsActive).ToArray();
        ApplicableCancellationPolicy? Find(int? id, CancellationScope scope) => active.SingleOrDefault(p => p.HomestayId == id && p.Scope == scope);
        return (hasHoliday ? Find(homestayId, CancellationScope.Holiday) : null)
            ?? Find(homestayId, CancellationScope.All)
            ?? (hasHoliday ? Find(null, CancellationScope.Holiday) : null)
            ?? Find(null, CancellationScope.All)
            ?? throw new BusinessException("Thiếu chính sách hủy áp dụng. Vui lòng báo Quản lý bổ sung chính sách chung.");
    }
}
