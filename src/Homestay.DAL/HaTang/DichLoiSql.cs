using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace Homestay.DAL.Infrastructure;

/// <summary>Đọc lỗi SqlException (số lỗi, tên ràng buộc) và đổi thành các ngoại lệ CSDL riêng của dự án.</summary>
public static class SqlErrorTranslator
{
    public static DbConstraintViolationException? ParseConstraint(int errorNumber, string message, Exception? innerException = null)
    {
        if (errorNumber is not (2627 or 2601 or 547)) return null;
        var pattern = errorNumber == 2601 ? "unique index ['\"](?<name>[^'\"]+)['\"]" : "constraint ['\"](?<name>[^'\"]+)['\"]";
        var match = Regex.Match(message, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var kind = errorNumber switch
        {
            2601 => DbConstraintKind.Unique,
            2627 => message.Contains("PRIMARY KEY", StringComparison.OrdinalIgnoreCase) ? DbConstraintKind.PrimaryKey : DbConstraintKind.Unique,
            _ => message.Contains("CHECK constraint", StringComparison.OrdinalIgnoreCase) ? DbConstraintKind.Check : DbConstraintKind.ForeignKey
        };
        return new(match.Success ? match.Groups["name"].Value : null, kind, errorNumber, innerException);
    }

    public static Exception Translate(SqlException exception)
    {
        // Một batch có thể có cả lỗi ràng buộc và thông báo kết thúc câu lệnh.
        foreach (SqlError error in exception.Errors)
        {
            if (ParseConstraint(error.Number, error.Message, exception) is { } constraint) return constraint;
            if (error.Number == 1205) return new DbDeadlockException(exception);
            if (error.Number is -2 or -1 or 2 or 20 or 53 or 64 or 121 or 233 or 258 or 10053 or 10054 or 10060 or 11001 or 4060 or 40197 or 40501 or 40613)
                return new DbUnavailableException(error.Number, exception);
        }
        return exception;
    }

    public static async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        try { return await operation(cancellationToken).ConfigureAwait(false); }
        catch (SqlException exception)
        {
            var translated = Translate(exception);
            if (ReferenceEquals(translated, exception)) throw;
            throw translated;
        }
    }
}
