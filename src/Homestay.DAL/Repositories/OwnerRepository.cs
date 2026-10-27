using Homestay.DAL.Infrastructure;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.DTO.Owners;
using Microsoft.Data.SqlClient;
namespace Homestay.DAL.Repositories;

public sealed partial class OwnerRepository
{
    // Hash the unmasked complete row; no bank/identity values are embedded in the token.
    private const string Columns = "o.*, HASHBYTES('SHA2_256',(SELECT o.* FOR JSON PATH,INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER)) AS RowVer";
    private static OwnerItem Map(SqlDataReader r) => new()
    {
        Id = r.GetInt32("Id"),
        FullName = r.GetString("FullName"),
        Phone = r.GetString("Phone"),
        Email = r.GetNullableString("Email"),
        Address = r.GetNullableString("Address"),
        IdNumber = r.GetNullableString("IdNumber"),
        BankName = r.GetNullableString("BankName"),
        BankAccountNo = r.GetNullableString("BankAccountNo"),
        BankAccountName = r.GetNullableString("BankAccountName"),
        Status = r.GetEnum<OwnerStatus>("Status"),
        ApprovedByEmployeeId = r.GetNullableInt32("ApprovedByEmployeeId"),
        ApprovedAtUtc = r.GetNullableUtcDateTime("ApprovedAt"),
        CreatedByEmployeeId = r.GetNullableInt32("CreatedByEmployeeId"),
        Note = r.GetNullableString("Note"),
        RowVer = r.GetRowVersion("RowVer")
    };
    public Task<PagedResult<OwnerItem>> ListAsync(DbSession db, OwnerSearchRequest request, string scope, Action<SqlCommand> addScope, CancellationToken ct)
    {
        var where = $" WHERE ({scope}) AND (@status IS NULL OR o.Status=@status) AND (@search=N'' OR o.FullName COLLATE Vietnamese_CI_AI LIKE @search ESCAPE '~' OR o.Phone LIKE @search ESCAPE '~')";
        void Parameters(SqlCommand c) { addScope(c); c.Parameters.AddEnum("@status", 20, request.Status); c.Parameters.AddNVarChar("@search", 500, StaffReadSql.Like(request.Page.SearchText)); }
        var sort = request.Page.SortColumn switch { "Id" => "o.Id", "Phone" => "o.Phone", "Status" => "o.Status", _ => "o.FullName" };
        return ListingReadSql.PageAsync(db, request.Page, Columns, "FROM dbo.Owners o", where, sort, "o.Id", Parameters, Map, ct);
    }
    public async Task<OwnerItem?> GetAsync(DbSession db, int id, string scope, Action<SqlCommand> addScope, CancellationToken ct)
    {
        using var c = db.CreateCommand($"SELECT {Columns} FROM dbo.Owners o WHERE o.Id=@id AND ({scope});");
        addScope(c); c.Parameters.AddInt32("@id", id);
        return (await db.QueryAsync(c, Map, ct).ConfigureAwait(false)).SingleOrDefault();
    }
    public Task<IReadOnlyList<Lookup>> LookupAsync(DbSession db, string scope, Action<SqlCommand> addScope, CancellationToken ct) =>
        ListingReadSql.LookupAsync(db, $"SELECT o.Id,o.FullName AS Name,CAST(CASE WHEN o.Status='Approved' THEN 1 ELSE 0 END AS bit) AS IsActive FROM dbo.Owners o WHERE ({scope}) AND o.Status IN ('Pending','Approved') ORDER BY o.FullName,o.Id;", addScope, ct);
}
