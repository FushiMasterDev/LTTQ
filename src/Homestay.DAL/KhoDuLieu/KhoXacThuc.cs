using Homestay.DAL.Infrastructure;
using Homestay.DTO.Auth;
using Homestay.DTO.Enums;

namespace Homestay.DAL.Repositories;

/// <summary>Thông tin tài khoản phục vụ đăng nhập: danh tính, mật khẩu băm, trạng thái, số lần sai, thời điểm hết khóa.</summary>
public sealed record AuthAccount
{
    public SessionInfo Identity { get; init; } = new();
    public string PasswordHash { get; init; } = "";
    public UserStatus Status { get; init; }
    public EmploymentStatus EmploymentStatus { get; init; }
    public int FailedLoginCount { get; init; }
    public DateTime? LockoutEndUtc { get; init; }
    public byte[] RowVer { get; init; } = [];
}

/// <summary>Truy vấn đăng nhập và phiên: tìm tài khoản, ghi lần sai/thành công, mở/đóng phiên, heartbeat, đổi mật khẩu.</summary>
public sealed class AuthRepository
{
    private const string Columns = """
        SELECT u.Id AS UserId, e.Id AS EmployeeId, e.EmployeeCode, e.FullName, e.Position, e.HasAllAreas,
            u.SecurityStamp, u.MustChangePassword, u.PasswordHash, u.Status, e.EmploymentStatus,
            u.FailedLoginCount, u.LockoutEndUtc, u.RowVer
        FROM dbo.Users u WITH (UPDLOCK, HOLDLOCK)
        JOIN dbo.Employees e WITH (HOLDLOCK) ON e.UserId=u.Id
        """;

    public Task<AuthAccount?> FindByUsernameAsync(UnitOfWork db, string username, CancellationToken ct) =>
        FindAsync(db, Columns + " WHERE u.Username COLLATE Vietnamese_CI_AS=@username;", username, null, ct);
    public Task<AuthAccount?> FindByUserIdAsync(UnitOfWork db, int userId, CancellationToken ct) =>
        FindAsync(db, Columns + " WHERE u.Id=@id;", null, userId, ct);

    private static async Task<AuthAccount?> FindAsync(UnitOfWork db, string sql, string? username, int? userId, CancellationToken ct)
    {
        using var command = db.CreateCommand(sql);
        if (userId.HasValue) command.Parameters.AddInt32("@id", userId);
        else command.Parameters.AddVarChar("@username", 50, username);
        var rows = await db.QueryAsync(command, r => new AuthAccount
        {
            Identity = new SessionInfo
            {
                UserId = r.GetInt32("UserId"), EmployeeId = r.GetInt32("EmployeeId"), EmployeeCode = r.GetString("EmployeeCode"),
                FullName = r.GetString("FullName"), Position = r.GetEnum<Position>("Position"), HasAllAreas = r.GetBoolean("HasAllAreas"),
                SecurityStamp = r.GetGuid("SecurityStamp"), MustChangePassword = r.GetBoolean("MustChangePassword")
            },
            PasswordHash = r.GetString("PasswordHash"), Status = r.GetEnum<UserStatus>("Status"),
            EmploymentStatus = r.GetEnum<EmploymentStatus>("EmploymentStatus"), FailedLoginCount = r.GetInt32("FailedLoginCount"),
            LockoutEndUtc = r.GetNullableUtcDateTime("LockoutEndUtc"), RowVer = r.GetRowVersion("RowVer")
        }, ct).ConfigureAwait(false);
        return rows.SingleOrDefault();
    }

    public async Task<IReadOnlyList<int>> GetAreaIdsAsync(DbSession db, int employeeId, CancellationToken ct)
    {
        using var command = db.CreateCommand("SELECT AreaId FROM dbo.EmployeeAreas WHERE EmployeeId=@id ORDER BY AreaId;");
        command.Parameters.AddInt32("@id", employeeId);
        return await db.QueryAsync(command, r => r.GetInt32("AreaId"), ct).ConfigureAwait(false);
    }

