using System.Collections.Frozen;
using System.Globalization;
using Homestay.DAL.Infrastructure;

namespace Homestay.BLL.Infrastructure;

public sealed class SettingsProvider(ConnectionFactory factory)
{
    private sealed record Rule(string Type, decimal? Min = null, decimal? Max = null);
    private static readonly FrozenDictionary<string, Rule> Rules = new Dictionary<string, Rule>(StringComparer.Ordinal)
    {
        ["Booking.HoldMinutes"] = new("int", 5m, 120m),
        ["Booking.ConfirmMinutes"] = new("int", 10m, 240m),
        ["Booking.DepositRate"] = new("decimal", 0.1m, 1.0m),
        ["Booking.MaxAwaitingPerCustomer"] = new("int", 1m, 10m),
        ["Booking.MaxReceiptRejections"] = new("int", 1m, 5m),
        ["Booking.MaxNights"] = new("int", 1m, 90m),
        ["Change.MinDaysBefore"] = new("int", 0m, 30m),
        ["Change.MaxPerBooking"] = new("int", 0m, 10m),
        ["Change.WindowDays"] = new("int", 30m, 365m),
        ["Contract.MinCommission"] = new("decimal", 0.01m, 0.99m),
        ["Contract.MaxCommission"] = new("decimal", 0.01m, 0.99m),
        ["Settlement.GraceDays"] = new("int", 0m, 15m),
        ["Voucher.IssueRate"] = new("decimal"),
        ["Voucher.MinAmount"] = new("decimal"),
        ["Voucher.MaxAmount"] = new("decimal"),
        ["Voucher.ValidMonths"] = new("int"),
        ["Voucher.MaxShareOfCommission"] = new("decimal"),
        ["Payment.ReviewAlertWorkingHours"] = new("int", 1m, 24m),
        ["Session.IdleMinutes"] = new("int", 5m, 240m),
        ["Login.MaxFailed"] = new("int"),
        ["Login.LockoutMinutes"] = new("int"),
        ["Fuji.BankName"] = new("string"),
        ["Fuji.BankAccountNo"] = new("string"),
        ["Fuji.BankAccountName"] = new("string"),
        ["WorkingHours.Start"] = new("TimeOnly"),
        ["WorkingHours.End"] = new("TimeOnly"),
    }.ToFrozenDictionary(StringComparer.Ordinal);
    public static IReadOnlyCollection<string> Keys { get; } = Array.AsReadOnly(Rules.Keys.Order(StringComparer.Ordinal).ToArray());

    public static (string ValueType, decimal? Minimum, decimal? Maximum, string? Format) GetDefinition(string key)
    {
        if (!Rules.TryGetValue(key, out var rule)) throw new BusinessException("Tham số không được hỗ trợ. Vui lòng tải lại danh sách tham số.");
        return (rule.Type, rule.Min, rule.Max, rule.Type == "TimeOnly" ? "HH:mm" : null);
    }

    public static void Validate(string key, string? value)
    {
        if (!Rules.TryGetValue(key, out var rule)) throw new BusinessException("Tham số không được hỗ trợ. Vui lòng tải lại danh sách tham số.");
        if (string.IsNullOrWhiteSpace(value) || value.Length > 500) throw new BusinessException($"Tham số {key} phải có từ 1 đến 500 ký tự.");
        if (rule.Type == "string") return;
        if (rule.Type == "TimeOnly")
        {
            if (!TimeOnly.TryParseExact(value, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                throw new BusinessException($"Tham số {key} phải có định dạng HH:mm, từ 00:00 đến 23:59.");
            return;
        }
        var valid = decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number);
        if (rule.Type == "int") valid = int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);
        if (rule.Min is { } min && rule.Max is { } max)
        {
            if (!valid || number < min || number > max)
                throw new BusinessException($"Tham số {key} phải {(rule.Type == "int" ? "là số nguyên " : "")}trong khoảng {min.ToString("0.##", CultureInfo.InvariantCulture)}–{max.ToString("0.##", CultureInfo.InvariantCulture)}.");
        }
        else if (!valid) throw new BusinessException($"Tham số {key} phải là {(rule.Type == "int" ? "số nguyên" : "số thập phân dùng dấu chấm")}.");
        // Spec 7.7 chưa nêu khoảng cho Voucher/Login: không tự đặt giới hạn nghiệp vụ.
    }

    public Task<IReadOnlyDictionary<string, string>> GetAllAsync(CancellationToken cancellationToken, DbSession? session = null) =>
        session is null
            ? BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(ReadAllAsync, ct), cancellationToken)
            : ReadAllAsync(session, cancellationToken);

    private static async Task<IReadOnlyDictionary<string, string>> ReadAllAsync(DbSession session, CancellationToken cancellationToken)
    {
        using var command = session.CreateCommand("SELECT SettingKey, SettingValue FROM dbo.SystemSettings;");
        var rows = await session.QueryAsync(command, r => (Key: r.GetString("SettingKey"), Value: r.GetString("SettingValue")), cancellationToken).ConfigureAwait(false);
        return rows.ToFrozenDictionary(r => r.Key, r => r.Value, StringComparer.Ordinal);
    }

