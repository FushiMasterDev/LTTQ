using System.Text.RegularExpressions;
using Homestay.BLL.Infrastructure;
using Homestay.DAL.Infrastructure;

namespace Homestay.Rules.Tests;

/// <summary>Kiểm thử dịch lỗi SQL và cấu hình: đọc thông báo lỗi không cần SQL Server, lỗi lạ được xử lý an toàn.</summary>
public sealed class ErrorAndSettingsTests
{
    [Theory]
    [InlineData(2627, "Violation of PRIMARY KEY constraint 'PK_RoomNights'. Cannot insert duplicate key in object 'dbo.RoomNights'.", "PK_RoomNights", DbConstraintKind.PrimaryKey)]
    [InlineData(2627, "Violation of UNIQUE KEY constraint 'UQ_Users_Username'. Cannot insert duplicate key in object 'dbo.Users'.", "UQ_Users_Username", DbConstraintKind.Unique)]
    [InlineData(2601, "Cannot insert duplicate key row in object 'dbo.Payments' with unique index 'UX_Payments_OneDeposit'.", "UX_Payments_OneDeposit", DbConstraintKind.Unique)]
    [InlineData(547, "The INSERT statement conflicted with the CHECK constraint \"CK_Payments_Amount\". The conflict occurred in database \"HomestayDesktop\", table \"dbo.Payments\".", "CK_Payments_Amount", DbConstraintKind.Check)]
    [InlineData(547, "The INSERT statement conflicted with the FOREIGN KEY constraint \"FK_Bookings_CustomerId\".", "FK_Bookings_CustomerId", DbConstraintKind.ForeignKey)]
    [InlineData(547, "The DELETE statement conflicted with the REFERENCE constraint \"FK_RoomNights_BookingId\".", "FK_RoomNights_BookingId", DbConstraintKind.ForeignKey)]
    public void SqlMessagesAreParsedWithoutSqlServer(int number, string message, string name, DbConstraintKind kind)
    {
        var exception = Assert.IsType<DbConstraintViolationException>(SqlErrorTranslator.ParseConstraint(number, message));
        Assert.Equal(name, exception.ConstraintName);
        Assert.Equal(kind, exception.Kind);
        Assert.Equal(number, exception.ErrorNumber);
    }

    [Fact]
    public void UnknownErrorsAndUnparseableNamesAreSafe()
    {
        Assert.Null(SqlErrorTranslator.ParseConstraint(50000, "unknown"));
        Assert.Null(SqlErrorTranslator.ParseConstraint(2627, "localized message")!.ConstraintName);
        Assert.Equal(ConstraintMessages.DefaultMessage, ConstraintMessages.GetMessage(null));
        Assert.Equal(ConstraintMessages.DefaultMessage, ConstraintMessages.GetMessage("CK_Unknown"));
        Assert.Equal(ConstraintMessages.DefaultMessage, ConstraintMessages.GetMessage("UQ_NewUnknown"));
    }

    internal static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "database", "001_schema.sql"))) return directory.FullName;
        throw new InvalidOperationException("Không tìm thấy gốc repo chứa database/001_schema.sql.");
    }

    [Fact]
    public void EverySchemaPrimaryAndUniqueKeyHasItsOwnActionableMessage()
    {
        var schema = File.ReadAllText(Path.Combine(FindRepoRoot(), "database", "001_schema.sql"));
        var names = Regex.Matches(schema, @"\b(?:PK|UQ|UX)_\w+").Select(m => m.Value).Distinct().ToArray();
        Assert.NotEmpty(names);
        foreach (var name in names)
        {
            Assert.True(ConstraintMessages.Messages.ContainsKey(name), name);
            var message = ConstraintMessages.GetMessage(name);
            Assert.NotEqual(ConstraintMessages.DefaultMessage, message);
            Assert.DoesNotContain(name, message);
        }
        Assert.Equal(names.Length, names.Select(ConstraintMessages.GetMessage).Distinct().Count());
    }

    [Fact]
    public void BusinessTranslationRetainsConstraintForLogging()
    {
        var source = new DbConstraintViolationException("PK_RoomNights", DbConstraintKind.PrimaryKey, 2627);
        var business = Assert.IsType<BusinessException>(BusinessErrorTranslator.Translate(source));
        Assert.Equal("Phòng vừa được đặt hoặc khóa trong khoảng ngày này. Vui lòng tải lại và chọn phòng khác.", business.Message);
        Assert.Equal("PK_RoomNights", business.ConstraintName);
        Assert.Same(source, business.InnerException);
        Assert.Equal("Mất kết nối máy chủ dữ liệu, vui lòng thử lại.", BusinessErrorTranslator.Translate(new DbUnavailableException(-2)).Message);
        Assert.Equal("Hệ thống đang bận xử lý cùng dữ liệu, vui lòng thử lại.", BusinessErrorTranslator.Translate(new DbLockTimeoutException("RoomType:1", -1)).Message);
        Assert.Equal(ConstraintMessages.DefaultMessage, BusinessErrorTranslator.Translate(new DbConstraintViolationException("FK_Unknown", DbConstraintKind.ForeignKey, 547)).Message);
    }

    [Theory]
    [InlineData("Booking.HoldMinutes", "5", "120", "4", "121")]
    [InlineData("Booking.DepositRate", "0.10", "1.00", "0.09", "1.01")]
    [InlineData("Change.MinDaysBefore", "0", "30", "-1", "31")]
    [InlineData("Change.WindowDays", "30", "365", "29", "366")]
    [InlineData("Contract.MinCommission", "0.01", "0.99", "0", "1")]
    [InlineData("Settlement.GraceDays", "0", "15", "-1", "16")]
    [InlineData("Session.IdleMinutes", "5", "240", "4", "241")]
    public void SettingsValidateInclusiveBoundaries(string key, string min, string max, string below, string above)
    {
        SettingsProvider.Validate(key, min);
        SettingsProvider.Validate(key, max);
        Assert.Throws<BusinessException>(() => SettingsProvider.Validate(key, below));
        Assert.Throws<BusinessException>(() => SettingsProvider.Validate(key, above));
        var message = Assert.Throws<BusinessException>(() => SettingsProvider.Validate(key, "invalid")).Message;
        Assert.Contains(key, message);
        Assert.Contains("khoảng", message);
    }

    [Theory]
    [InlineData("Booking.HoldMinutes", "5.5")]
    [InlineData("Booking.DepositRate", "0,3")]
    [InlineData("WorkingHours.Start", "24:00")]
    [InlineData("WorkingHours.End", "8:00")]
    [InlineData("Login.MaxFailed", "1.5")]
    [InlineData("Unknown", "123")]
    [InlineData("Fuji.BankName", "")]
    public void InvalidSettingsAreRejected(string key, string value) => Assert.Throws<BusinessException>(() => SettingsProvider.Validate(key, value));

    [Fact]
    public void AllSeedSettingsAreRecognizedAndValid()
    {
        var seed = File.ReadAllText(Path.Combine(FindRepoRoot(), "database", "010_seed_reference.sql"));
        var settings = Regex.Matches(seed, @"\('(?<key>(?:Booking|Change|Contract|Settlement|Voucher|Payment|Session|Login|Fuji|WorkingHours)\.[^']+)', N'(?<value>[^']*)'");
        Assert.Equal(SettingsProvider.Keys.Count, settings.Count);
        foreach (Match match in settings) SettingsProvider.Validate(match.Groups["key"].Value, match.Groups["value"].Value);
    }
}
