using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
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

    public Task<BookingDetail> ConfirmOnBehalfAsync(ConfirmOnBehalfRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
    public Task<BookingDetail> RejectOnBehalfAsync(RejectOnBehalfRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
    public Task<BookingDetail> CheckInAsync(StayActionRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
    public Task<BookingDetail> CheckOutAsync(StayActionRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
    public Task<BookingDetail> MarkNoShowAsync(StayActionRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
    public Task<BookingDetail> CancelHomestayUnavailableAsync(CancelHomestayUnavailableRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
