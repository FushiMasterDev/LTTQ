using System.Globalization;

namespace Homestay.BLL.Rules;

/// <summary>Vietnamese integer VND spelling for receipts and settlement documents.</summary>
public static class VietnameseAmountWords
{
    private static readonly string[] Digits = ["không", "một", "hai", "ba", "bốn", "năm", "sáu", "bảy", "tám", "chín"];
    private static readonly string[] Groups = ["", "nghìn", "triệu", "tỷ", "nghìn tỷ", "triệu tỷ", "tỷ tỷ"];

    public static string Spell(decimal amount)
    {
        if (amount < 0 || amount != decimal.Truncate(amount) || amount > 999_999_999_999_999_999m)
            throw new ArgumentOutOfRangeException(nameof(amount), "Số tiền phải là số nguyên VND không âm, tối đa 18 chữ số.");
        if (amount == 0) return "Không đồng";
        var number = decimal.ToInt64(amount).ToString(CultureInfo.InvariantCulture);
        var padded = number.PadLeft(((number.Length + 2) / 3) * 3, '0');
        var chunks = Enumerable.Range(0, padded.Length / 3).Select(i => int.Parse(padded.AsSpan(i * 3, 3), CultureInfo.InvariantCulture)).ToArray();
        var parts = new List<string>();
        var first = Array.FindIndex(chunks, n => n != 0);
        for (var i = first; i < chunks.Length; i++)
        {
            if (chunks[i] == 0) continue;
            parts.Add(ReadGroup(chunks[i], i > first) + " " + Groups[chunks.Length - i - 1]);
        }
        var result = string.Join(" ", parts).Trim();
        return char.ToUpper(result[0], CultureInfo.GetCultureInfo("vi-VN")) + result[1..] + " đồng";
    }

    private static string ReadGroup(int value, bool forceHundreds)
    {
        var hundred = value / 100; var ten = value / 10 % 10; var unit = value % 10;
        var words = new List<string>();
        if (hundred > 0 || forceHundreds) { words.Add(Digits[hundred]); words.Add("trăm"); }
        if (ten > 1) { words.Add(Digits[ten]); words.Add("mươi"); if (unit == 1) words.Add("mốt"); else if (unit == 5) words.Add("lăm"); else if (unit > 0) words.Add(Digits[unit]); }
        else if (ten == 1) { words.Add("mười"); if (unit == 5) words.Add("lăm"); else if (unit > 0) words.Add(Digits[unit]); }
        else if (unit > 0) { if (hundred > 0 || forceHundreds) words.Add("lẻ"); words.Add(Digits[unit]); }
        return string.Join(" ", words);
    }
}
