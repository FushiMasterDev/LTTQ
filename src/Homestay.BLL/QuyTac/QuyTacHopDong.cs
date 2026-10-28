using Homestay.BLL.Infrastructure;
namespace Homestay.BLL.Rules;

public static class ContractRules
{
    // Decimal rate (.1250), never percent (12.5). Bounds come from SettingsProvider at write time.
    public static void Validate(decimal commissionRate, DateOnly startDate, DateOnly endDate,
        decimal minimumCommission = .10m, decimal maximumCommission = .15m)
    {
        if (minimumCommission < .01m || maximumCommission > .99m || minimumCommission > maximumCommission)
            throw new BusinessException("Khoảng hoa hồng cấu hình không hợp lệ. Vui lòng kiểm tra tham số hợp đồng.");
        if (commissionRate < minimumCommission || commissionRate > maximumCommission || decimal.Round(commissionRate, 4) != commissionRate)
            throw new BusinessException($"Hoa hồng phải từ {minimumCommission * 100:0.##}% đến {maximumCommission * 100:0.##}% và tối đa bốn số lẻ của tỷ lệ.");
        if (endDate <= startDate) throw new BusinessException("Ngày kết thúc hợp đồng phải sau ngày bắt đầu.");
    }
    public static void RequireCurrentDates(DateOnly startDate, DateOnly endDate, DateOnly serverToday)
    {
        if (startDate > serverToday || endDate < serverToday)
            throw new BusinessException("Hợp đồng phải phủ hôm nay; chưa hỗ trợ kích hoạt trong tương lai.");
    }
}
