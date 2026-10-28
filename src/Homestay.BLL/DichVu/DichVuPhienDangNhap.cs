using Homestay.BLL.Infrastructure;
using Homestay.BLL.Security;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Auth;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Services;

/// <summary>Dịch vụ phiên đăng nhập của chính mình: giữ phiên sống (heartbeat), đổi mật khẩu, mở khóa màn hình và đăng xuất.</summary>
public sealed partial class SessionService(Authorizer authorizer, ICurrentSession currentSession, WriteOperationRunner writes,
    WriteSessionValidator validator, AuthRepository repository, DbClock clock, SettingsProvider settings, PasswordHasher hasher,
    AuditWriter audit, string machineName)
{
    public async Task<bool> HeartbeatAsync(CancellationToken cancellationToken = default)
    {
        try { authorizer.Require(Permission.AccountSelfManage); }
        catch (SessionExpiredException) { return false; }
        var session = currentSession.RequireSession();
        return await writes.ExecuteMaintenanceAsync(async (db, ct) =>
        {
            if (!await validator.IsValidAsync(session, db, ct).ConfigureAwait(false)) return false;
            return await repository.HeartbeatAsync(db, session, await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false), ct).ConfigureAwait(false) == 1;
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.AccountSelfManage);
        ArgumentNullException.ThrowIfNull(request);
        var session = currentSession.RequireSession();
        PasswordHasher.ValidatePolicy(request.NewPassword, session.EmployeeCode);
        if (request.NewPassword != request.ConfirmPassword) throw new BusinessException("Mật khẩu nhập lại không khớp. Vui lòng nhập lại mật khẩu mới.");
        if (request.NewPassword == request.OldPassword) throw new BusinessException("Mật khẩu mới phải khác mật khẩu cũ. Vui lòng chọn mật khẩu khác.");
        var stamp = await writes.ExecuteAsync(async (db, ct) =>
        {
            var account = await repository.FindByUserIdAsync(db, session.UserId, ct).ConfigureAwait(false) ?? throw new SessionExpiredException();
            if (!hasher.Verify(request.OldPassword, account.PasswordHash).IsValid)
                throw new BusinessException("Mật khẩu cũ không đúng. Vui lòng nhập lại.");
            var nextStamp = Guid.NewGuid();
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            if (await repository.ChangePasswordAsync(db, account, hasher.Hash(request.NewPassword), nextStamp, now, ct).ConfigureAwait(false) == 0)
                throw new ConcurrencyException();
            await audit.WriteAsync(db, new(session.UserId, "Auth.ChangePassword", "Users", session.UserId.ToString(),
                new { account.Identity.MustChangePassword }, new { MustChangePassword = false }, null, machineName), ct).ConfigureAwait(false);
            return nextStamp;
        }, cancellationToken).ConfigureAwait(false);
        // Chỉ đổi bộ nhớ sau commit; schema không cho sửa stamp trên UserSessions.
        currentSession.Set(session with { SecurityStamp = stamp, MustChangePassword = false });
    }

    public async Task UnlockScreenAsync(string password, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.AccountSelfManage);
        var session = currentSession.RequireSession();
        var error = await writes.ExecuteAsync(async (db, ct) =>
        {
            var account = await repository.FindByUserIdAsync(db, session.UserId, ct).ConfigureAwait(false) ?? throw new SessionExpiredException();
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            if (account.LockoutEndUtc is { } end && end > now)
            {
                await audit.WriteAsync(db, new(session.UserId, "Auth.LoginFailed", "Users", session.EmployeeCode[..Math.Min(50, session.EmployeeCode.Length)], null, null, null, machineName), ct).ConfigureAwait(false);
                return LoginErrors.LockedUntil(end, now);
            }
            if (hasher.Verify(password, account.PasswordHash).IsValid)
            {
                if (await repository.SetFailureAsync(db, account, 0, null, now, ct).ConfigureAwait(false) == 0) throw new ConcurrencyException();
                await audit.WriteAsync(db, new(session.UserId, "Auth.UnlockScreen", "UserSessions", session.SessionId.ToString(), null, null, null, machineName), ct).ConfigureAwait(false);
                return null;
            }
            var locked = await LoginAttemptRecorder.RecordAsync(repository, settings, db, account, now, ct).ConfigureAwait(false);
            if (locked) await repository.EndSessionsAsync(db, session.UserId, session.SessionId, SessionEndReason.Idle, now, ct).ConfigureAwait(false);
            await audit.WriteAsync(db, new(session.UserId, "Auth.LoginFailed", "Users", session.EmployeeCode[..Math.Min(50, session.EmployeeCode.Length)], null, null, null, machineName), ct).ConfigureAwait(false);
            return LoginErrors.InvalidCredentials;
        }, cancellationToken).ConfigureAwait(false);
        if (error is not null) throw new BusinessException(error);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        try { authorizer.Require(Permission.AccountSelfManage); }
        catch (SessionExpiredException) { currentSession.Clear(); return; }
        var session = currentSession.RequireSession();
        await writes.ExecuteMaintenanceAsync(async (db, ct) =>
        {
            // Cùng thứ tự khóa Users → UserSessions như các thao tác khác.
            await repository.FindByUserIdAsync(db, session.UserId, ct).ConfigureAwait(false);
            var count = await repository.EndSessionsAsync(db, session.UserId, session.SessionId, SessionEndReason.Logout,
                await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            if (count > 0) await audit.WriteAsync(db, new(session.UserId, "Auth.Logout", "UserSessions", session.SessionId.ToString(), null, null, null, machineName), ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
        currentSession.Clear();
    }
}
