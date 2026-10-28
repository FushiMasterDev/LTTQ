using Homestay.BLL.Infrastructure;

namespace Homestay.BLL.Rules;

public static class PhoneNumber
{
    public static string Normalize(string? value)
    {
        var text = string.Concat((value ?? "").Where(c => !char.IsWhiteSpace(c) && c is not ('.' or '-')));
        if (text.StartsWith("+84", StringComparison.Ordinal)) text = "0" + text[3..];
        else if (text.StartsWith("84", StringComparison.Ordinal)) text = "0" + text[2..];
        if (text.All(c => c is >= '0' and <= '9') &&
            ((text.Length == 10 && text[0] == '0') ||
             (text.Length == 11 && text.StartsWith("02", StringComparison.Ordinal)))) return "+84" + text[1..];
        throw new BusinessException("Số điện thoại không hợp lệ");
    }
}