    public async Task<bool> IsSessionOpenAsync(UnitOfWork db, long sessionId, int userId, CancellationToken ct)
    {
        using var command = db.CreateCommand("SELECT COUNT(*) FROM dbo.UserSessions WITH (UPDLOCK, HOLDLOCK) WHERE Id=@session AND UserId=@user AND EndedAt IS NULL;");
        command.Parameters.AddInt64("@session", sessionId);
        command.Parameters.AddInt32("@user", userId);
        return (int)(await db.ScalarAsync(command, ct).ConfigureAwait(false))! == 1;
    }

    public async Task<int> SetFailureAsync(UnitOfWork db, AuthAccount account, int failedCount, DateTime? lockoutEnd, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            UPDATE dbo.Users SET FailedLoginCount=@failed, LockoutEndUtc=@lockout, UpdatedAt=@now
            WHERE Id=@id AND RowVer=@version;
            """);
        command.Parameters.AddInt32("@id", account.Identity.UserId);
        command.Parameters.AddInt32("@failed", failedCount);
        command.Parameters.AddUtcDateTime("@lockout", lockoutEnd);
        command.Parameters.AddUtcDateTime("@now", now);
        command.Parameters.AddRowVersion("@version", account.RowVer);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<int> SetLoginSuccessAsync(UnitOfWork db, AuthAccount account, string? rehash, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            UPDATE dbo.Users SET FailedLoginCount=0, LockoutEndUtc=NULL, LastLoginAt=@now,
                PasswordHash=COALESCE(@hash, PasswordHash), UpdatedAt=@now
            WHERE Id=@id AND Status='Active' AND RowVer=@version;
            """);
        command.Parameters.AddInt32("@id", account.Identity.UserId);
        command.Parameters.AddVarChar("@hash", 255, rehash);
        command.Parameters.AddUtcDateTime("@now", now);
        command.Parameters.AddRowVersion("@version", account.RowVer);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<long> InsertSessionAsync(UnitOfWork db, int userId, string machine, string version, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            INSERT dbo.UserSessions (UserId, MachineName, AppVersion, LoginAt, LastSeenAt)
            OUTPUT INSERTED.Id VALUES (@user, @machine, @version, @now, @now);
            """);
        command.Parameters.AddInt32("@user", userId);
        command.Parameters.AddNVarChar("@machine", 100, machine);
        command.Parameters.AddVarChar("@version", 20, version);
        command.Parameters.AddUtcDateTime("@now", now);
        return (long)(await db.ScalarAsync(command, ct).ConfigureAwait(false))!;
    }

    public async Task<int> EndSessionsAsync(UnitOfWork db, int userId, long? sessionId, SessionEndReason reason, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            UPDATE dbo.UserSessions SET EndedAt=@now, EndReason=@reason
            WHERE UserId=@user AND (@session IS NULL OR Id=@session) AND EndedAt IS NULL;
            """);
        command.Parameters.AddInt32("@user", userId);
        command.Parameters.AddInt64("@session", sessionId);
        command.Parameters.AddEnum("@reason", 20, (SessionEndReason?)reason);
        command.Parameters.AddUtcDateTime("@now", now);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<int> HeartbeatAsync(UnitOfWork db, SessionInfo session, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("UPDATE dbo.UserSessions SET LastSeenAt=@now WHERE Id=@session AND UserId=@user AND EndedAt IS NULL;");
        command.Parameters.AddInt64("@session", session.SessionId);
        command.Parameters.AddInt32("@user", session.UserId);
        command.Parameters.AddUtcDateTime("@now", now);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<int> ChangePasswordAsync(UnitOfWork db, AuthAccount account, string hash, Guid stamp, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            UPDATE dbo.Users SET PasswordHash=@hash, PasswordChangedAt=@now, MustChangePassword=0,
                SecurityStamp=@stamp, FailedLoginCount=0, LockoutEndUtc=NULL, UpdatedAt=@now
            WHERE Id=@id AND Status='Active' AND RowVer=@version;
            """);
        command.Parameters.AddInt32("@id", account.Identity.UserId);
        command.Parameters.AddVarChar("@hash", 255, hash);
        command.Parameters.AddGuid("@stamp", stamp);
        command.Parameters.AddUtcDateTime("@now", now);
        command.Parameters.AddRowVersion("@version", account.RowVer);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }
}
