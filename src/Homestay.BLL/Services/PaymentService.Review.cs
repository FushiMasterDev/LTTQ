using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DTO.Bookings;
using Homestay.DAL.Repositories;
using Homestay.DTO.Enums;
using Homestay.DTO.Common;
using Homestay.DTO.Payments;

namespace Homestay.BLL.Services;

public sealed partial class PaymentService
{
    public Task<PagedResult<PaymentQueueItem>> ListPendingReceiptsAsync(PaymentQueueRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.PaymentVerify);
        return ListPendingAsync(request, false, cancellationToken);
    }
    public Task<PagedResult<PaymentQueueItem>> ListPendingRefundsAsync(PaymentQueueRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingRefundRecord);
        return ListPendingAsync(request, true, cancellationToken);
    }
    private Task<PagedResult<PaymentQueueItem>> ListPendingAsync(PaymentQueueRequest request, bool refunds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.Page(request.Page, "RecordedAtUtc", "VoucherNo", "BookingCode", "Amount");
        if (request.BookingId is <= 0 || request.Method is { } method && !Enum.IsDefined(method))
            throw new BusinessException("Bộ lọc phiếu không hợp lệ.");
        var scope = dataScope.BookingFilter();
        var user = currentSession.RequireSession().UserId;
        return BusinessErrorTranslator.ExecuteAsync(token => bookingService.ReadFactory.ReadAsync((db, t) =>
            payments.ListPendingAsync(db, request, refunds, user, scope.Condition, scope.AddParametersTo, t), token), ct);
    }
    public Task<ReceiptVerificationDetail> GetReceiptForVerificationAsync(int paymentId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.PaymentVerify);
        var scope = dataScope.BookingFilter();
        return BusinessErrorTranslator.ExecuteAsync(ct => bookingService.ReadFactory.ReadAsync(async (db, token) =>
        {
            var receipt = await payments.GetReceiptAsync(db, paymentId, currentSession.RequireSession().UserId, scope.Condition, scope.AddParametersTo, token).ConfigureAwait(false)
                ?? throw new NotFoundException();
            return new ReceiptVerificationDetail(receipt, await bookingService.ReadScopedDetailAsync(db, receipt.BookingId, token).ConfigureAwait(false),
                await payments.GetReceiptRejectCountAsync(db, receipt.BookingId, token).ConfigureAwait(false),
                await bookingService.ReadSettings.GetBookingMaxReceiptRejectionsAsync(token, db).ConfigureAwait(false));
        }, ct), cancellationToken);
    }
    public Task<BookingDetail> VerifyReceiptAsync(VerifyReceiptRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.PaymentVerify);
        ArgumentNullException.ThrowIfNull(request);
        return ReviewReceiptAsync(request.PaymentId, request.RowVer, request.BookingRowVer, null, true, cancellationToken);
    }
    public Task<BookingDetail> RejectReceiptAsync(RejectReceiptRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.PaymentVerify);
        ArgumentNullException.ThrowIfNull(request);
        return ReviewReceiptAsync(request.PaymentId, request.RowVer, request.BookingRowVer,
            BookingService.RequiredLifecycleNote(request.Reason), false, cancellationToken);
    }

    private Task<BookingDetail> ReviewReceiptAsync(int paymentId, byte[] rowVer, byte[] bookingRowVer,
        string? reason, bool verify, CancellationToken cancellationToken)
    {
        StaffInputRules.RowVer(rowVer); StaffInputRules.RowVer(bookingRowVer);
        var version = rowVer.ToArray(); var bookingVersion = bookingRowVer.ToArray();
        return writes.ExecuteAsync(async (db, ct) =>
        {
            var session = currentSession.RequireSession();
            var scope = dataScope.BookingFilter();
            var receipt = await payments.GetReceiptAsync(db, paymentId, session.UserId, scope.Condition, scope.AddParametersTo, ct).ConfigureAwait(false)
                ?? throw new NotFoundException();
            // Booking first, matching RecordReceipt: serialize all operations on this booking.
            var old = await bookings.GetForUpdateAsync(db, receipt.BookingId, scope.Condition, scope.AddParametersTo, ct).ConfigureAwait(false)
                ?? throw new NotFoundException();
            BookingService.CheckVersion(old, bookingVersion);
            if (!receipt.RowVer.AsSpan().SequenceEqual(version)) throw new ConcurrencyException();
            if (receipt.Status != PaymentStatus.Pending || old.Status != BookingStatus.PaymentSubmitted)
                throw new BusinessException("Chỉ được xử lý phiếu thu chờ xác minh của đơn Chờ xác minh. Vui lòng tải lại đơn.");
            if (verify && receipt.RecordedByUserId == session.UserId)
                throw new ForbiddenException("Người ghi nhận không được tự xác minh phiếu thu. Vui lòng nhờ Kế toán khác xác minh.");
            if (verify && receipt.Amount < old.AmountDue)
                throw new BusinessException("Phiếu thu thiếu tiền, không xác minh được — từ chối hoặc chờ khách chuyển bù.");
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            var metadata = new BookingStatusMetadata();
            var target = BookingStatus.Deposited;
            if (verify)
            {
                // Seed uses Booking.ConfirmMinutes=30. Missing key alone falls back to spec 30;
                // malformed configured values still fail validation.
                var values = await bookingService.ReadSettings.GetAllAsync(ct, db).ConfigureAwait(false);
                var minutes = 30;
                if (values.TryGetValue("Booking.ConfirmMinutes", out var configured))
                {
                    SettingsProvider.Validate("Booking.ConfirmMinutes", configured);
                    minutes = int.Parse(configured, System.Globalization.CultureInfo.InvariantCulture);
                }
                metadata = metadata with { ConfirmDeadlineAtUtc = now.AddMinutes(minutes) };
            }
            else
            {
                var count = await payments.GetReceiptRejectCountAsync(db, old.Id, ct).ConfigureAwait(false);
                var max = await bookingService.ReadSettings.GetBookingMaxReceiptRejectionsAsync(ct, db).ConfigureAwait(false);
                target = count + 1 >= max ? BookingStatus.Expired : BookingStatus.AwaitingPayment;
                metadata = metadata with { IncrementReceiptRejectCount = true,
                    HoldExpiresAtUtc = target == BookingStatus.AwaitingPayment
                        ? now.AddMinutes(await bookingService.ReadSettings.GetBookingHoldMinutesAsync(ct, db).ConfigureAwait(false)) : null };
            }
            var actor = BookingStateMachine.Actor(session.Position);
            BookingStateMachine.Require(old.Status, target, actor, reason);
            if (await payments.ReviewReceiptAsync(db, paymentId, version, verify, session.UserId, now, reason, ct).ConfigureAwait(false) == 0)
                throw new ConcurrencyException();
            await new BookingStatusWriter(bookings, audit).ChangeStatusAsync(db, old.Id, old.Status, target, bookingVersion,
                actor, now, new(session.UserId, "Booking.ChangeStatus", "Bookings", old.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    new { Status = old.Status.ToString() }, new { Status = target.ToString(), PaymentId = paymentId,
                        metadata.ConfirmDeadlineAtUtc, metadata.HoldExpiresAtUtc, metadata.IncrementReceiptRejectCount }, reason, machineName), metadata, ct).ConfigureAwait(false);
            await audit.WriteAsync(db, new(session.UserId, verify ? "Payment.Verify" : "Payment.Reject", "Payments",
                paymentId.ToString(System.Globalization.CultureInfo.InvariantCulture), new { Status = "Pending" },
                new { BookingId = old.Id, Status = verify ? "Verified" : "Rejected", VerifiedByUserId = verify ? (int?)session.UserId : null,
                    VerifiedAtUtc = verify ? (DateTime?)now : null, RejectReason = reason }, reason, machineName), ct).ConfigureAwait(false);
            await notifications.NotifyBookingOperatorsAsync(db, old.Id, verify
                ? $"Đơn {old.Code} đã xác minh cọc, gọi chủ xác nhận"
                : $"Phiếu thu {receipt.VoucherNo} đơn {old.Code} bị từ chối. Vui lòng xem lý do trên phiếu.", now, ct).ConfigureAwait(false);
            return await bookingService.ReadDetailAsync(db, old.Id, null, ct).ConfigureAwait(false);
        }, cancellationToken);
    }
    public Task<PaymentQueueItem> RecordRefundPaidAsync(RecordRefundPaidRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
