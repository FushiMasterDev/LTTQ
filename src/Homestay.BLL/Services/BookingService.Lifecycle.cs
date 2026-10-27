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
    // Reuse the already composed read dependencies without changing the M2.4 PaymentService constructor.
    internal ConnectionFactory ReadFactory => factory;
    internal SettingsProvider ReadSettings => settings;

    public Task<IReadOnlyList<Lookup>> ListAssigneesAsync(int bookingId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingAssign);
        var scope = dataScope.BookingFilter();
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        {
            var context = await repository.GetAssignmentContextAsync(db, bookingId, scope.Condition, scope.AddParametersTo, token).ConfigureAwait(false)
                ?? throw new NotFoundException();
            if (authorizer.GetAccess(Permission.BookingAssign) == AccessLevel.Own && context.AssignedEmployeeId != currentSession.RequireSession().EmployeeId)
                throw new ForbiddenException("Chỉ được giao lại đơn mình đang phụ trách.");
            return await repository.ListAssigneesAsync(db, bookingId, token).ConfigureAwait(false);
        }, ct), cancellationToken);
    }

    internal async Task<BookingDetail> ReadScopedDetailAsync(DbSession db, int bookingId, CancellationToken ct)
    {
        var scope = dataScope.BookingFilter();
        _ = await repository.GetAssignmentContextAsync(db, bookingId, scope.Condition, scope.AddParametersTo, ct).ConfigureAwait(false)
            ?? throw new NotFoundException();
        return await ReadDetailAsync(db, bookingId, null, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<Lookup>> ListConfirmationRoomsAsync(int bookingId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingRoomChange);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        {
            var detail = await ReadScopedDetailAsync(db, bookingId, token).ConfigureAwait(false);
            if (!BookingStateMachine.CanTransition(detail.Booking.Status, BookingStatus.Confirmed, BookingStateMachine.Actor(currentSession.RequireSession().Position)))
                throw new BusinessException("Chỉ được chọn phòng khi đơn đang chờ chủ xác nhận.");
            return await repository.ListConfirmationRoomsAsync(db, bookingId, await clock.GetUtcNowAsync(db, token).ConfigureAwait(false), token).ConfigureAwait(false);
        }, ct), cancellationToken);
    }

    public Task<BookingRefundPreview> PreviewRefundAsync(int bookingId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingCancellationRequest);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        {
            var detail = await ReadScopedDetailAsync(db, bookingId, token).ConfigureAwait(false);
            var now = await clock.GetUtcNowAsync(db, token).ConfigureAwait(false);
            var today = DateOnly.FromDateTime(now.AddHours(7));
            var reason = detail.Booking.Status switch
            {
                BookingStatus.NoShow => RefundReason.NoShow,
                BookingStatus.Completed => RefundReason.Completed,
                BookingStatus.Rejected => RefundReason.OwnerRejected,
                _ => RefundReason.CustomerCancellation
            };
            var calculation = RefundCalculator.Calculate(new(detail.Booking.CheckIn, today, detail.AmountDue,
                detail.Payments.Any(p => p.Type == PaymentType.Deposit && p.Status == PaymentStatus.Verified), detail.CancellationPolicy)
                { Reason = reason, TotalAmount = detail.TotalAmount, CommissionRate = detail.CommissionRate });
            return new BookingRefundPreview(bookingId, now, today, detail.Booking.RowVer, calculation);
        }, ct), cancellationToken);
    }

    internal static string RequiredLifecycleNote(string? text)
    {
        var note = text?.Trim() ?? "";
        if (note.Length is < 1 or > 500)
            throw new BusinessException("Vui lòng nhập ghi chú hoặc lý do từ 1 đến 500 ký tự.");
        return note;
    }

    public Task<BookingDetail> ConfirmOnBehalfAsync(ConfirmOnBehalfRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingConfirm);
        ArgumentNullException.ThrowIfNull(request);
        if (request.RoomId is not null) authorizer.Require(Permission.BookingRoomChange);
        return LifecycleAsync(request.BookingId, request.Note, request.RowVer, BookingStatus.Confirmed, request.RoomId, cancellationToken);
    }
    public Task<BookingDetail> RejectOnBehalfAsync(RejectOnBehalfRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingConfirm);
        ArgumentNullException.ThrowIfNull(request);
        return LifecycleAsync(request.BookingId, request.Reason, request.RowVer, BookingStatus.Rejected, null, cancellationToken);
    }
    public Task<BookingDetail> CheckInAsync(StayActionRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingStayManage);
        ArgumentNullException.ThrowIfNull(request);
        return LifecycleAsync(request.BookingId, request.Note, request.RowVer, BookingStatus.CheckedIn, null, cancellationToken);
    }
    public Task<BookingDetail> CheckOutAsync(StayActionRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingStayManage);
        ArgumentNullException.ThrowIfNull(request);
        return LifecycleAsync(request.BookingId, request.Note, request.RowVer, BookingStatus.Completed, null, cancellationToken);
    }
    public Task<BookingDetail> MarkNoShowAsync(StayActionRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingStayManage);
        ArgumentNullException.ThrowIfNull(request);
        return LifecycleAsync(request.BookingId, request.Note, request.RowVer, BookingStatus.NoShow, null, cancellationToken);
    }

    private Task<BookingDetail> LifecycleAsync(int id, string text, byte[] rowVer, BookingStatus target, int? roomId, CancellationToken cancellationToken)
    {
        var note = RequiredLifecycleNote(text);
        StaffInputRules.RowVer(rowVer);
        var version = rowVer.ToArray();
        return writes.ExecuteAsync(async (db, ct) =>
        {
            // RoomType lock precedes booking UPDLOCK, the same ordering as Create/HoldExpiry.
            // Re-read the scoped booking and RowVer after waiting for the lock.
            if (roomId is not null)
            {
                var initial = await ReadScopedDetailAsync(db, id, ct).ConfigureAwait(false);
                await db.AcquireAppLockAsync($"RoomType:{initial.Booking.RoomTypeId}", 5000, ct).ConfigureAwait(false);
            }
            var old = await WritableAsync(db, id, ct).ConfigureAwait(false);
            CheckVersion(old, version);
            var session = currentSession.RequireSession();
            var actor = BookingStateMachine.Actor(session.Position);
            BookingStateMachine.Require(old.Status, target, actor, note);
            var detail = await ReadDetailAsync(db, id, null, ct).ConfigureAwait(false);
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            var today = DateOnly.FromDateTime(now.AddHours(7));
            if (target == BookingStatus.CheckedIn && today < detail.Booking.CheckIn)
                throw new BusinessException("Chưa đến ngày nhận phòng. Vui lòng thao tác từ ngày nhận phòng.");
            if (target == BookingStatus.NoShow && today <= detail.Booking.CheckIn)
                throw new BusinessException("Chỉ được ghi nhận không đến từ ngày sau ngày nhận phòng. Vui lòng kiểm tra lại.");
            var metadata = new BookingStatusMetadata();
            if (roomId is { } replacement)
            {
                await _holdExpiry.ExpireAsync(db, detail.Booking.RoomTypeId, now, ct).ConfigureAwait(false);
                // Use the exact lookup predicate, including StaffReadSql.Occupies, under RoomType lock.
                var rooms = await repository.ListConfirmationRoomsAsync(db, id, now, ct).ConfigureAwait(false);
                if (!rooms.Any(r => r.Id == replacement))
                    throw new BusinessException("Phòng thay thế phải khác phòng hiện tại, cùng loại, đang bật và trống mọi đêm. Vui lòng chọn lại.");
                await repository.MoveRoomNightsAsync(db, id, replacement, ct).ConfigureAwait(false);
                metadata = metadata with { RoomId = replacement };
            }
            if (target == BookingStatus.Rejected)
            {
                var refund = RefundCalculator.Calculate(new(detail.Booking.CheckIn, today, detail.AmountDue,
                    detail.Payments.Any(p => p.Type == PaymentType.Deposit && p.Status == PaymentStatus.Verified), detail.CancellationPolicy)
                    { Reason = RefundReason.OwnerRejected });
                if (refund.RefundAmount != detail.AmountDue)
                    throw new BusinessException("Đơn chưa có phiếu cọc đã xác minh. Vui lòng báo Kế toán kiểm tra trước khi từ chối thay chủ.");
                metadata = metadata with { RefundAmount = refund.RefundAmount };
            }
            var action = target switch
            {
                BookingStatus.Confirmed => "Booking.ConfirmOnBehalf",
                BookingStatus.Rejected => "Booking.RejectOnBehalf",
                BookingStatus.CheckedIn => "Booking.CheckIn",
                BookingStatus.Completed => "Booking.CheckOut",
                BookingStatus.NoShow => "Booking.MarkNoShow",
                _ => throw new ArgumentOutOfRangeException(nameof(target))
            };
            await new BookingStatusWriter(repository, audit).ChangeStatusAsync(db, id, old.Status, target, version,
                actor, now, new(session.UserId, action, "Bookings", id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    new { Status = old.Status.ToString(), detail.Booking.RoomId },
                    new { Status = target.ToString(), RoomId = roomId ?? detail.Booking.RoomId,
                        RefundAmount = target == BookingStatus.NoShow ? 0m : metadata.RefundAmount }, note, machineName), metadata, ct).ConfigureAwait(false);
            if (target == BookingStatus.Rejected)
            {
                var key = "PC" + now.AddHours(7).ToString("yyMM", System.Globalization.CultureInfo.InvariantCulture);
                var voucher = key + "-" + (await repository.NextSequenceAsync(db, key, ct).ConfigureAwait(false)).ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
                var paymentId = await new PaymentRepository().InsertPendingRefundAsync(db, id, voucher, metadata.RefundAmount!.Value,
                    session.UserId, now, note, ct).ConfigureAwait(false);
                await audit.WriteAsync(db, new(session.UserId, "Payment.CreateRefund", "Payments", paymentId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    null, new { BookingId = id, VoucherNo = voucher, Type = "Refund", Direction = "Out", Status = "Pending", Amount = metadata.RefundAmount }, note, machineName), ct).ConfigureAwait(false);
                await new NotificationRepository().NotifyAccountantsAsync(db, $"Phiếu chi hoàn {voucher} đơn {old.Code} chờ chi", paymentId, now, ct).ConfigureAwait(false);
            }
            return await ReadDetailAsync(db, id, null, ct).ConfigureAwait(false);
        }, cancellationToken);
    }
    public Task<BookingDetail> CancelHomestayUnavailableAsync(CancelHomestayUnavailableRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
