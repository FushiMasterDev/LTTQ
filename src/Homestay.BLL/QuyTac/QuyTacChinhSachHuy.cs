using Homestay.BLL.Infrastructure;
using Homestay.DTO.Catalog;

namespace Homestay.BLL.Rules;

/// <summary>Kiểm tra chính sách hủy hợp lệ (các mốc ngày và tỷ lệ hoàn tiền phải hợp lý).</summary>
public static class CancellationPolicyRules
{
    public static void Validate(IReadOnlyList<CancellationRuleItem>? rules)
    {
        if (rules is null || !rules.Any(r => r.MinDaysBefore == 0))
            throw new BusinessException("Chính sách phải có mốc 0 ngày. Vui lòng bổ sung mốc này.");
        if (rules.Any(r => r.MinDaysBefore < 0 || r.RefundPercent is < 0 or > 100))
            throw new BusinessException("Số ngày phải từ 0 và phần trăm hoàn từ 0 đến 100. Vui lòng sửa các mốc.");
        if (rules.Select(r => r.MinDaysBefore).Distinct().Count() != rules.Count)
            throw new BusinessException("Các mốc số ngày không được trùng nhau. Vui lòng sửa các mốc.");
        var sorted = rules.OrderBy(r => r.MinDaysBefore).ToArray();
        for (var i = 1; i < sorted.Length; i++)
            if (sorted[i].RefundPercent < sorted[i - 1].RefundPercent)
                throw new BusinessException("Phần trăm hoàn không được tăng khi số ngày giảm. Vui lòng sửa các mốc.");
    }
}
