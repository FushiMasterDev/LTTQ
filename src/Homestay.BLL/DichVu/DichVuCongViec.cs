using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.DTO.Tasks;
using Microsoft.Data.SqlClient;

namespace Homestay.BLL.Services;

/// <summary>Dịch vụ giao việc nội bộ: xem việc được giao/của mình, tạo, sửa, bắt đầu, hoàn thành và hủy công việc.</summary>
public sealed class TaskService(Authorizer authorizer, ICurrentSession currentSession, DataScope dataScope, ConnectionFactory factory,
    WriteOperationRunner writes, TaskRepository repository, NotificationRepository notifications, DbClock clock, AuditWriter audit, string machineName)
{
    public Task<PagedResult<WorkTaskItem>> ListAssignedAsync(WorkTaskSearchRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.WorkTaskAssign);
        ValidateSearch(request);
        return ListCoreAsync(request, dataScope.TaskFilter(), cancellationToken);
    }
    public Task<PagedResult<WorkTaskItem>> ListMineAsync(WorkTaskSearchRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.OwnWorkTaskManage);
        ValidateSearch(request);
        return ListCoreAsync(request with { AssigneeEmployeeId = currentSession.RequireSession().EmployeeId }, OwnScope(), cancellationToken);
    }
    public Task<WorkTaskItem> GetAsync(int taskId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.OwnWorkTaskManage);
        var scope = ReadScope();
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
            await repository.GetAsync(db, taskId, scope.Condition, scope.AddParametersTo, await clock.GetUtcNowAsync(db, token).ConfigureAwait(false), token).ConfigureAwait(false)
                ?? throw new NotFoundException(), ct), cancellationToken);
    }
    public Task<IReadOnlyList<Lookup>> ListAssigneesAsync(int? bookingId = null, int? homestayId = null, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.WorkTaskAssign);
        ValidateLinks(bookingId, homestayId);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        {
            await CheckLinksAsync(db, bookingId, homestayId, token).ConfigureAwait(false);
            return await repository.ListAssigneesAsync(db, bookingId, homestayId, currentSession.RequireSession().EmployeeId, null, token).ConfigureAwait(false);
        }, ct), cancellationToken);
    }
    public Task<WorkTaskItem> CreateAsync(WorkTaskValues request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.WorkTaskAssign);
        var values = WorkTaskRules.Normalize(request);
        return writes.ExecuteAsync(async (db, ct) =>
        {
            await ValidateRecipientAsync(db, values, ct).ConfigureAwait(false);
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            var id = await repository.CreateAsync(db, values, WorkTaskRules.DueUtc(values.DueAtVietnam), currentSession.RequireSession().UserId, now, ct).ConfigureAwait(false);
            await AuditAsync(db, "Task.Create", id, null, values, null, ct).ConfigureAwait(false);
            await notifications.NotifyTaskEmployeeAsync(db, values.AssigneeEmployeeId, $"Bạn được giao việc #{id}: {values.Title}", id, now, ct).ConfigureAwait(false);
            return await ReadWrittenAsync(db, id, now, ct).ConfigureAwait(false);
        }, cancellationToken);
    }
    public Task<WorkTaskItem> UpdateAsync(UpdateWorkTaskRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.WorkTaskAssign);
        ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.RowVer(request.RowVer);
        var values = WorkTaskRules.Normalize(request.Values); var version = request.RowVer.ToArray();
        return writes.ExecuteAsync(async (db, ct) =>
        {
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            var old = await LockedAsync(db, request.TaskId, dataScope.TaskFilter(), version, now, ct).ConfigureAwait(false);
            _ = WorkTaskRules.Next(old.Status, WorkTaskOperation.Edit, false, false, true);
            await ValidateRecipientAsync(db, values, ct).ConfigureAwait(false);
            if (await repository.UpdateAsync(db, old.Id, values, WorkTaskRules.DueUtc(values.DueAtVietnam), version, now, ct).ConfigureAwait(false) == 0) throw new ConcurrencyException();
            await AuditAsync(db, "Task.Update", old.Id, old, values, null, ct).ConfigureAwait(false);
            await notifications.NotifyTaskEmployeeAsync(db, values.AssigneeEmployeeId, $"Việc #{old.Id} đã được cập nhật: {values.Title}", old.Id, now, ct).ConfigureAwait(false);
            if (values.AssigneeEmployeeId != old.AssigneeEmployeeId)
                await notifications.NotifyTaskEmployeeAsync(db, old.AssigneeEmployeeId, $"Việc #{old.Id} đã được giao cho người khác.", old.Id, now, ct).ConfigureAwait(false);
            return await ReadWrittenAsync(db, old.Id, now, ct).ConfigureAwait(false);
        }, cancellationToken);
    }
    public Task<WorkTaskItem> StartAsync(WorkTaskActionRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.OwnWorkTaskManage);
        ArgumentNullException.ThrowIfNull(request);
        return ApplyAsync(request.TaskId, request.RowVer, WorkTaskOperation.Start, null, cancellationToken);
    }
    public Task<WorkTaskItem> CompleteAsync(CompleteWorkTaskRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.OwnWorkTaskManage);
        ArgumentNullException.ThrowIfNull(request);
        return ApplyAsync(request.TaskId, request.RowVer, WorkTaskOperation.Complete, WorkTaskRules.Result(request.ResultNote), cancellationToken);
    }
    public Task<WorkTaskItem> CancelAsync(CancelWorkTaskRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.WorkTaskAssign);
        ArgumentNullException.ThrowIfNull(request);
        return ApplyAsync(request.TaskId, request.RowVer, WorkTaskOperation.Cancel, ListingWriteRules.Reason(request.Reason), cancellationToken);
    }
    private Task<WorkTaskItem> ApplyAsync(int id, byte[] rowVer, WorkTaskOperation operation, string? note, CancellationToken cancellationToken)
    {
        StaffInputRules.RowVer(rowVer); var version = rowVer.ToArray();
        return writes.ExecuteAsync(async (db, ct) =>
        {
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            var old = await LockedAsync(db, id, operation == WorkTaskOperation.Cancel ? CreatorScope() : OwnScope(), version, now, ct).ConfigureAwait(false);
            var session = currentSession.RequireSession();
            if (operation == WorkTaskOperation.Cancel && old.CreatedByUserId != session.UserId) throw new NotFoundException();
            var next = WorkTaskRules.Next(old.Status, operation, old.AssigneeEmployeeId == session.EmployeeId, old.CreatedByUserId == session.UserId,
                session.Position == Position.Manager, old.StartedAtUtc is not null);
            if (await repository.SetStatusAsync(db, id, version, next, operation == WorkTaskOperation.Complete ? note : null, now, ct).ConfigureAwait(false) == 0) throw new ConcurrencyException();
            var action = operation switch { WorkTaskOperation.Start => "Task.Start", WorkTaskOperation.Complete => "Task.Complete", _ => "Task.Cancel" };
            await AuditAsync(db, action, id, new { old.Status, old.StartedAtUtc, old.ResultNote }, new { Status = next, ResultNote = operation == WorkTaskOperation.Complete ? note : null }, note, ct).ConfigureAwait(false);
            if (operation == WorkTaskOperation.Complete)
                await notifications.NotifyTaskAsync(db, old.CreatedByUserId, $"Việc #{id} đã hoàn thành: {old.Title}", id, now, ct).ConfigureAwait(false);
            if (operation == WorkTaskOperation.Cancel)
                await notifications.NotifyTaskEmployeeAsync(db, old.AssigneeEmployeeId, $"Việc #{id} đã hủy: {old.Title}", id, now, ct).ConfigureAwait(false);
            return await ReadWrittenAsync(db, id, now, ct).ConfigureAwait(false);
        }, cancellationToken);
    }
    private async Task<WorkTaskItem> LockedAsync(UnitOfWork db, int id, ScopeSql scope, byte[] version, DateTime now, CancellationToken ct)
    {
        var old = await repository.GetAsync(db, id, scope.Condition, scope.AddParametersTo, now, ct).ConfigureAwait(false) ?? throw new NotFoundException();
        if (!old.RowVer.AsSpan().SequenceEqual(version)) throw new ConcurrencyException();
        return old;
    }
    private async Task ValidateRecipientAsync(UnitOfWork db, WorkTaskValues values, CancellationToken ct)
    {
        await CheckLinksAsync(db, values.BookingId, values.HomestayId, ct).ConfigureAwait(false);
        if ((await repository.ListAssigneesAsync(db, values.BookingId, values.HomestayId, currentSession.RequireSession().EmployeeId, values.AssigneeEmployeeId, ct).ConfigureAwait(false)).Count != 1)
            throw new BusinessException("Người nhận không còn hoạt động hoặc không đủ khu vực của việc. Vui lòng chọn lại.");
    }
    private async Task CheckLinksAsync(DbSession db, int? bookingId, int? homestayId, CancellationToken ct)
    {
        if (bookingId is { } booking)
        {
            var scope = dataScope.BookingFilter();
            var actualHome = await repository.GetLinkedBookingHomeAsync(db, booking, scope.Condition, scope.AddParametersTo, ct).ConfigureAwait(false) ?? throw new NotFoundException();
            if (homestayId is { } home && actualHome != home) throw new BusinessException("Đơn và homestay phải cùng một hồ sơ. Vui lòng chọn lại.");
        }
        if (homestayId is { } homestay)
        {
            var scope = dataScope.HomestayFilter();
            if (!await repository.LinkedHomeExistsAsync(db, homestay, scope.Condition, scope.AddParametersTo, ct).ConfigureAwait(false)) throw new NotFoundException();
        }
    }
    private async Task<WorkTaskItem> ReadWrittenAsync(DbSession db, int id, DateTime now, CancellationToken ct) =>
        await repository.GetAsync(db, id, "1=1", _ => { }, now, ct).ConfigureAwait(false) ?? throw new NotFoundException();
    private Task AuditAsync(UnitOfWork db, string action, int id, object? old, object? next, string? note, CancellationToken ct) =>
        audit.WriteAsync(db, new(currentSession.RequireSession().UserId, action, "WorkTasks", id.ToString(), old, next, note, machineName), ct);
    private Task<PagedResult<WorkTaskItem>> ListCoreAsync(WorkTaskSearchRequest request, ScopeSql scope, CancellationToken cancellationToken) =>
        BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
            await repository.ListAsync(db, request, scope.Condition, scope.AddParametersTo, await clock.GetUtcNowAsync(db, token).ConfigureAwait(false), token).ConfigureAwait(false), ct), cancellationToken);
    private ScopeSql OwnScope()
    {
        using var holder = new SqlCommand();
        return new("t.AssigneeEmployeeId=@ownTaskEmployee", [holder.Parameters.AddInt32("@ownTaskEmployee", currentSession.RequireSession().EmployeeId)]);
    }
    private ScopeSql CreatorScope()
    {
        var scope = dataScope.TaskFilter();
        using var holder = new SqlCommand();
        var creator = holder.Parameters.AddInt32("@cancelTaskCreator", currentSession.RequireSession().UserId);
        return new($"({scope.Condition}) AND t.CreatedByUserId=@cancelTaskCreator", scope.Parameters.Append(creator).ToArray());
    }
    private ScopeSql ReadScope()
    {
        var own = OwnScope();
        if (!authorizer.Can(Permission.WorkTaskAssign)) return own;
        var manager = dataScope.TaskFilter();
        return new($"({own.Condition}) OR ({manager.Condition})", own.Parameters.Concat(manager.Parameters).ToArray());
    }
    private static void ValidateLinks(int? booking, int? home)
    {
        if (booking is <= 0 || home is <= 0) throw new BusinessException("Liên kết đơn hoặc homestay không hợp lệ. Vui lòng chọn lại.");
    }
    private static void ValidateSearch(WorkTaskSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.Page(request.Page, "DueAtUtc", "Title", "Priority", "Status", "CreatedAtUtc");
        if (request.AssigneeEmployeeId is <= 0 || request.Status is { } status && !Enum.IsDefined(status)) throw new BusinessException("Bộ lọc việc không hợp lệ. Vui lòng chọn lại.");
    }
}
