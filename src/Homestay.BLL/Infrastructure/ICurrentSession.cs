using Homestay.DTO.Auth;

namespace Homestay.BLL.Infrastructure;

public interface ICurrentSession
{
    SessionInfo? Session { get; }
    SessionInfo RequireSession();
    void Set(SessionInfo session);
    void Clear();
}

public sealed class CurrentSession : ICurrentSession
{
    private SessionInfo? _session;
    public SessionInfo? Session => Volatile.Read(ref _session);
    public SessionInfo RequireSession() => Session ?? throw new SessionExpiredException();
    public void Set(SessionInfo session)
    {
        ArgumentNullException.ThrowIfNull(session);
        // Tách danh sách khỏi nguồn có thể bị sửa sau đăng nhập.
        Volatile.Write(ref _session, session with { AreaIds = Array.AsReadOnly(session.AreaIds.ToArray()) });
    }
    public void Clear() => Volatile.Write(ref _session, null);
}
