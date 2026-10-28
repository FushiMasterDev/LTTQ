using Homestay.DAL.Infrastructure;
using Microsoft.Data.SqlClient;

namespace Homestay.DAL.Repositories;

/// <summary>Các đoạn SQL dùng chung khi đọc nhân viên: tìm kiếm LIKE, chuẩn hóa mã, phân trang.</summary>
internal static class StaffReadSql
{
    internal static string Like(string? value) => string.IsNullOrWhiteSpace(value) ? "" : "%" + value.Trim().Replace("~", "~~").Replace("%", "~%").Replace("_", "~_").Replace("[", "~[") + "%";
    internal static string NormalizeCode(string? code) => (code ?? "").Trim().Replace("-", "").ToUpperInvariant();
    // Dùng cùng predicate cho tra và lịch. Không UPDATE trong luồng đọc.
    internal const string Occupies = """
        (rn.RoomBlockId IS NOT NULL
        OR (rn.BookingId IS NOT NULL AND b.Status IN ('AwaitingPayment','PaymentSubmitted','Deposited','Confirmed','CheckedIn','Completed')
            AND NOT (b.Status='AwaitingPayment' AND b.HoldExpiresAt < @now
                AND NOT EXISTS (SELECT 1 FROM dbo.Payments p WHERE p.BookingId=b.Id AND p.Type='Deposit' AND p.Status='Pending')))
        OR (rn.ChangeRequestId IS NOT NULL AND cr.Status IN ('Pending','AwaitingPayment')
            AND NOT (cr.Status='AwaitingPayment' AND cr.PaymentDeadlineAt < @now
                AND NOT EXISTS (SELECT 1 FROM dbo.Payments p WHERE p.ChangeRequestId=cr.Id AND p.Type='AdditionalDeposit' AND p.Status='Pending'))))
        """;
    internal static async Task<List<T>> ReadSetAsync<T>(SqlDataReader reader, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        var result = new List<T>();
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) result.Add(map(reader));
        return result;
    }
    internal static void PageParameters(SqlCommand command, Homestay.DTO.Common.PageRequest page)
    {
        command.Parameters.AddInt32("@offset", checked((page.PageNumber - 1) * page.PageSize));
        command.Parameters.AddInt32("@size", page.PageSize);
    }
}
