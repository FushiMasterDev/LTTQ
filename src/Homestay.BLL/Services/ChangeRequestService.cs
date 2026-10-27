using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Bookings;
using Homestay.DTO.ChangeRequests;
using Homestay.DTO.Common;

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
        => throw new NotImplementedException();
    public Task<CancelRequestDetail> ApproveAsync(ReviewCancelRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
    public Task<CancelRequestDetail> RejectAsync(RejectCancelRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
