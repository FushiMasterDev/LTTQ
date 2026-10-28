using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Repositories;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.DTO.Staff;

namespace Homestay.BLL.Services;

/// <summary>Phần quản lý phiên: xem phiên đang mở, lịch sử phiên và buộc đăng xuất người khác.</summary>
public sealed partial class SessionService
{
    public Task<PagedResult<EmployeeSessionItem>> ListOpenSessionsAsync(SessionSearchRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.SessionManage);
        return ListManagedSessionsAsync(request, true, cancellationToken);
    }
    public Task<PagedResult<EmployeeSessionItem>> ListSessionHistoryAsync(SessionSearchRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.SessionManage);
        ArgumentNullException.ThrowIfNull(request);
        if (request.EmployeeId is null or <= 0) throw new BusinessException("Vui lòng chọn nhân viên để xem lịch sử phiên.");
        return ListManagedSessionsAsync(request, false, cancellationToken);
    }
    private Task<PagedResult<EmployeeSessionItem>> ListManagedSessionsAsync(SessionSearchRequest request, bool openOnly, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.Page(request.Page, "LoginAtUtc", "LastSeenAtUtc", "EmployeeCode", "MachineName");
        if (request.EmployeeId is <= 0) throw new BusinessException("Vui lòng chọn nhân viên hợp lệ.");
        // Existing SessionService has no factory parameter. Share the runner's configured read connection.
        return writes.ReadAsync((db, token) => new SessionManagementRepository().ListAsync(db, request, openOnly, token), ct);
    }
    public Task ForceLogoutAsync(ForceLogoutRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.SessionManage);
        ArgumentNullException.ThrowIfNull(request);
        var reason = ListingWriteRules.Reason(request.Reason);
        var session = currentSession.RequireSession();
        if (request.SessionId == session.SessionId) throw new BusinessException("Không được tự buộc đăng xuất phiên của mình. Vui lòng dùng Đăng xuất.");
        return writes.ExecuteAsync(async (db, ct) =>
        {
            var userId = await new SessionManagementRepository().GetUserIdAsync(db, request.SessionId, ct).ConfigureAwait(false) ?? throw new NotFoundException();
            // Lock account before session, matching AuthService/heartbeat's lock order.
            _ = await repository.FindByUserIdAsync(db, userId, ct).ConfigureAwait(false) ?? throw new NotFoundException();
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            if (await repository.EndSessionsAsync(db, userId, request.SessionId, SessionEndReason.ForcedByManager, now, ct).ConfigureAwait(false) == 0) throw new ConcurrencyException();
            await audit.WriteAsync(db, new(session.UserId, "Session.ForceLogout", "UserSessions", request.SessionId.ToString(),
                new { EndedAt = (DateTime?)null }, new { EndedAt = now, EndReason = SessionEndReason.ForcedByManager }, reason, machineName), ct).ConfigureAwait(false);
        }, cancellationToken);
    }
}
