using Homestay.BLL.Infrastructure;
using Homestay.DTO.Common;

namespace Homestay.BLL.Rules;

internal static class StaffInputRules
{
    internal static void Page(PageRequest page, params string[] sortColumns)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.PageNumber < 1 || page.PageSize is not (10 or 20 or 50 or 100) || (long)(page.PageNumber - 1) * page.PageSize > int.MaxValue)
            throw new BusinessException("Trang không hợp lệ. Vui lòng chọn cỡ trang 10, 20, 50 hoặc 100.");
        Text(page.SearchText, 200);
        if (!string.IsNullOrEmpty(page.SortColumn) && !sortColumns.Contains(page.SortColumn, StringComparer.Ordinal))
            throw new BusinessException("Cột sắp xếp không hợp lệ. Vui lòng chọn lại cột.");
    }
    internal static void Text(string? value, int max)
    {
        if (value?.Length > max) throw new BusinessException($"Nội dung tối đa {max} ký tự. Vui lòng rút gọn.");
    }
    internal static void RowVer(byte[] value)
    {
        if (value is not { Length: 8 }) throw new BusinessException("Thiếu phiên bản dữ liệu. Vui lòng tải lại rồi thao tác lại.");
    }
    public static string? MaskIdentity(string? value) => string.IsNullOrEmpty(value) ? value : new string('•', Math.Max(8, value.Length - 4)) + value[^Math.Min(4, value.Length)..];
}
