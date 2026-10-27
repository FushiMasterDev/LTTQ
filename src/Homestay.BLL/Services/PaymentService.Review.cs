using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DTO.Bookings;
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
        => throw new NotImplementedException();
    public Task<BookingDetail> RejectReceiptAsync(RejectReceiptRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
    public Task<PaymentQueueItem> RecordRefundPaidAsync(RecordRefundPaidRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
