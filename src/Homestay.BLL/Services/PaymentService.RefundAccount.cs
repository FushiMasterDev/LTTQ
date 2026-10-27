using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DTO.Payments;
namespace Homestay.BLL.Services;

public sealed partial class PaymentService
{
    public Task<RefundAccountResult> UpdateRefundAccountAsync(UpdateRefundAccountRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.RefundAccountEdit); ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.RowVer(request.RowVer); var version = request.RowVer.ToArray();
        var account = ListingWriteRules.Ascii(request.PayeeAccount, 30, "Số tài khoản nhận hoàn")
            ?? throw new BusinessException("Vui lòng nhập số tài khoản nhận hoàn từ 1 đến 30 ký tự ASCII.");
        var name = ListingWriteRules.Text(request.PayeeName, 100);
        return writes.ExecuteAsync(async (db, ct) =>
        {
            var scope = dataScope.BookingFilter();
            var initial = await payments.GetAccountAsync(db, request.PaymentId, scope.Condition, scope.AddParametersTo, ct).ConfigureAwait(false) ?? throw new NotFoundException();
            // Same booking-first order as RecordRefundPaidAsync; both compete on the payment's rowversion.
            _ = await bookings.GetForUpdateAsync(db, initial.BookingId, scope.Condition, scope.AddParametersTo, ct).ConfigureAwait(false) ?? throw new NotFoundException();
            var old = await payments.GetAccountAsync(db, request.PaymentId, scope.Condition, scope.AddParametersTo, ct).ConfigureAwait(false) ?? throw new NotFoundException();
            if (!old.RowVer.SequenceEqual(version)) throw new ConcurrencyException();
            if (old.Direction != "Out" || old.Type != "Refund" || old.Status != "Pending")
                throw new BusinessException("Chỉ được sửa tài khoản nhận hoàn của phiếu Out/Refund đang Chờ chi. Vui lòng tải lại phiếu.");
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            if (await payments.UpdateRefundAccountAsync(db, old.Id, version, account, name, now, ct).ConfigureAwait(false) == 0) throw new ConcurrencyException();
            await audit.WriteAsync(db, new(currentSession.RequireSession().UserId, "Payment.UpdateRefundAccount", "Payments", old.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                new { PayeeAccount = ReceiptRules.MaskAccount(old.Account), PayeeName = old.Name is null ? null : "[Đã ẩn]" },
                new { PayeeAccount = ReceiptRules.MaskAccount(account), PayeeName = name is null ? null : "[Đã ẩn]" }, null, machineName), ct).ConfigureAwait(false);
            var next = await payments.GetAccountAsync(db, old.Id, scope.Condition, scope.AddParametersTo, ct).ConfigureAwait(false) ?? throw new NotFoundException();
            return new RefundAccountResult(next.Id, next.Name, ReceiptRules.MaskAccount(next.Account)!, next.RowVer);
        }, cancellationToken);
    }
}
