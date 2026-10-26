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
