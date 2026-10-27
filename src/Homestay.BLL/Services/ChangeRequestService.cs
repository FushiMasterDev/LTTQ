using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Bookings;
using Homestay.DTO.ChangeRequests;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using System.Globalization;

namespace Homestay.BLL.Services;

public sealed class ChangeRequestService(Authorizer authorizer, ICurrentSession currentSession, DataScope dataScope,
    ConnectionFactory factory, ChangeRequestRepository repository, BookingService bookings)
{
    public Task<PagedResult<CancelRequestItem>> ListAsync(ChangeRequestSearchRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingCancellationRequest);
        ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.Page(request.Page, "RequestedAtUtc", "BookingCode", "Status");
        if (request.BookingId is <= 0 || request.Status is { } status && !Enum.IsDefined(status))
            throw new BusinessException("Bộ lọc yêu cầu hủy không hợp lệ.");
        var scope = dataScope.BookingFilter();
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync((db, token) =>
            repository.ListAsync(db, request, scope.Condition, scope.AddParametersTo, currentSession.RequireSession().UserId,
                authorizer.Can(Permission.BookingChangeReview), token), ct), cancellationToken);
    }
    public Task<CancelRequestDetail> GetAsync(int requestId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingCancellationRequest);
        var scope = dataScope.BookingFilter();
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        {
            var item = await repository.GetAsync(db, requestId, scope.Condition, scope.AddParametersTo, currentSession.RequireSession().UserId,
                authorizer.Can(Permission.BookingChangeReview), token).ConfigureAwait(false) ?? throw new NotFoundException();
            return new CancelRequestDetail(item, await bookings.ReadScopedDetailAsync(db, item.BookingId, token).ConfigureAwait(false));
        }, ct), cancellationToken);
    }
    public Task<BookingRefundPreview> PreviewCancelAsync(int bookingId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingCancellationRequest);
        return bookings.PreviewRefundAsync(bookingId, cancellationToken);
    }
    public Task<CancelRequestDetail> SubmitCancelAsync(SubmitCancelRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingCancellationRequest);
        ArgumentNullException.ThrowIfNull(request);
        var reason = BookingService.RequiredLifecycleNote(request.Reason);
        StaffInputRules.RowVer(request.RowVer);
        var version = request.RowVer.ToArray();
        return bookings.WriteOperations.ExecuteAsync(async (db, ct) =>
        {
            var old = await bookings.WritableAsync(db, request.BookingId, ct).ConfigureAwait(false);
            BookingService.CheckVersion(old, version);
            RequireCancellable(old.Status);
            if (await repository.HasOpenAsync(db, old.Id, ct).ConfigureAwait(false))
                throw new BusinessException(ConstraintMessages.GetMessage("UX_BookingChangeRequests_OneOpenRequest"),
                    new DbConstraintViolationException("UX_BookingChangeRequests_OneOpenRequest", DbConstraintKind.Unique, 2601));
            var detail = await bookings.ReadScopedDetailAsync(db, old.Id, ct).ConfigureAwait(false);
            var now = await bookings.WriteClock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            var quote = BookingService.CalculateRefund(detail, now, RefundReason.CustomerCancellation).RefundAmount;
            var user = currentSession.RequireSession().UserId;
            var id = await repository.InsertCancelAsync(db, old.Id, reason, quote, user, now, ct).ConfigureAwait(false);
            await bookings.WriteAudit.WriteAsync(db, new(user, "ChangeRequest.SubmitCancel", "BookingChangeRequests", id.ToString(CultureInfo.InvariantCulture),
                null, new { BookingId = old.Id, RequestType = "Cancel", Status = "Pending", Reason = reason,
                    QuotedRefundAmount = quote, RequestedByUserId = user, RequestedAtUtc = now }, reason, bookings.WriteMachineName), ct).ConfigureAwait(false);
            await new NotificationRepository().NotifyScopedManagersAsync(db, old.Id, $"Đơn {old.Code} có yêu cầu hủy chờ duyệt", now, ct).ConfigureAwait(false);
            return await ReadResultAsync(db, id, ct).ConfigureAwait(false);
        }, cancellationToken);
    }
    public Task<CancelRequestDetail> ApproveAsync(ReviewCancelRequest request, CancellationToken cancellationToken = default)
    {
        RequireManager();
        ArgumentNullException.ThrowIfNull(request);
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : BookingService.RequiredLifecycleNote(request.Note);
        return ReviewAsync(request.RequestId, request.RowVer, request.BookingRowVer, note, true, cancellationToken);
    }
    public Task<CancelRequestDetail> RejectAsync(RejectCancelRequest request, CancellationToken cancellationToken = default)
    {
        RequireManager();
        ArgumentNullException.ThrowIfNull(request);
        return ReviewAsync(request.RequestId, request.RowVer, request.BookingRowVer,
            BookingService.RequiredLifecycleNote(request.Reason), false, cancellationToken);
    }

    private void RequireManager()
    {
        authorizer.Require(Permission.BookingChangeReview);
        if (currentSession.RequireSession().Position != Position.Manager) throw new ForbiddenException();
    }
    private static void RequireCancellable(BookingStatus status)
    {
        // Submission does not change the booking; the eventual transition belongs to a reviewing Manager.
        BookingStateMachine.Require(status, BookingStatus.Cancelled, BookingTransitionActor.Manager);
        if (status is not (BookingStatus.Deposited or BookingStatus.Confirmed))
            throw new BusinessException("Chỉ được gửi hoặc duyệt yêu cầu hủy khi đơn Đã cọc hoặc Đã xác nhận. Vui lòng tải lại đơn.");
    }
    private Task<CancelRequestDetail> ReviewAsync(int id, byte[] rowVer, byte[] bookingRowVer, string? note,
        bool approve, CancellationToken cancellationToken)
    {
        StaffInputRules.RowVer(rowVer); StaffInputRules.RowVer(bookingRowVer);
        var version = rowVer.ToArray(); var bookingVersion = bookingRowVer.ToArray();
        return bookings.WriteOperations.ExecuteAsync(async (db, ct) =>
        {
            var initial = await ReadResultItemAsync(db, id, ct).ConfigureAwait(false);
            // Lock booking first, then re-read request after any competing approval has committed.
            var old = await bookings.WritableAsync(db, initial.BookingId, ct).ConfigureAwait(false);
            BookingService.CheckVersion(old, bookingVersion);
            var item = await ReadResultItemAsync(db, id, ct).ConfigureAwait(false);
            if (!item.RowVer.AsSpan().SequenceEqual(version)) throw new ConcurrencyException();
            var user = currentSession.RequireSession().UserId;
            if (item.RequestedByUserId == user)
                throw new ForbiddenException("Người gửi không được tự duyệt hoặc từ chối yêu cầu hủy. Vui lòng nhờ Quản lý khác xử lý.");
            if (item.Status != ChangeRequestStatus.Pending)
                throw new BusinessException("Chỉ được duyệt hoặc từ chối yêu cầu hủy đang Chờ duyệt. Vui lòng tải lại yêu cầu.");
            if (approve)
            {
                RequireCancellable(old.Status);
                if (item.QuotedRefundAmount is not { } quote || quote < 0 || quote > old.AmountDue)
                    throw new BusinessException("Yêu cầu thiếu hoặc sai số tiền hoàn đã chốt. Vui lòng từ chối và gửi lại yêu cầu.");
            }
            var now = await bookings.WriteClock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            var target = approve ? ChangeRequestStatus.Applied : ChangeRequestStatus.Rejected;
            if (await repository.DecideAsync(db, item, target, user, note, now, ct).ConfigureAwait(false) == 0)
                throw new ConcurrencyException();
            await bookings.WriteAudit.WriteAsync(db, new(user, approve ? "ChangeRequest.Approve" : "ChangeRequest.Reject", "BookingChangeRequests", id.ToString(CultureInfo.InvariantCulture),
                new { Status = item.Status.ToString() }, new { BookingId = old.Id, Status = target.ToString(), DecidedByUserId = user,
                    DecidedAtUtc = now, DecisionNote = note, item.QuotedRefundAmount }, note, bookings.WriteMachineName), ct).ConfigureAwait(false);
            if (approve)
                await bookings.ApplyCancellationAsync(db, old, bookingVersion, item.QuotedRefundAmount!.Value, item.Reason,
                    "Booking.CancelApproved", now, ct).ConfigureAwait(false);
            await new NotificationRepository().NotifyBookingAssigneeAsync(db, item.RequestedByUserId,
                approve ? $"Yêu cầu hủy đơn {old.Code} đã được duyệt" : $"Yêu cầu hủy đơn {old.Code} bị từ chối. Vui lòng xem lý do trên yêu cầu.",
                old.Id, now, ct).ConfigureAwait(false);
            return await ReadResultAsync(db, id, ct).ConfigureAwait(false);
        }, cancellationToken);
    }
    private async Task<CancelRequestItem> ReadResultItemAsync(DbSession db, int id, CancellationToken ct)
    {
        var scope = dataScope.BookingFilter();
        return await repository.GetAsync(db, id, scope.Condition, scope.AddParametersTo, currentSession.RequireSession().UserId,
            authorizer.Can(Permission.BookingChangeReview), ct).ConfigureAwait(false) ?? throw new NotFoundException();
    }
    private async Task<CancelRequestDetail> ReadResultAsync(DbSession db, int id, CancellationToken ct)
    {
        var item = await ReadResultItemAsync(db, id, ct).ConfigureAwait(false);
        return new(item, await bookings.ReadScopedDetailAsync(db, item.BookingId, ct).ConfigureAwait(false));
    }
}
