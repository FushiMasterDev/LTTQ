using System.Globalization;
using System.Text;

namespace Homestay.GUI.Common;

/// <summary>Định dạng hiển thị kiểu Việt Nam: tiền, ngày, giờ Việt Nam, chuẩn hóa từ khóa tìm kiếm.</summary>
public static class UiFormat
{
    public static readonly CultureInfo Vietnamese = CultureInfo.GetCultureInfo("vi-VN");
    public static string Money(decimal value) => value.ToString("#,0", Vietnamese) + " ₫";
    public static string Date(DateTime value) => value.ToString("dd/MM/yyyy", Vietnamese);
    public static DateTime VietnamTime(DateTimeOffset value) => value.ToOffset(TimeSpan.FromHours(7)).DateTime;
    public static string SearchKey(string value)
    {
        var normalized = value.Replace('đ', 'd').Replace('Đ', 'D').Normalize(NormalizationForm.FormD);
        return new string(normalized.Where(c => CharUnicodeInfo.GetUnicodeCategory(c)
            != UnicodeCategory.NonSpacingMark).ToArray()).ToUpperInvariant();
    }
}