    public Task<string> GetValueAsync(string key, CancellationToken cancellationToken, DbSession? session = null)
    {
        if (!Rules.ContainsKey(key)) throw new BusinessException("Tham số không được hỗ trợ. Vui lòng tải lại danh sách tham số.");
        async Task<string> ReadAsync(DbSession db, CancellationToken token)
        {
            using var command = db.CreateCommand("SELECT SettingValue FROM dbo.SystemSettings WHERE SettingKey=@key;");
            command.Parameters.AddVarChar("@key", 100, key);
            var value = await db.ScalarAsync(command, token).ConfigureAwait(false) as string;
            if (value is null) throw new BusinessException($"Thiếu tham số {key}. Vui lòng báo quản lý kiểm tra cấu hình.");
            Validate(key, value);
            return value;
        }
        // Trong thao tác ghi, để runner nhận deadlock và thử lại toàn bộ transaction.
        return session is null
            ? BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(ReadAsync, ct), cancellationToken)
            : ReadAsync(session, cancellationToken);
    }

    public async Task<int> GetBookingHoldMinutesAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Booking.HoldMinutes", cancellationToken, session).ConfigureAwait(false);
        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<int> GetBookingConfirmMinutesAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Booking.ConfirmMinutes", cancellationToken, session).ConfigureAwait(false);
        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<decimal> GetBookingDepositRateAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Booking.DepositRate", cancellationToken, session).ConfigureAwait(false);
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<int> GetBookingMaxAwaitingPerCustomerAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Booking.MaxAwaitingPerCustomer", cancellationToken, session).ConfigureAwait(false);
        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<int> GetBookingMaxReceiptRejectionsAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Booking.MaxReceiptRejections", cancellationToken, session).ConfigureAwait(false);
        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<int> GetBookingMaxNightsAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Booking.MaxNights", cancellationToken, session).ConfigureAwait(false);
        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<int> GetChangeMinDaysBeforeAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Change.MinDaysBefore", cancellationToken, session).ConfigureAwait(false);
        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<int> GetChangeMaxPerBookingAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Change.MaxPerBooking", cancellationToken, session).ConfigureAwait(false);
        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<int> GetChangeWindowDaysAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Change.WindowDays", cancellationToken, session).ConfigureAwait(false);
        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<decimal> GetContractMinCommissionAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Contract.MinCommission", cancellationToken, session).ConfigureAwait(false);
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<decimal> GetContractMaxCommissionAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Contract.MaxCommission", cancellationToken, session).ConfigureAwait(false);
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<int> GetSettlementGraceDaysAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Settlement.GraceDays", cancellationToken, session).ConfigureAwait(false);
        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<decimal> GetVoucherIssueRateAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Voucher.IssueRate", cancellationToken, session).ConfigureAwait(false);
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<decimal> GetVoucherMinAmountAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Voucher.MinAmount", cancellationToken, session).ConfigureAwait(false);
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<decimal> GetVoucherMaxAmountAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Voucher.MaxAmount", cancellationToken, session).ConfigureAwait(false);
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<int> GetVoucherValidMonthsAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Voucher.ValidMonths", cancellationToken, session).ConfigureAwait(false);
        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<decimal> GetVoucherMaxShareOfCommissionAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Voucher.MaxShareOfCommission", cancellationToken, session).ConfigureAwait(false);
        return decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<int> GetPaymentReviewAlertWorkingHoursAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Payment.ReviewAlertWorkingHours", cancellationToken, session).ConfigureAwait(false);
        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<int> GetSessionIdleMinutesAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Session.IdleMinutes", cancellationToken, session).ConfigureAwait(false);
        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<int> GetLoginMaxFailedAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Login.MaxFailed", cancellationToken, session).ConfigureAwait(false);
        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<int> GetLoginLockoutMinutesAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Login.LockoutMinutes", cancellationToken, session).ConfigureAwait(false);
        return int.Parse(value, CultureInfo.InvariantCulture);
    }

    public async Task<string> GetFujiBankNameAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Fuji.BankName", cancellationToken, session).ConfigureAwait(false);
        return value;
    }

    public async Task<string> GetFujiBankAccountNoAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Fuji.BankAccountNo", cancellationToken, session).ConfigureAwait(false);
        return value;
    }

    public async Task<string> GetFujiBankAccountNameAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("Fuji.BankAccountName", cancellationToken, session).ConfigureAwait(false);
        return value;
    }

    public async Task<TimeOnly> GetWorkingHoursStartAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("WorkingHours.Start", cancellationToken, session).ConfigureAwait(false);
        return TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);
    }

    public async Task<TimeOnly> GetWorkingHoursEndAsync(CancellationToken cancellationToken, DbSession? session = null)
    {
        var value = await GetValueAsync("WorkingHours.End", cancellationToken, session).ConfigureAwait(false);
        return TimeOnly.ParseExact(value, "HH:mm", CultureInfo.InvariantCulture);
    }

}
