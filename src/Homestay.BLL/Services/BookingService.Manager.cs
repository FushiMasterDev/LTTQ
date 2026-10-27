using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Bookings;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Services;

public sealed partial class BookingService
{
    public Task<IReadOnlyList<BookingDetail>> AssignManyAsync(AssignManyBookingsRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingAssign);
        if (authorizer.GetAccess(Permission.BookingAssign) != AccessLevel.All) throw new ForbiddenException();
        ArgumentNullException.ThrowIfNull(request);
        if (request.AssignedEmployeeId <= 0 || request.Bookings is null || request.Bookings.Count is < 1 or > 100 ||
            request.Bookings.Any(b => b is null || b.BookingId <= 0) || request.Bookings.Select(b => b.BookingId).Distinct().Count() != request.Bookings.Count)
            throw new BusinessException("Vui lòng chọn từ 1 đến 100 đơn khác nhau và người nhận hợp lệ.");
        foreach (var item in request.Bookings) { StaffInputRules.RowVer(item.RowVer); StaffInputRules.Text(item.BookingCode, 20); }
        var items = request.Bookings.Select(b => b with { RowVer = b.RowVer.ToArray() }).OrderBy(b => b.BookingId).ToArray();
        return writes.ExecuteAsync<IReadOnlyList<BookingDetail>>(async (db, ct) =>
        {
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            var result = new List<BookingDetail>(); int userId = 0;
            foreach (var item in items)
            {
                BookingWriteSnapshot old;
                try { old = await WritableAsync(db, item.BookingId, ct).ConfigureAwait(false); }
                catch (NotFoundException) { throw new NotFoundException($"Không tìm thấy đơn {item.BookingCode ?? "#" + item.BookingId}. Vui lòng tải lại danh sách."); }
                try { userId = await AssignCoreAsync(db, old, request.AssignedEmployeeId, item.RowVer, now, ct).ConfigureAwait(false); }
                catch (ConcurrencyException) { throw new ConcurrencyException($"Đơn {old.Code} vừa thay đổi. Chưa giao đơn nào; vui lòng tải lại danh sách."); }
                catch (BusinessException ex) { throw new BusinessException($"Đơn {old.Code}: {ex.Message}", ex); }
                result.Add(await ReadDetailAsync(db, old.Id, null, ct).ConfigureAwait(false));
            }
            var codes = string.Join(", ", result.Take(10).Select(b => b.Booking.Code));
            await new NotificationRepository().NotifyBookingAssigneeAsync(db, userId,
                $"Bạn được giao {result.Count} đơn: {codes}{(result.Count > 10 ? ", … (xem danh sách đơn của tôi)" : "")}", items[0].BookingId, now, ct).ConfigureAwait(false);
            return result;
        }, cancellationToken);
    }
    // M2.4 Reassign and M5.1 batch use the same validation, conditional UPDATE and audit.
    private async Task<int> AssignCoreAsync(UnitOfWork db, BookingWriteSnapshot old, int employeeId, byte[] version, DateTime now, CancellationToken ct)
    {
        CheckVersion(old, version);
        if (BookingStateMachine.IsTerminal(old.Status)) throw new BusinessException("Đơn đã kết thúc. Vui lòng tải lại đơn.");
        if (old.AssignedEmployeeId == employeeId) throw new BusinessException("Vui lòng chọn người phụ trách khác người hiện tại.");
        var userId = await repository.GetAssignableUserAsync(db, employeeId, old.Id, ct).ConfigureAwait(false)
            ?? throw new BusinessException("Người nhận phải là Nhân viên hoặc Quản lý đang làm việc, có tài khoản hoạt động và phụ trách được khu vực của đơn. Vui lòng chọn lại.");
        if (await repository.AssignAsync(db, old.Id, employeeId, version, false, now, ct).ConfigureAwait(false) == 0) throw new ConcurrencyException();
        await AssignmentAuditAsync(db, "Booking.Reassign", old, employeeId, ct).ConfigureAwait(false);
        return userId;
    }
    public Task<PagedResult<BookingExceptionItem>> ListExceptionsAsync(BookingExceptionSearchRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.OperationsReportView);
        ValidateExceptions(request);
        var scope = dataScope.BookingFilter();
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        {
            var now = await clock.GetUtcNowAsync(db, token).ConfigureAwait(false);
            var thresholds = await ExceptionThresholdsAsync(db, token).ConfigureAwait(false);
            return await repository.ListExceptionsAsync(db, request, scope.Condition, scope.AddParametersTo, now, thresholds, token).ConfigureAwait(false);
        }, ct), cancellationToken);
    }
    public Task<ManagerDashboard> GetManagerDashboardAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.OperationsReportView);
        var scope = dataScope.BookingFilter(); var tasks = dataScope.TaskFilter();
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        {
            var now = await clock.GetUtcNowAsync(db, token).ConfigureAwait(false);
            return await repository.ManagerDashboardAsync(db, scope.Condition, scope.AddParametersTo, tasks.Condition, tasks.AddParametersTo, now,
                await ExceptionThresholdsAsync(db, token).ConfigureAwait(false), token).ConfigureAwait(false);
        }, ct), cancellationToken);
    }
    private async Task<ManagerExceptionThresholds> ExceptionThresholdsAsync(DbSession db, CancellationToken ct)
    {
        var start = await settings.GetWorkingHoursStartAsync(ct, db).ConfigureAwait(false);
        var end = await settings.GetWorkingHoursEndAsync(ct, db).ConfigureAwait(false);
        if (start >= end) throw new BusinessException("Giờ làm việc bắt đầu phải trước giờ kết thúc. Vui lòng kiểm tra tham số.");
        // No hold-alert setting in 7.7/seed: use the existing M2 dashboard's 5-minute alert window.
        return new(5, await settings.GetPaymentReviewAlertWorkingHoursAsync(ct, db).ConfigureAwait(false), start, end);
    }
    private static void ValidateExceptions(BookingExceptionSearchRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.Page(request.Page, "BookingCode", "Kind", "SinceUtc", "ElapsedSeconds", "DeadlineAtUtc");
        if (request.Kind is { } kind && !Enum.IsDefined(kind) || request.AreaId is <= 0 || request.HomestayId is <= 0)
            throw new BusinessException("Bộ lọc ngoại lệ không hợp lệ. Vui lòng chọn lại.");
    }
}
