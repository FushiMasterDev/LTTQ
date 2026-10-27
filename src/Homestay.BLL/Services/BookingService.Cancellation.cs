using System.Globalization;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Bookings;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Services;

public sealed partial class BookingService
{
    // Preview and transactional submission use exactly the same calculation and collected-deposit predicate.
    internal static RefundCalculation CalculateRefund(BookingDetail detail, DateTime now, RefundReason reason) =>
        RefundCalculator.Calculate(new(detail.Booking.CheckIn, DateOnly.FromDateTime(now.AddHours(7)), detail.AmountDue,
            detail.Payments.Any(p => p.Type == PaymentType.Deposit && p.Status == PaymentStatus.Verified), detail.CancellationPolicy)
            { Reason = reason, TotalAmount = detail.TotalAmount, CommissionRate = detail.CommissionRate });

    // Extracted from M3.3A owner rejection: one allocation/insert/audit/notification path for all refunds.
    internal async Task CreatePendingRefundAsync(UnitOfWork db, BookingWriteSnapshot old, decimal amount,
        string reason, DateTime now, CancellationToken ct)
    {
        if (amount <= 0) return;
        var user = currentSession.RequireSession().UserId;
        var key = "PC" + now.AddHours(7).ToString("yyMM", CultureInfo.InvariantCulture);
        var voucher = key + "-" + (await repository.NextSequenceAsync(db, key, ct).ConfigureAwait(false)).ToString("D4", CultureInfo.InvariantCulture);
        var paymentId = await new PaymentRepository().InsertPendingRefundAsync(db, old.Id, voucher, amount,
            user, now, reason, ct).ConfigureAwait(false);
        await audit.WriteAsync(db, new(user, "Payment.CreateRefund", "Payments", paymentId.ToString(CultureInfo.InvariantCulture),
            null, new { BookingId = old.Id, VoucherNo = voucher, Type = "Refund", Direction = "Out", Status = "Pending", Amount = amount },
            reason, machineName), ct).ConfigureAwait(false);
        await new NotificationRepository().NotifyAccountantsAsync(db, $"Phiếu chi hoàn {voucher} đơn {old.Code} chờ chi", paymentId, now, ct).ConfigureAwait(false);
    }

    internal async Task ApplyCancellationAsync(UnitOfWork db, BookingWriteSnapshot old, byte[] version, decimal refund,
        string reason, string action, DateTime now, CancellationToken ct)
    {
        var session = currentSession.RequireSession();
        await new BookingStatusWriter(repository, audit).ChangeStatusAsync(db, old.Id, old.Status, BookingStatus.Cancelled,
            version, BookingStateMachine.Actor(session.Position), now,
            new(session.UserId, action, "Bookings", old.Id.ToString(CultureInfo.InvariantCulture),
                new { Status = old.Status.ToString() }, new { Status = "Cancelled", RefundAmount = refund, CancelReason = reason }, reason, machineName),
            new BookingStatusMetadata { RefundAmount = refund }, ct).ConfigureAwait(false);
        await CreatePendingRefundAsync(db, old, refund, reason, now, ct).ConfigureAwait(false);
    }

    public Task<BookingDetail> CancelHomestayUnavailableAsync(CancelHomestayUnavailableRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingChangeReview);
        if (currentSession.RequireSession().Position != Position.Manager) throw new ForbiddenException();
        ArgumentNullException.ThrowIfNull(request);
        var reason = RequiredLifecycleNote(request.Reason);
        StaffInputRules.RowVer(request.RowVer);
        var version = request.RowVer.ToArray();
        return writes.ExecuteAsync(async (db, ct) =>
        {
            var old = await WritableAsync(db, request.BookingId, ct).ConfigureAwait(false);
            CheckVersion(old, version);
            var session = currentSession.RequireSession();
            BookingStateMachine.Require(old.Status, BookingStatus.Cancelled, BookingStateMachine.Actor(session.Position), reason);
            if (old.Status is not (BookingStatus.Deposited or BookingStatus.Confirmed))
                throw new BusinessException("Chỉ được hủy do homestay không phục vụ được khi đơn Đã cọc hoặc Đã xác nhận. Vui lòng tải lại đơn.");
            var detail = await ReadDetailAsync(db, old.Id, null, ct).ConfigureAwait(false);
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            var refund = CalculateRefund(detail, now, RefundReason.HomestayUnavailable);
            if (refund.RefundAmount != detail.AmountDue)
                throw new BusinessException("Đơn chưa có phiếu cọc đã xác minh. Vui lòng báo Kế toán kiểm tra trước khi hủy.");
            var changes = new ChangeRequestRepository();
            var open = await changes.GetOpenCancelAsync(db, old.Id, ct).ConfigureAwait(false);
            if (open is not null)
            {
                if (open.Status == ChangeRequestStatus.Pending && open.RequestedByUserId == session.UserId)
                    throw new ForbiddenException("Người gửi không được tự đóng yêu cầu hủy. Vui lòng nhờ Quản lý khác hủy do homestay không phục vụ được.");
                if (await changes.DecideAsync(db, open, ChangeRequestStatus.Expired, session.UserId, reason, now, ct).ConfigureAwait(false) == 0)
                    throw new ConcurrencyException();
                await audit.WriteAsync(db, new(session.UserId, "ChangeRequest.Expire", "BookingChangeRequests", open.Id.ToString(CultureInfo.InvariantCulture),
                    new { Status = open.Status.ToString() }, new { Status = "Expired", BookingId = old.Id, DecisionNote = reason }, reason, machineName), ct).ConfigureAwait(false);
            }
            await ApplyCancellationAsync(db, old, version, refund.RefundAmount, reason, "Booking.CancelHomestayUnavailable", now, ct).ConfigureAwait(false);
            await new NotificationRepository().NotifyBookingOperatorsAsync(db, old.Id,
                $"Đơn {old.Code} đã hủy do homestay không phục vụ được", now, ct).ConfigureAwait(false);
            return await ReadDetailAsync(db, old.Id, null, ct).ConfigureAwait(false);
        }, cancellationToken);
    }
}
