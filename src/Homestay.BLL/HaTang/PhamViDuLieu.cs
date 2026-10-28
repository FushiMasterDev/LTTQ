using System.Text.RegularExpressions;
using Homestay.DAL.Infrastructure;
using Homestay.DTO.Enums;
using Microsoft.Data.SqlClient;

namespace Homestay.BLL.Infrastructure;

/// <summary>Điều kiện SQL kèm tham số để lọc dữ liệu theo phạm vi của người dùng.</summary>
public sealed record ScopeSql(string Condition, IReadOnlyList<SqlParameter> Parameters)
{
    public void AddParametersTo(SqlCommand command)
    {
        // Clone để cùng bộ lọc có thể dùng cho COUNT và truy vấn trang.
        foreach (var parameter in Parameters) command.Parameters.Add((SqlParameter)((ICloneable)parameter).Clone());
    }
}

/// <summary>Giới hạn dữ liệu theo phạm vi người dùng (khu vực phụ trách, đơn mình phụ trách) và kiểm tra có được sửa bản ghi không.</summary>
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
        if (financialRead && session.Position == Position.Accountant) return new("1 = 1", []);
        if (session.Position is not (Position.Staff or Position.Manager)) return new("1 = 0", []);
        using var holder = new SqlCommand();
        var employee = "@" + parameterPrefix + "EmployeeId";
        var parameter = holder.Parameters.AddInt32(employee, session.EmployeeId);
        // M5.1: membership and HasAllAreas are read by SQL for EVERY operation, including open sessions.
        var areaSql = $"EXISTS(SELECT 1 FROM dbo.Employees scopeEmployee WHERE scopeEmployee.Id={employee} AND ((scopeEmployee.Position='Manager' AND scopeEmployee.HasAllAreas=1) OR EXISTS(SELECT 1 FROM dbo.EmployeeAreas scopeArea WHERE scopeArea.EmployeeId=scopeEmployee.Id AND scopeArea.AreaId={homestayAlias}.AreaId)))";
        return new($"({areaSql} OR {bookingAlias}.CreatedByEmployeeId = {employee} OR {bookingAlias}.AssignedEmployeeId = {employee})", [parameter]);
    }

    // Hồ sơ không có ngoại lệ "đơn mình tạo/phụ trách". KT chỉ có đường đọc tài chính được gọi rõ ràng.
    public ScopeSql HomestayFilter(string alias = "h", bool financialRead = false) => AreaFilter(alias, "AreaId", financialRead);
    public ScopeSql AreaFilter(string alias = "a", string column = "Id", bool financialRead = false)
    {
        foreach (var value in new[] { alias, column })
            if (!Regex.IsMatch(value, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)) throw new ArgumentException("Invalid SQL identifier.");
        var session = currentSession.RequireSession();
        if (financialRead && session.Position == Position.Accountant) return new("1=1", []);
        if (session.Position is not (Position.Staff or Position.Manager)) return new("1=0", []);
        using var holder = new SqlCommand();
        var employee = holder.Parameters.AddInt32("@listingEmployee", session.EmployeeId);
        return new($"EXISTS(SELECT 1 FROM dbo.Employees listingEmployee WHERE listingEmployee.Id=@listingEmployee AND ((listingEmployee.Position='Manager' AND listingEmployee.HasAllAreas=1) OR EXISTS(SELECT 1 FROM dbo.EmployeeAreas listingArea WHERE listingArea.EmployeeId=listingEmployee.Id AND listingArea.AreaId={alias}.{column})))", [employee]);
    }
    public ScopeSql OwnerFilter()
    {
        var session = currentSession.RequireSession();
        var home = HomestayFilter(financialRead: true);
        if (home.Condition == "1=1") return home;
        using var holder = new SqlCommand();
        var creator = holder.Parameters.AddInt32("@ownerCreator",session.EmployeeId);
        return new($"(EXISTS(SELECT 1 FROM dbo.Employees ownerManager WHERE ownerManager.Id=@ownerCreator AND ownerManager.Position='Manager' AND ownerManager.HasAllAreas=1) OR EXISTS(SELECT 1 FROM dbo.Homestays h WHERE h.OwnerId=o.Id AND ({home.Condition})) OR (o.Status='Pending' AND o.CreatedByEmployeeId=@ownerCreator))", home.Parameters.Concat(new[] { creator }).ToArray());
    }

    public ScopeSql TaskFilter(string alias = "t")
    {
        if (!Regex.IsMatch(alias, "^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant)) throw new ArgumentException("Invalid SQL identifier.");
        var bookings = BookingFilter("tb", "th", "taskScope");
        var homes = HomestayFilter("taskHome");
        using var holder = new SqlCommand();
        var user = holder.Parameters.AddInt32("@taskCreator", currentSession.RequireSession().UserId);
        // Linked resources must all remain in scope. Unlinked tasks follow creator/recipient areas.
        return new($"""
            (({alias}.BookingId IS NULL OR EXISTS(SELECT 1 FROM dbo.Bookings tb JOIN dbo.Homestays th ON th.Id=tb.HomestayId WHERE tb.Id={alias}.BookingId AND ({bookings.Condition})))
             AND ({alias}.HomestayId IS NULL OR EXISTS(SELECT 1 FROM dbo.Homestays taskHome WHERE taskHome.Id={alias}.HomestayId AND ({homes.Condition})))
             AND ({alias}.BookingId IS NOT NULL OR {alias}.HomestayId IS NOT NULL OR {alias}.CreatedByUserId=@taskCreator
                  OR EXISTS(SELECT 1 FROM dbo.Employees me WHERE me.UserId=@taskCreator AND me.Position='Manager' AND (me.HasAllAreas=1
                      OR EXISTS(SELECT 1 FROM dbo.EmployeeAreas ma JOIN dbo.EmployeeAreas ra ON ra.AreaId=ma.AreaId WHERE ma.EmployeeId=me.Id AND ra.EmployeeId={alias}.AssigneeEmployeeId)))))
            """, bookings.Parameters.Concat(homes.Parameters).Append(user).ToArray());
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
        var scope = HomestayFilter();
        using var command = uow.CreateCommand($"SELECT h.Status FROM dbo.Homestays h WITH (UPDLOCK) WHERE h.Id=@id AND ({scope.Condition});");
        scope.AddParametersTo(command);
        command.Parameters.AddInt32("@id", homestayId);
        var status = await uow.ScalarAsync(command, cancellationToken).ConfigureAwait(false);
        if (status is null) throw new NotFoundException();
        if (session.Position == Position.Staff && (string)status != nameof(HomestayStatus.Draft)) throw new ForbiddenException();
    }
}
