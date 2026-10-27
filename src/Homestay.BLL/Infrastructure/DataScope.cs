using System.Text.RegularExpressions;
using Homestay.DAL.Infrastructure;
using Homestay.DTO.Enums;
using Microsoft.Data.SqlClient;

namespace Homestay.BLL.Infrastructure;

public sealed record ScopeSql(string Condition, IReadOnlyList<SqlParameter> Parameters)
{
    public void AddParametersTo(SqlCommand command)
    {
        // Clone để cùng bộ lọc có thể dùng cho COUNT và truy vấn trang.
        foreach (var parameter in Parameters) command.Parameters.Add((SqlParameter)((ICloneable)parameter).Clone());
    }
}

public sealed class DataScope(ICurrentSession currentSession)
{
    // Preserve the M4.1 OwnerService constructor while sharing its authenticated session with the real write runner.
    internal ICurrentSession CurrentSession => currentSession;

    public ScopeSql BookingFilter(string bookingAlias = "b", string homestayAlias = "h", string parameterPrefix = "scope") =>
        BuildBookingFilter(bookingAlias, homestayAlias, parameterPrefix, financialRead: true);

    private ScopeSql BuildBookingFilter(string bookingAlias, string homestayAlias, string parameterPrefix, bool financialRead)
    {
        foreach (var identifier in new[] { bookingAlias, homestayAlias, parameterPrefix })
            if (!Regex.IsMatch(identifier, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
                throw new ArgumentException("Invalid SQL identifier.");
        var session = currentSession.RequireSession();
        if ((session.Position == Position.Manager && session.HasAllAreas) || (financialRead && session.Position == Position.Accountant))
            return new ScopeSql("1 = 1", []);
        if (session.Position is not (Position.Staff or Position.Manager)) return new ScopeSql("1 = 0", []);
        var parameters = new List<SqlParameter>();
        using var holder = new SqlCommand();
        var employee = "@" + parameterPrefix + "EmployeeId";
        parameters.Add(holder.Parameters.AddInt32(employee, session.EmployeeId));
        var areas = session.AreaIds.Distinct().Select((id, index) =>
        {
            var name = "@" + parameterPrefix + "Area" + index;
            parameters.Add(holder.Parameters.AddInt32(name, id));
            return name;
        }).ToArray();
        var areaSql = areas.Length == 0 ? "1 = 0" : $"{homestayAlias}.AreaId IN ({string.Join(", ", areas)})";
        return new ScopeSql($"({areaSql} OR {bookingAlias}.CreatedByEmployeeId = {employee} OR {bookingAlias}.AssignedEmployeeId = {employee})", parameters.AsReadOnly());
    }

    // Hồ sơ không có ngoại lệ "đơn mình tạo/phụ trách". KT chỉ có đường đọc tài chính được gọi rõ ràng.
    public ScopeSql HomestayFilter(string alias = "h", bool financialRead = false) => AreaFilter(alias, "AreaId", financialRead);
    public ScopeSql AreaFilter(string alias = "a", string column = "Id", bool financialRead = false)
    {
        foreach (var value in new[] { alias, column })
            if (!Regex.IsMatch(value, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)) throw new ArgumentException("Invalid SQL identifier.");
        var session = currentSession.RequireSession();
        if (session.Position == Position.Manager && session.HasAllAreas || financialRead && session.Position == Position.Accountant)
            return new("1=1", []);
        if (session.Position is not (Position.Staff or Position.Manager)) return new("1=0", []);
        using var holder = new SqlCommand();
        var parameters = session.AreaIds.Distinct().Select((id,index) => holder.Parameters.AddInt32("@listingArea"+index,id)).ToArray();
        return new(parameters.Length == 0 ? "1=0" : $"{alias}.{column} IN ({string.Join(",",parameters.Select(p=>p.ParameterName))})", parameters);
    }
    public ScopeSql OwnerFilter()
    {
        var session = currentSession.RequireSession();
        var home = HomestayFilter(financialRead: true);
        if (home.Condition == "1=1") return home;
        using var holder = new SqlCommand();
        var creator = holder.Parameters.AddInt32("@ownerCreator",session.EmployeeId);
        return new($"(EXISTS(SELECT 1 FROM dbo.Homestays h WHERE h.OwnerId=o.Id AND ({home.Condition})) OR (o.Status='Pending' AND o.CreatedByEmployeeId=@ownerCreator))", home.Parameters.Concat(new[] { creator }).ToArray());
    }

    public async Task EnsureBookingWritableAsync(UnitOfWork uow, int bookingId, CancellationToken cancellationToken)
    {
        var session = currentSession.RequireSession();
        if (session.Position == Position.Accountant) throw new ForbiddenException();
        var filter = BuildBookingFilter("b", "h", "scope", financialRead: false);
        using var command = uow.CreateCommand($"SELECT b.Id FROM dbo.Bookings b WITH (UPDLOCK) JOIN dbo.Homestays h ON h.Id=b.HomestayId WHERE b.Id=@bookingId AND {filter.Condition};");
        command.Parameters.AddInt32("@bookingId", bookingId);
        filter.AddParametersTo(command);
        var exists = await uow.ScalarAsync(command, cancellationToken).ConfigureAwait(false);
        if (exists is null) throw new NotFoundException();
    }

    public async Task EnsureHomestayEditableAsync(UnitOfWork uow, int homestayId, CancellationToken cancellationToken)
    {
        var session = currentSession.RequireSession();
        if (session.Position is not (Position.Staff or Position.Manager)) throw new ForbiddenException();
        using var command = uow.CreateCommand("SELECT Status FROM dbo.Homestays WITH (UPDLOCK) WHERE Id=@id;");
        command.Parameters.AddInt32("@id", homestayId);
        var status = await uow.ScalarAsync(command, cancellationToken).ConfigureAwait(false);
        if (status is null) throw new NotFoundException();
        if (session.Position == Position.Staff && (string)status != nameof(HomestayStatus.Draft)) throw new ForbiddenException();
    }
}
