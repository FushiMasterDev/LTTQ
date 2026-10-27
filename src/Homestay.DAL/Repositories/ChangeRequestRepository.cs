using Homestay.DAL.Infrastructure;
using Homestay.DTO.ChangeRequests;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Microsoft.Data.SqlClient;

namespace Homestay.DAL.Repositories;

public sealed class ChangeRequestRepository
{
    private const string From = """
         FROM dbo.BookingChangeRequests cr JOIN dbo.Bookings b ON b.Id=cr.BookingId JOIN dbo.Homestays h ON h.Id=b.HomestayId
         JOIN dbo.Customers c ON c.Id=b.CustomerId JOIN dbo.Users ru ON ru.Id=cr.RequestedByUserId
         LEFT JOIN dbo.Employees re ON re.UserId=ru.Id LEFT JOIN dbo.Users du ON du.Id=cr.DecidedByUserId
         LEFT JOIN dbo.Employees de ON de.UserId=du.Id
        """;
    private const string Columns = """
        cr.Id,cr.BookingId,b.Code AS BookingCode,c.FullName AS CustomerName,h.Name AS HomestayName,cr.Status,cr.Reason,
        cr.QuotedRefundAmount,cr.RequestedByUserId,COALESCE(re.FullName,ru.Username) AS RequestedByName,cr.RequestedAt,
        cr.DecidedByUserId,COALESCE(de.FullName,du.Username) AS DecidedByName,cr.DecidedAt,cr.DecisionNote,cr.RowVer
        """;
    private static CancelRequestItem Map(SqlDataReader r, int userId, bool canReview) => new()
    {
        Id = r.GetInt32("Id"), BookingId = r.GetInt32("BookingId"), BookingCode = r.GetString("BookingCode"),
        CustomerName = r.GetString("CustomerName"), HomestayName = r.GetString("HomestayName"), Status = r.GetEnum<ChangeRequestStatus>("Status"),
        Reason = r.GetString("Reason"), QuotedRefundAmount = r.IsDBNull(r.GetOrdinal("QuotedRefundAmount")) ? null : r.GetDecimal("QuotedRefundAmount"),
        RequestedByUserId = r.GetInt32("RequestedByUserId"), RequestedByName = r.GetString("RequestedByName"), RequestedAtUtc = r.GetUtcDateTime("RequestedAt"),
        DecidedByUserId = r.GetNullableInt32("DecidedByUserId"), DecidedByName = r.GetNullableString("DecidedByName"),
        DecidedAtUtc = r.GetNullableUtcDateTime("DecidedAt"), DecisionNote = r.GetNullableString("DecisionNote"),
        IsRequestedByCurrentUser = r.GetInt32("RequestedByUserId") == userId,
        CanReview = canReview && r.GetInt32("RequestedByUserId") != userId && r.GetEnum<ChangeRequestStatus>("Status") == ChangeRequestStatus.Pending,
        RowVer = r.GetRowVersion("RowVer")
    };
    public async Task<PagedResult<CancelRequestItem>> ListAsync(DbSession db, ChangeRequestSearchRequest request, string condition,
        Action<SqlCommand> addScope, int userId, bool canReview, CancellationToken ct)
    {
        var where = $"""
             WHERE cr.RequestType='Cancel' AND ({condition}) AND (@status IS NULL OR cr.Status=@status)
             AND (@booking IS NULL OR cr.BookingId=@booking) AND (@search=N'' OR b.Code LIKE @search ESCAPE '~'
                 OR c.FullName COLLATE Vietnamese_CI_AI LIKE @search ESCAPE '~' OR h.Name COLLATE Vietnamese_CI_AI LIKE @search ESCAPE '~')
            """;
        void Parameters(SqlCommand command)
        {
            addScope(command); command.Parameters.AddEnum("@status", 20, request.Status); command.Parameters.AddInt32("@booking", request.BookingId);
            command.Parameters.AddNVarChar("@search", 500, StaffReadSql.Like(request.Page.SearchText));
        }
        using var count = db.CreateCommand("SELECT COUNT(*)" + From + where); Parameters(count);
        var total = (int)(await db.ScalarAsync(count, ct).ConfigureAwait(false))!;
        var sort = request.Page.SortColumn switch { "BookingCode" => "b.Code", "Status" => "cr.Status", _ => "cr.RequestedAt" };
        using var command = db.CreateCommand($"SELECT {Columns}{From}{where} ORDER BY {sort} {(request.Page.SortDescending ? "DESC" : "ASC")},cr.Id OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY;");
        Parameters(command); StaffReadSql.PageParameters(command, request.Page);
        return new() { TotalCount = total, PageNumber = request.Page.PageNumber, PageSize = request.Page.PageSize,
            Items = await db.QueryAsync(command, r => Map(r, userId, canReview), ct).ConfigureAwait(false) };
    }
    public async Task<CancelRequestItem?> GetAsync(DbSession db, int id, string condition, Action<SqlCommand> addScope,
        int userId, bool canReview, CancellationToken ct)
    {
        using var command = db.CreateCommand($"SELECT {Columns}{From} WHERE cr.Id=@id AND cr.RequestType='Cancel' AND ({condition});");
        command.Parameters.AddInt32("@id", id); addScope(command);
        return (await db.QueryAsync(command, r => Map(r, userId, canReview), ct).ConfigureAwait(false)).SingleOrDefault();
    }
}
