using System.Globalization;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Enums;
using Homestay.DTO.Payments;

namespace Homestay.BLL.Services;

/// <summary>Dịch vụ thanh toán: nhân viên ghi phiếu thu tiền cọc kèm ảnh chứng từ chuyển khoản.</summary>
public sealed partial class PaymentService(Authorizer authorizer, ICurrentSession currentSession, DataScope dataScope,
    WriteOperationRunner writes, BookingRepository bookings, PaymentRepository payments, EvidenceRepository files,
    NotificationRepository notifications, DbClock clock, BookingService bookingService, AuditWriter audit, string machineName)
{
    public Task<RecordReceiptResult> RecordReceiptAsync(RecordReceiptRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.PaymentRecord);
        var normalized = ReceiptRules.Normalize(request);
        // File I/O belongs to GUI; validation, owned bytes and hashing precede opening the transaction.
        var evidence = ReceiptRules.ValidateEvidence(normalized.EvidenceBytes, normalized.EvidenceFileName);
        return writes.ExecuteAsync(async (db, ct) =>
        {
            var scope = dataScope.BookingFilter();
            var old = await bookings.GetForUpdateAsync(db, normalized.BookingId, scope.Condition, scope.AddParametersTo, ct).ConfigureAwait(false)
                ?? throw new NotFoundException();
            BookingService.CheckVersion(old, normalized.RowVer);
            if (await bookings.HasActiveDepositAsync(db, old.Id, ct).ConfigureAwait(false))
                throw new BusinessException(ConstraintMessages.GetMessage("UX_Payments_OneDeposit"),
                    new DbConstraintViolationException("UX_Payments_OneDeposit", DbConstraintKind.Unique, 2601));
            if (old.Status != BookingStatus.AwaitingPayment)
                throw new BusinessException("Chỉ được ghi nhận thu cọc cho đơn Chờ thanh toán. Vui lòng tải lại đơn.");
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            if (old.HoldExpiresAtUtc <= now) throw new BusinessException(ReceiptRules.ExpiredMessage);
            var transferredAt = ReceiptRules.TransferredAt(normalized, now);
            var session = currentSession.RequireSession();
            var actor = BookingStateMachine.Actor(session.Position);
            BookingStateMachine.Require(old.Status, BookingStatus.PaymentSubmitted, actor);
            var voucher = ReceiptRules.VoucherNo(now, await bookings.NextSequenceAsync(db, ReceiptRules.CounterKey(now), ct).ConfigureAwait(false));
            Guid? fileId = evidence is null ? null : await files.InsertAsync(db, evidence.FileName, evidence.ContentType,
                evidence.Content, evidence.Sha256, session.UserId, ct).ConfigureAwait(false);
            var id = await payments.InsertReceiptAsync(db, normalized, voucher, transferredAt, session.UserId, now, fileId, ct).ConfigureAwait(false);
            await new BookingStatusWriter(bookings, audit).ChangeStatusAsync(db, old.Id, old.Status, BookingStatus.PaymentSubmitted,
                normalized.RowVer, actor, now, new(session.UserId, "Booking.ChangeStatus", "Bookings", old.Id.ToString(CultureInfo.InvariantCulture),
                    new { Status = old.Status.ToString() }, new { Status = nameof(BookingStatus.PaymentSubmitted), PaymentId = id }, null, machineName), ct).ConfigureAwait(false);
            await audit.WriteAsync(db, new(session.UserId, "Payment.Record", "Payments", id.ToString(CultureInfo.InvariantCulture), null,
                new { BookingId = old.Id, VoucherNo = voucher, Type = "Deposit", Direction = "In", Status = "Pending", Method = normalized.Method.ToString(),
                    normalized.Amount, TransferredAtUtc = transferredAt, RecordedAtUtc = now, normalized.PayerName,
                    PayerAccount = ReceiptRules.MaskAccount(normalized.PayerAccount), normalized.TransferContent, EvidenceFileId = fileId }, null, machineName), ct).ConfigureAwait(false);
            await notifications.NotifyAccountantsAsync(db, $"Phiếu thu {voucher} của đơn {old.Code} chờ xác minh", id, now, ct).ConfigureAwait(false);
            var detail = await bookingService.ReadDetailAsync(db, old.Id, null, ct).ConfigureAwait(false);
            var insufficient = normalized.Amount < old.AmountDue;
            return new RecordReceiptResult(detail, detail.Payments.Single(p => p.Id == id))
                { IsInsufficient = insufficient, Warning = insufficient ? ReceiptRules.InsufficientMessage : null };
        }, cancellationToken);
    }
}
