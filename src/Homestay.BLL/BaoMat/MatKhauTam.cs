using System.Security.Cryptography;

namespace Homestay.BLL.Security;

/// <summary>Sinh mật khẩu tạm ngẫu nhiên khi tạo nhân viên mới hoặc đặt lại mật khẩu.</summary>
public static class TemporaryPassword
{
    private const string Letters = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    public static string Generate()
    {
        var chars = new char[12];
        chars[0] = Letters[RandomNumberGenerator.GetInt32(Letters.Length)];
        chars[1] = Digits[RandomNumberGenerator.GetInt32(Digits.Length)];
        var alphabet = Letters + Digits;
        for (var i = 2; i < chars.Length; i++) chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
        for (var i = chars.Length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
        return new string(chars);
    }
}
