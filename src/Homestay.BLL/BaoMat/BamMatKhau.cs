using System.Globalization;
using System.Security.Cryptography;

namespace Homestay.BLL.Security;

public readonly record struct PasswordVerificationResult(bool IsValid, bool NeedsRehash);

public sealed class PasswordHasher
{
    public const int Iterations = 600_000;
    public const int SaltSize = 16;
    public const int HashSize = 32;

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"PBKDF2-SHA256${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public PasswordVerificationResult Verify(string? password, string? encodedHash)
    {
        if (password is null || encodedHash is null) return default;
        var parts = encodedHash.Split('$');
        if (parts.Length != 4 || parts[0] != "PBKDF2-SHA256" ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var iterations) || iterations <= 0) return default;
        Span<byte> salt = stackalloc byte[SaltSize];
        Span<byte> expected = stackalloc byte[HashSize];
        if (!Convert.TryFromBase64String(parts[2], salt, out var saltLength) || saltLength != SaltSize ||
            !Convert.TryFromBase64String(parts[3], expected, out var hashLength) || hashLength != HashSize) return default;
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, HashSize);
        var valid = CryptographicOperations.FixedTimeEquals(actual, expected);
        return new(valid, valid && iterations < Iterations);
    }

    public static void ValidatePolicy(string? password, string username)
    {
        if (string.IsNullOrEmpty(password) || password.Length < 8 || !password.Any(char.IsLetter) || !password.Any(char.IsDigit))
            throw new Infrastructure.BusinessException("Mật khẩu phải có ít nhất 8 ký tự, bao gồm chữ và số.");
        if (string.Equals(password, username, StringComparison.OrdinalIgnoreCase))
            throw new Infrastructure.BusinessException("Mật khẩu không được trùng tên đăng nhập. Vui lòng chọn mật khẩu khác.");
    }
}
