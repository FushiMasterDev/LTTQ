using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.DTO.Staff;

namespace Homestay.BLL.Services;

public sealed partial class StaffService
{
    public async Task<EmployeeAreasDetail> GetAreasAsync(int employeeId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.EmployeeAreaAssign);
        var detail = await GetAsync(employeeId, cancellationToken).ConfigureAwait(false);
        return new(employeeId, detail.Employee.Position, detail.Employee.HasAllAreas, detail.Areas, detail.Employee.RowVer);
    }
    public Task<PagedResult<EmployeeListItem>> ListByAreaAsync(EmployeesByAreaRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.EmployeeAreaAssign);
        ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.Page(request.Page, "EmployeeCode", "FullName");
        if (request.AreaId <= 0) throw new BusinessException("Vui lòng chọn khu vực hợp lệ.");
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync((db, token) => repository.ListByAreaAsync(db, request, token), ct), cancellationToken);
    }
    public Task SetAreasAsync(SetEmployeeAreasRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.EmployeeAreaAssign);
        ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.RowVer(request.RowVer);
        if (request.EmployeeId <= 0 || request.AreaIds is null || request.AreaIds.Count > 1000 || request.AreaIds.Any(id => id <= 0) || request.AreaIds.Distinct().Count() != request.AreaIds.Count)
            throw new BusinessException("Danh sách khu vực không hợp lệ hoặc trùng lặp. Vui lòng chọn lại (tối đa 1000 khu vực).");
        var input = request with { AreaIds = request.AreaIds.ToArray(), RowVer = request.RowVer.ToArray() };
        return writes.ExecuteAsync(async (db, ct) =>
        {
            var old = await LockEmployeeAsync(db, input.EmployeeId, input.RowVer, ct).ConfigureAwait(false);
            if (old.Employee.Position == Position.Accountant && (input.HasAllAreas || input.AreaIds.Count != 0))
                throw new BusinessException("Kế toán không có phạm vi khu vực. Vui lòng bỏ chọn các khu vực.");
            if (input.HasAllAreas && old.Employee.Position != Position.Manager)
                throw new BusinessException("Chỉ Quản lý được giao toàn khu vực. Vui lòng bỏ chọn toàn khu vực.");
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            if (await repository.TouchEmployeeAsync(db, input.EmployeeId, input.RowVer, now, ct).ConfigureAwait(false) == 0) throw new ConcurrencyException();
            try { await repository.SetAreasAsync(db, input, currentSession.RequireSession().EmployeeId, now, ct).ConfigureAwait(false); }
            catch (InvalidEmployeeAreaException) { throw new BusinessException("Khu vực không tồn tại hoặc đã ngừng hoạt động. Vui lòng tải lại danh sách khu vực."); }
            await audit.WriteAsync(db, new(currentSession.RequireSession().UserId, "Staff.SetAreas", "Employees", input.EmployeeId.ToString(),
                new { old.Employee.HasAllAreas, AreaIds = old.Areas.Select(a => a.Id).ToArray() }, new { input.HasAllAreas, input.AreaIds }, null, machineName), ct).ConfigureAwait(false);
        }, cancellationToken);
    }
    public Task ChangePositionAsync(ChangePositionRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.EmployeeManage);
        ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.RowVer(request.RowVer);
        if (!Enum.IsDefined(request.Position)) throw new BusinessException("Chức vụ không hợp lệ. Vui lòng chọn lại.");
        if (request.EmployeeId == currentSession.RequireSession().EmployeeId)
            throw new BusinessException("Không được tự đổi chức vụ của chính mình. Vui lòng nhờ Quản lý khác.");
        return ChangeEmploymentCoreAsync(request.EmployeeId, request.RowVer.ToArray(), request.Position, null, ListingWriteRules.Reason(request.Reason), cancellationToken);
    }
    public Task TerminateEmploymentAsync(TerminateEmploymentRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.EmployeeManage);
        ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.RowVer(request.RowVer);
        if (request.Status is not (EmploymentStatus.Resigned or EmploymentStatus.Suspended))
            throw new BusinessException("Vui lòng chọn nghỉ việc hoặc tạm nghỉ.");
        if (request.EmployeeId == currentSession.RequireSession().EmployeeId)
            throw new BusinessException("Không được tự cho nghỉ việc. Vui lòng nhờ Quản lý khác.");
        return ChangeEmploymentCoreAsync(request.EmployeeId, request.RowVer.ToArray(), null, request.Status, ListingWriteRules.Reason(request.Reason), cancellationToken);
    }
    private Task ChangeEmploymentCoreAsync(int employeeId, byte[] version, Position? position, EmploymentStatus? status, string reason, CancellationToken cancellationToken) =>
        writes.ExecuteSerializedAsync("Managers", async (db, ct) =>
        {
            var old = await LockEmployeeAsync(db, employeeId, version, ct).ConfigureAwait(false);
            if (old.Employee.EmploymentStatus != EmploymentStatus.Active) throw new BusinessException("Nhân viên không còn đang làm việc. Vui lòng tải lại danh sách.");
            var nextPosition = position ?? old.Employee.Position;
            var nextStatus = status ?? old.Employee.EmploymentStatus;
            if (old.Employee.Position == Position.Manager && (nextPosition != Position.Manager || nextStatus != EmploymentStatus.Active) &&
                await repository.CountOtherActiveManagersAsync(db, old.Employee.UserId, ct).ConfigureAwait(false) < 1)
                throw new BusinessException("Phải còn ít nhất một Quản lý đang hoạt động. Vui lòng bổ sung Quản lý trước khi thay đổi.");
            if (nextPosition == old.Employee.Position && nextStatus == old.Employee.EmploymentStatus) throw new BusinessException("Chức vụ không thay đổi. Vui lòng chọn chức vụ khác.");
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            if (await repository.TouchEmployeeAsync(db, employeeId, version, now, ct).ConfigureAwait(false) == 0) throw new ConcurrencyException();
            await repository.ChangeEmploymentAsync(db, employeeId, nextPosition, nextStatus, now, ct).ConfigureAwait(false);
            await accounts.EndSessionsAsync(db, old.Employee.UserId, null, SessionEndReason.AccountChanged, now, ct).ConfigureAwait(false);
            // Accountant cannot operate bookings. Relinquish open work on this role change as on termination.
            if (nextStatus != EmploymentStatus.Active || nextPosition == Position.Accountant)
                await ReleaseWorkAsync(db, employeeId, now, reason, ct).ConfigureAwait(false);
            await audit.WriteAsync(db, new(currentSession.RequireSession().UserId, status is null ? "Staff.ChangePosition" : "Staff.Terminate", "Employees", employeeId.ToString(),
                new { old.Employee.Position, old.Employee.EmploymentStatus, old.Employee.HasAllAreas, AreaIds = old.Areas.Select(a => a.Id).ToArray() },
                new { Position = nextPosition, EmploymentStatus = nextStatus, HasAllAreas = nextPosition == Position.Manager && old.Employee.HasAllAreas,
                    AreaIds = nextPosition == Position.Accountant ? Array.Empty<int>() : old.Areas.Select(a => a.Id).ToArray() }, reason, machineName), ct).ConfigureAwait(false);
        }, cancellationToken);

    private async Task<EmployeeDetail> LockEmployeeAsync(UnitOfWork db, int id, byte[] version, CancellationToken ct)
    {
        var detail = await repository.GetAsync(db, id, ct).ConfigureAwait(false) ?? throw new NotFoundException();
        _ = await accounts.FindByUserIdAsync(db, detail.Employee.UserId, ct).ConfigureAwait(false) ?? throw new NotFoundException();
        detail = await repository.GetAsync(db, id, ct).ConfigureAwait(false) ?? throw new NotFoundException();
        if (!detail.Employee.RowVer.AsSpan().SequenceEqual(version)) throw new ConcurrencyException();
        return detail;
    }
    private async Task ReleaseWorkAsync(UnitOfWork db, int employeeId, DateTime now, string reason, CancellationToken ct)
    {
        var bookings = new BookingRepository(); var notifications = new NotificationRepository();
        foreach (var booking in await bookings.ListEmployeeOpenBookingsAsync(db, employeeId, ct).ConfigureAwait(false))
        {
            if (await bookings.AssignAsync(db, booking.Id, (int?)null, booking.RowVer, false, now, ct).ConfigureAwait(false) == 0) throw new ConcurrencyException();
            await audit.WriteAsync(db, new(currentSession.RequireSession().UserId, "Booking.ReleaseAssignee", "Bookings", booking.Id.ToString(),
                new { AssignedEmployeeId = employeeId }, new { AssignedEmployeeId = (int?)null }, reason, machineName), ct).ConfigureAwait(false);
            await notifications.NotifyScopedManagersAsync(db, booking.Id, $"Đơn {booking.Code} cần giao lại do nhân viên thay đổi công tác.", now, ct).ConfigureAwait(false);
        }
        var tasks = new TaskRepository();
        foreach (var task in await tasks.ListEmployeeOpenTasksAsync(db, employeeId, now, ct).ConfigureAwait(false))
        {
            if (await tasks.SetStatusAsync(db, task.Id, task.RowVer, WorkTaskStatus.Cancelled, null, now, ct).ConfigureAwait(false) == 0) throw new ConcurrencyException();
            await audit.WriteAsync(db, new(currentSession.RequireSession().UserId, "Task.CancelEmployment", "WorkTasks", task.Id.ToString(),
                new { task.Status }, new { Status = WorkTaskStatus.Cancelled }, reason, machineName), ct).ConfigureAwait(false);
            await notifications.NotifyTaskAsync(db, task.CreatedByUserId, $"Việc #{task.Id} đã hủy do nhân viên thay đổi công tác; vui lòng giao lại.", task.Id, now, ct).ConfigureAwait(false);
            await notifications.NotifyTaskEmployeeAsync(db, employeeId, $"Việc #{task.Id} đã hủy do thay đổi công tác.", task.Id, now, ct).ConfigureAwait(false);
        }
    }
}
