using Homestay.BLL.Infrastructure;
using Homestay.BLL.Security;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Auth;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Services;

public sealed class AuthService(ConnectionFactory factory, AuthRepository repository, DbClock clock, SettingsProvider settings,
    PasswordHasher hasher, AuditWriter audit, ICurrentSession currentSession)
{
    private const string DummyHash = "PBKDF2-SHA256$600000$AAAAAAAAAAAAAAAAAAAAAA==$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    public async Task<SessionInfo> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var username = (request.Username ?? "").Trim();
        var machine = (request.MachineName ?? "").Trim();
        if (machine.Length > 100 || (request.AppVersion ?? "").Length > 20)
            throw new BusinessException("Tên máy hoặc phiên bản ứng dụng quá dài. Vui lòng kiểm tra cấu hình ứng dụng.");
        var result = await TransactionOperationRunner.ExecuteAsync(factory, async (db, ct) =>
        {
            // Không cắt tên quá dài thành một tên tài khoản hợp lệ.
            var account = username.Length is > 0 and <= 50
                ? await repository.FindByUsernameAsync(db, username, ct).ConfigureAwait(false) : null;
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            string? error = null;
            if (account?.LockoutEndUtc is { } end && end > now) error = LoginErrors.LockedUntil(end, now);
            else
            {
                // Vẫn băm khi không có tài khoản để thời gian phản hồi không lộ mã nhân viên có tồn tại (spec 3.10).
                var verification = hasher.Verify(request.Password, account?.PasswordHash ?? DummyHash);
                if (account is null) verification = default;
                error = LoginErrors.ForCredentials(account is not null, verification.IsValid, account?.Status ?? default, account?.EmploymentStatus ?? default);
                if (!verification.IsValid)
                {
                    if (account is not null) await LoginAttemptRecorder.RecordAsync(repository, settings, db, account, now, ct).ConfigureAwait(false);
                }
                else if (error is null)
                {
                    if (await repository.SetLoginSuccessAsync(db, account!, verification.NeedsRehash ? hasher.Hash(request.Password) : null, now, ct).ConfigureAwait(false) == 0)
                        throw new ConcurrencyException();
                    await repository.EndSessionsAsync(db, account!.Identity.UserId, null, SessionEndReason.Replaced, now, ct).ConfigureAwait(false);
                    var id = await repository.InsertSessionAsync(db, account.Identity.UserId, machine, request.AppVersion ?? "", now, ct).ConfigureAwait(false);
                    var areas = await repository.GetAreaIdsAsync(db, account.Identity.EmployeeId, ct).ConfigureAwait(false);
                    var session = account.Identity with { SessionId = id, LoginAtUtc = now, AreaIds = areas };
                    await audit.WriteAsync(db, new(session.UserId, "Auth.Login", "Users", session.UserId.ToString(), null, new { session.SessionId }, null, machine), ct).ConfigureAwait(false);
                    return (Session: (SessionInfo?)session, Error: (string?)null);
                }
            }
            await audit.WriteAsync(db, new(account?.Identity.UserId, "Auth.LoginFailed", "Users", username[..Math.Min(50, username.Length)],
                null, null, null, machine), ct).ConfigureAwait(false);
            return (Session: (SessionInfo?)null, Error: error);
        }, cancellationToken).ConfigureAwait(false);
        if (result.Error is not null) throw new BusinessException(result.Error);
        currentSession.Set(result.Session!);
        return currentSession.RequireSession();
    }
}
