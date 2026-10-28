using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.BLL.Security;
using Homestay.BLL.Services;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Auth;
using Homestay.DTO.Catalog;
using Homestay.DTO.Enums;
using Homestay.DTO.Staff;

namespace Homestay.Rules.Tests;

/// <summary>Kiểm thử quy tắc M1.4: chuẩn hóa số điện thoại, chính sách hủy, dữ liệu nhập nhân viên.</summary>
public sealed class M14RulesTests
{
    [Theory]
    [InlineData("0912 345 678", "+84912345678")]
    [InlineData("0912.345-678", "+84912345678")]
    [InlineData("84912345678", "+84912345678")]
    [InlineData("+84912345678", "+84912345678")]
    [InlineData("024 1234 5678", "+842412345678")]
    [InlineData("842412345678", "+842412345678")]
    [InlineData("+84 28.1234.5678", "+842812345678")]
    [InlineData("\t0987\n654321 ", "+84987654321")]
    public void VietnamesePhoneIsNormalized(string input, string expected) => Assert.Equal(expected, PhoneNumber.Normalize(input));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("091234567")]
    [InlineData("09123456789")]
    [InlineData("+81912345678")]
    [InlineData("+840912345678")]
    [InlineData("+84912345678x")]
    [InlineData("(0912)345678")]
    [InlineData("０９１２３４５６７８")]
    [InlineData("912345678")]
    public void InvalidPhoneUsesBusinessMessage(string? input) =>
        Assert.Equal("Số điện thoại không hợp lệ", Assert.Throws<BusinessException>(() => PhoneNumber.Normalize(input)).Message);

    [Fact]
    public void CancellationTiersPermitUnsortedInputAndEqualRefunds()
    {
        CancellationPolicyRules.Validate([new(7, 100), new(0, 0), new(3, 50), new(2, 50)]);
        CancellationPolicyRules.Validate([new(0, 100)]);
    }

    [Fact]
    public void CancellationTiersRejectMissingZeroDuplicateNegativeAndReversedRefunds()
    {
        Assert.Throws<BusinessException>(() => CancellationPolicyRules.Validate(null));
        Assert.Throws<BusinessException>(() => CancellationPolicyRules.Validate([]));
        Assert.Throws<BusinessException>(() => CancellationPolicyRules.Validate([new(3, 50)]));
        Assert.Throws<BusinessException>(() => CancellationPolicyRules.Validate([new(0, 0), new(0, 50)]));
        Assert.Throws<BusinessException>(() => CancellationPolicyRules.Validate([new(0, 0), new(-1, 0)]));
        Assert.Throws<BusinessException>(() => CancellationPolicyRules.Validate([new(0, -1)]));
        Assert.Throws<BusinessException>(() => CancellationPolicyRules.Validate([new(0, 101)]));
        Assert.Throws<BusinessException>(() => CancellationPolicyRules.Validate([new(0, 80), new(7, 50)]));
    }

    [Fact]
    public void TemporaryPasswordsMeetPolicyAndAvoidAmbiguousCharacters()
    {
        var passwords = Enumerable.Range(0, 128).Select(_ => TemporaryPassword.Generate()).ToArray();
        foreach (var password in passwords)
        {
            Assert.InRange(password.Length, 10, 12);
            PasswordHasher.ValidatePolicy(password, "NV1234");
            Assert.DoesNotContain(password, c => "0O1lI".Contains(c));
        }
        Assert.Equal(passwords.Length, passwords.Distinct(StringComparer.Ordinal).Count());
    }

    [Theory]
    [InlineData(UserStatus.Active, EmploymentStatus.Active)]
    [InlineData(UserStatus.Locked, EmploymentStatus.Active)]
    [InlineData(UserStatus.Disabled, EmploymentStatus.Resigned)]
    [InlineData(UserStatus.Active, EmploymentStatus.Suspended)]
    public void WrongPasswordDoesNotExposeAccountExistenceOrStatus(UserStatus status, EmploymentStatus employment)
    {
        Assert.Equal("Tên đăng nhập hoặc mật khẩu không đúng", LoginErrors.ForCredentials(false, false, status, employment));
        Assert.Equal(LoginErrors.ForCredentials(false, false, status, employment), LoginErrors.ForCredentials(true, false, status, employment));
        Assert.Equal(LoginErrors.InactiveAccount, LoginErrors.ForCredentials(true, true, UserStatus.Locked, employment));
        Assert.Null(LoginErrors.ForCredentials(true, true, UserStatus.Active, EmploymentStatus.Active));
    }

    [Fact]
    public void TemporaryLockMessageRoundsUpAndNeverShowsZeroMinutes()
    {
        var now = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.EndsWith("sau 2 phút.", LoginErrors.LockedUntil(now.AddSeconds(61), now));
        Assert.EndsWith("sau 1 phút.", LoginErrors.LockedUntil(now.AddMilliseconds(1), now));
    }

    [Theory]
    [InlineData(Position.Staff)]
    [InlineData(Position.Accountant)]
    public async Task NonManagersAreRejectedBeforeValidationOrDatabase(Position position)
    {
        var context = new ServicesWithoutDatabase(position);
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Staff.ListAsync(new()));
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Staff.GetAsync(1));
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Staff.CreateAsync(new()));
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Staff.LockAccountAsync(new()));
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Staff.UnlockAccountAsync(new()));
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Staff.ResetPasswordAsync(new()));
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Catalog.CreateAreaAsync(new()));
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Catalog.CreateItemAsync(CatalogKind.Amenity, new("Test")));
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Catalog.SetActiveAsync(CatalogKind.Area, 1, true, false));
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Catalog.CreatePolicyAsync(new()));
        await Assert.ThrowsAsync<ForbiddenException>(() => context.Catalog.UpdateSettingAsync(new("Booking.HoldMinutes", "15", "15")));
        if (position == Position.Staff) await Assert.ThrowsAsync<ForbiddenException>(() => context.Catalog.ListSettingsAsync());
    }

    [Fact]
    public async Task EmptySessionHeartbeatAndLogoutAreIdempotentWithoutDatabase()
    {
        var context = new ServicesWithoutDatabase(Position.Manager);
        context.Current.Clear();
        Assert.False(await context.Sessions.HeartbeatAsync());
        await context.Sessions.LogoutAsync();
        await context.Sessions.LogoutAsync();
    }

    /// <summary>Bộ dịch vụ khởi tạo không có CSDL, dùng để kiểm tra lỗi nhập được chặn trước khi kết nối.</summary>
    private sealed class ServicesWithoutDatabase
    {
        public CurrentSession Current { get; } = new();
        public StaffService Staff { get; }
        public CatalogService Catalog { get; }
        public SessionService Sessions { get; }
        public ServicesWithoutDatabase(Position position)
        {
            Current.Set(new SessionInfo { Position = position });
            var factory = new ConnectionFactory("Server=unused;Database=unused", 1);
            var accounts = new AuthRepository();
            var authorizer = new Authorizer(Current);
            var validator = new WriteSessionValidator(accounts);
            var writes = new WriteOperationRunner(factory, Current, validator);
            var clock = new DbClock(factory);
            var settings = new SettingsProvider(factory);
            var audit = new AuditWriter();
            Staff = new(authorizer, Current, factory, writes, new StaffRepository(), accounts, clock, new PasswordHasher(), audit, "Rules");
            Catalog = new(authorizer, Current, factory, writes, new CatalogRepository(), clock, settings, audit, "Rules");
            Sessions = new(authorizer, Current, writes, validator, accounts, clock, settings, new PasswordHasher(), audit, "Rules");
        }
    }
}
