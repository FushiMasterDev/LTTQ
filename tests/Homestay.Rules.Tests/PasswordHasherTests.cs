using System.Security.Cryptography;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Security;

namespace Homestay.Rules.Tests;

public sealed class PasswordHasherTests
{
    private readonly PasswordHasher _hasher = new();

    [Fact]
    public void HashUsesRequiredFormatAndRandomSalt()
    {
        var encoded = _hasher.Hash("MậtKhẩu123");
        var parts = encoded.Split('$');
        Assert.Equal(4, parts.Length);
        Assert.Equal("PBKDF2-SHA256", parts[0]);
        Assert.Equal("600000", parts[1]);
        Assert.Equal(16, Convert.FromBase64String(parts[2]).Length);
        Assert.Equal(32, Convert.FromBase64String(parts[3]).Length);
        Assert.NotEqual(encoded, _hasher.Hash("MậtKhẩu123"));
        Assert.Equal(new PasswordVerificationResult(true, false), _hasher.Verify("MậtKhẩu123", encoded));
        Assert.Equal(default, _hasher.Verify("SaiMậtKhẩu123", encoded));
    }

    [Theory]
    [InlineData(1, "rJD1TYj3W5Sdwgvkmwbmc0j4zOZYTc2x3MKZ2K4gxxA=")]
    [InlineData(12, "JCXMwgNbs+DNv2UxW+0CYqcNau0FIyvt2eHNToLjtIg=")]
    public void VerifiesTSqlSeedVectors(int repeat, string expected)
    {
        var password = string.Concat(Enumerable.Repeat("MậtKhẩu123", repeat));
        var encoded = $"PBKDF2-SHA256$1000$AAECAwQFBgcICQoLDA0ODw==${expected}";
        Assert.Equal(new PasswordVerificationResult(true, true), _hasher.Verify(password, encoded));
        Assert.False(_hasher.Verify(password + "x", encoded).IsValid);
    }

    [Fact]
    public void HigherIterationHashDoesNotNeedRehash()
    {
        var salt = new byte[16];
        var hash = Rfc2898DeriveBytes.Pbkdf2("Abc12345", salt, 600001, HashAlgorithmName.SHA256, 32);
        Assert.Equal(new PasswordVerificationResult(true, false), _hasher.Verify("Abc12345", $"PBKDF2-SHA256$600001${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("broken")]
    [InlineData("PBKDF2-SHA256$0$AAECAwQFBgcICQoLDA0ODw==$rJD1TYj3W5Sdwgvkmwbmc0j4zOZYTc2x3MKZ2K4gxxA=")]
    [InlineData("PBKDF2-SHA256$-1$x$x")]
    [InlineData("PBKDF2-SHA256$2147483648$x$x")]
    [InlineData("PBKDF2-SHA256$1000$invalid!$invalid!")]
    [InlineData("PBKDF2-SHA256$1000$AA==$AA==")]
    [InlineData("PBKDF2-SHA512$1000$x$x")]
    public void MalformedHashIsInvalid(string? encoded) => Assert.Equal(default, _hasher.Verify("Abc12345", encoded));

    [Theory]
    [InlineData("Abc1234")]
    [InlineData("abcdefgh")]
    [InlineData("12345678")]
    [InlineData("")]
    [InlineData(null)]
    public void PolicyRejectsWeakPassword(string? password) => Assert.Throws<BusinessException>(() => PasswordHasher.ValidatePolicy(password, "NV0001"));

    [Fact]
    public void PolicyUsesCaseInsensitiveUsernameComparison()
    {
        Assert.Throws<BusinessException>(() => PasswordHasher.ValidatePolicy("nV123456", "Nv123456"));
        PasswordHasher.ValidatePolicy("MậtKhẩu123", "NV0001");
        PasswordHasher.ValidatePolicy("abcd1234", "NV0001");
    }
}
