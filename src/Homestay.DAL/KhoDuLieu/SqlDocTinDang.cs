using Homestay.DAL.Infrastructure;
using Homestay.DTO.Common;
using Microsoft.Data.SqlClient;
namespace Homestay.DAL.Repositories;

/// <summary>Truy vấn dùng chung cho tin đăng: lấy một trang dữ liệu (đếm tổng + lấy trang) và danh sách tra cứu id/tên.</summary>
internal static class ListingReadSql
{
    internal static async Task<PagedResult<T>> PageAsync<T>(DbSession db, PageRequest page, string columns, string from,
        string where, string sort, string idColumn, Action<SqlCommand> parameters, Func<SqlDataReader, T> map, CancellationToken ct)
    {
        using var count = db.CreateCommand("SELECT COUNT(*) " + from + where);
        parameters(count);
        var total = (int)(await db.ScalarAsync(count, ct).ConfigureAwait(false))!;
        var tie = sort == idColumn ? "" : ", " + idColumn;
        using var command = db.CreateCommand($"SELECT {columns} {from}{where} ORDER BY {sort} {(page.SortDescending ? "DESC" : "ASC")}{tie} OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY;");
        parameters(command); StaffReadSql.PageParameters(command, page);
        return new() { Items = await db.QueryAsync(command, map, ct).ConfigureAwait(false), TotalCount = total, PageNumber = page.PageNumber, PageSize = page.PageSize };
    }
    internal static async Task<IReadOnlyList<Lookup>> LookupAsync(DbSession db, string sql, Action<SqlCommand> parameters, CancellationToken ct)
    {
        using var command = db.CreateCommand(sql); parameters(command);
        return await db.QueryAsync(command, r => new Lookup { Id = r.GetInt32("Id"), Name = r.GetString("Name"), IsActive = r.GetBoolean("IsActive") }, ct).ConfigureAwait(false);
    }
}
