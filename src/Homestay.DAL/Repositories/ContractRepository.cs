using Homestay.DAL.Infrastructure;
using Homestay.DTO.Common;
using Homestay.DTO.Contracts;
using Homestay.DTO.Enums;
using Microsoft.Data.SqlClient;
namespace Homestay.DAL.Repositories;

public sealed partial class ContractRepository
{
    private const string Columns = "c.*,h.Name AS HomestayName,h.RowVer";
    private const string From = "FROM dbo.Contracts c JOIN dbo.Homestays h ON h.Id=c.HomestayId ";
    private static ContractItem Map(SqlDataReader r) => new(r.GetInt32("Id"), r.GetInt32("HomestayId"), r.GetString("HomestayName"), r.GetString("ContractNo"),
        r.GetDateOnly("StartDate"), r.GetDateOnly("EndDate"), r.GetDecimal("CommissionRate"), r.GetEnum<ContractStatus>("Status"), r.GetInt32("SignedByEmployeeId"),
        r.GetNullableUtcDateTime("TerminatedAt"), r.GetNullableString("TerminateReason"), r.GetRowVersion("RowVer"));
    public Task<PagedResult<ContractItem>> ListAsync(DbSession db, ContractSearchRequest request, string scope, Action<SqlCommand> addScope, CancellationToken ct)
    {
        var where = $" WHERE ({scope}) AND (@home IS NULL OR c.HomestayId=@home) AND (@status IS NULL OR c.Status=@status) AND (@search=N'' OR c.ContractNo LIKE @search ESCAPE '~' OR h.Name COLLATE Vietnamese_CI_AI LIKE @search ESCAPE '~')";
        void Parameters(SqlCommand c) { addScope(c); c.Parameters.AddInt32("@home", request.HomestayId); c.Parameters.AddEnum("@status", 20, request.Status); c.Parameters.AddNVarChar("@search", 500, StaffReadSql.Like(request.Page.SearchText)); }
        var sort = request.Page.SortColumn switch { "Id" => "c.Id", "HomestayName" => "h.Name", "StartDate" => "c.StartDate", "EndDate" => "c.EndDate", "Status" => "c.Status", _ => "c.ContractNo" };
        return ListingReadSql.PageAsync(db, request.Page, Columns, From, where, sort, "c.Id", Parameters, Map, ct);
    }
    public async Task<ContractItem?> GetAsync(DbSession db, int id, string scope, Action<SqlCommand> addScope, CancellationToken ct)
    {
        using var c = db.CreateCommand($"SELECT {Columns} {From} WHERE c.Id=@id AND ({scope});"); addScope(c); c.Parameters.AddInt32("@id", id);
        return (await db.QueryAsync(c, Map, ct).ConfigureAwait(false)).SingleOrDefault();
    }
    public async Task<IReadOnlyList<ContractItem>> ListForHomestayAsync(DbSession db, int home, CancellationToken ct)
    {
        using var c = db.CreateCommand($"SELECT {Columns} {From} WHERE c.HomestayId=@id ORDER BY c.StartDate DESC,c.Id DESC;"); c.Parameters.AddInt32("@id", home);
        return await db.QueryAsync(c, Map, ct).ConfigureAwait(false);
    }
}
