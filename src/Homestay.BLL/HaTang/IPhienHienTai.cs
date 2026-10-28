using Homestay.DTO.Auth;

namespace Homestay.BLL.Infrastructure;

/// <summary>Hợp đồng truy cập phiên đăng nhập hiện tại (người đang dùng, chức vụ, khu vực).</summary>
public interface ICurrentSession
{
    SessionInfo? Session { get; }
    SessionInfo RequireSession();
    void Set(SessionInfo session);
    void Clear();
}

/// <summary>Giữ phiên đăng nhập hiện tại trong bộ nhớ ứng dụng; đặt khi đăng nhập và xóa khi đăng xuất.</summary>
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
