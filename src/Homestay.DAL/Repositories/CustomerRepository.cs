using Homestay.DAL.Infrastructure;
using Homestay.DTO.Common;
using Homestay.DTO.Customers;
using Homestay.DTO.Enums;
using Microsoft.Data.SqlClient;

namespace Homestay.DAL.Repositories;

public sealed class CustomerRepository
{
    internal const string Columns = """
        c.Id, c.FullName, c.Phone, c.Email, c.IdNumber, c.Note, c.RowVer,
        CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.Bookings b WHERE b.CustomerId=c.Id) THEN 1 ELSE 0 END AS bit) AS HasBookings
        """;
    internal static CustomerItem Map(SqlDataReader r) => new(r.GetInt32("Id"), r.GetString("FullName"), r.GetString("Phone"), r.GetNullableString("Email"),
        r.GetNullableString("IdNumber"), false, r.GetNullableString("Note"), r.GetBoolean("HasBookings"), r.GetRowVersion("RowVer"));

    public async Task<IReadOnlyList<CustomerItem>> QuickSearchAsync(DbSession db, string text, string? normalizedPhone, CancellationToken ct)
    {
        using var command = db.CreateCommand($"""
            SELECT TOP (5) {Columns} FROM dbo.Customers c
            WHERE (@phone IS NOT NULL AND c.Phone=@phone) OR (@phone IS NULL
                AND (c.FullName COLLATE Vietnamese_CI_AI LIKE @search ESCAPE '~' OR c.Phone LIKE @search ESCAPE '~'))
            ORDER BY c.FullName,c.Id;
            """);
        command.Parameters.AddVarChar("@phone", 16, normalizedPhone);
        command.Parameters.AddNVarChar("@search", 500, StaffReadSql.Like(text));
        return await db.QueryAsync(command, Map, ct).ConfigureAwait(false);
    }

    public async Task<PagedResult<CustomerItem>> SearchAsync(DbSession db, CustomerSearchRequest request, CancellationToken ct)
    {
        const string where = " WHERE (@search=N'' OR c.FullName COLLATE Vietnamese_CI_AI LIKE @search ESCAPE '~' OR c.Phone LIKE @search ESCAPE '~' OR c.Email LIKE @search ESCAPE '~')";
        using var count = db.CreateCommand("SELECT COUNT(*) FROM dbo.Customers c" + where);
        count.Parameters.AddNVarChar("@search", 500, StaffReadSql.Like(request.Page.SearchText));
        var total = (int)(await db.ScalarAsync(count, ct).ConfigureAwait(false))!;
        var column = request.Page.SortColumn switch { "Phone" => "c.Phone", "Email" => "c.Email", "Id" => "c.Id", _ => "c.FullName" };
        using var command = db.CreateCommand($"SELECT {Columns} FROM dbo.Customers c {where} ORDER BY {column} {(request.Page.SortDescending ? "DESC" : "ASC")}, c.Id OFFSET @offset ROWS FETCH NEXT @size ROWS ONLY;");
        command.Parameters.AddNVarChar("@search", 500, StaffReadSql.Like(request.Page.SearchText));
        StaffReadSql.PageParameters(command, request.Page);
        return new() { Items = await db.QueryAsync(command, Map, ct).ConfigureAwait(false), TotalCount = total, PageNumber = request.Page.PageNumber, PageSize = request.Page.PageSize };
    }

    public async Task<CustomerItem?> GetAsync(DbSession db, int id, bool forUpdate, CancellationToken ct)
    {
        using var command = db.CreateCommand($"SELECT {Columns} FROM dbo.Customers c {(forUpdate ? "WITH (UPDLOCK,HOLDLOCK)" : "")} WHERE c.Id=@id;");
        command.Parameters.AddInt32("@id", id);
        return (await db.QueryAsync(command, Map, ct).ConfigureAwait(false)).SingleOrDefault();
    }
    public async Task<CustomerItem?> FindByPhoneAsync(DbSession db, string phone, CancellationToken ct)
    {
        using var command = db.CreateCommand($"SELECT {Columns} FROM dbo.Customers c {(db is UnitOfWork ? "WITH (UPDLOCK,HOLDLOCK)" : "")} WHERE c.Phone=@phone;");
        command.Parameters.AddVarChar("@phone", 16, phone);
        return (await db.QueryAsync(command, Map, ct).ConfigureAwait(false)).SingleOrDefault();
    }
    public async Task<IReadOnlyList<CustomerBookingItem>> RecentBookingsAsync(DbSession db, int customerId, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            SELECT TOP (20) b.Id, b.Code, h.Name AS HomestayName, b.CheckIn, b.CheckOut, b.Status, p.PaymentState, p.PaymentStateName
            FROM dbo.Bookings b JOIN dbo.Homestays h ON h.Id=b.HomestayId JOIN dbo.vw_BookingPaymentState p ON p.BookingId=b.Id
            WHERE b.CustomerId=@id ORDER BY b.CreatedAt DESC, b.Id DESC;
            """);
        command.Parameters.AddInt32("@id", customerId);
        return await db.QueryAsync(command, r => new CustomerBookingItem(r.GetInt32("Id"), r.GetString("Code"), r.GetString("HomestayName"), r.GetDateOnly("CheckIn"),
            r.GetDateOnly("CheckOut"), r.GetEnum<BookingStatus>("Status"), r.GetEnum<PaymentState>("PaymentState"), r.GetString("PaymentStateName")), ct).ConfigureAwait(false);
    }
    public async Task<int> CreateAsync(UnitOfWork db, CreateCustomerRequest request, int employeeId, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            INSERT dbo.Customers (FullName,Phone,Email,IdNumber,Note,CreatedByEmployeeId,CreatedAt)
            VALUES (@name,@phone,@email,@identity,@note,@employee,@now); SELECT CONVERT(int,SCOPE_IDENTITY());
            """);
        AddValues(command, request.FullName, request.Email, request.IdNumber, request.Note, now);
        command.Parameters.AddVarChar("@phone", 16, request.Phone);
        command.Parameters.AddInt32("@employee", employeeId);
        return (int)(await db.ScalarAsync(command, ct).ConfigureAwait(false))!;
    }
    public async Task<int> UpdateAsync(UnitOfWork db, UpdateCustomerRequest request, string? identity, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            UPDATE dbo.Customers SET FullName=@name, Email=@email, IdNumber=@identity, Note=@note, UpdatedAt=@now
            WHERE Id=@id AND RowVer=@version;
            """);
        AddValues(command, request.FullName, request.Email, identity, request.Note, now);
        command.Parameters.AddInt32("@id", request.CustomerId);
        command.Parameters.AddRowVersion("@version", request.RowVer);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }
    public async Task<int> ChangePhoneAsync(UnitOfWork db, ChangeCustomerPhoneRequest request, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("UPDATE dbo.Customers SET Phone=@phone, UpdatedAt=@now WHERE Id=@id AND RowVer=@version;");
        command.Parameters.AddVarChar("@phone", 16, request.Phone);
        command.Parameters.AddInt32("@id", request.CustomerId);
        command.Parameters.AddRowVersion("@version", request.RowVer);
        command.Parameters.AddUtcDateTime("@now", now);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }
    private static void AddValues(SqlCommand command, string name, string? email, string? identity, string? note, DateTime now)
    {
        command.Parameters.AddNVarChar("@name", 100, name);
        command.Parameters.AddNVarChar("@email", 256, email);
        command.Parameters.AddVarChar("@identity", 20, identity);
        command.Parameters.AddNVarChar("@note", 1000, note);
        command.Parameters.AddUtcDateTime("@now", now);
    }
}
