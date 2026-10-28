using Homestay.BLL.Infrastructure;
using Homestay.DTO.Auth;

namespace Homestay.GUI.Workflow;

/// <summary>Hợp đồng các thao tác tài khoản mà luồng đăng nhập cần (đăng nhập, đổi mật khẩu), giúp tách khỏi form để dễ kiểm thử.</summary>
public interface IAccountGateway
{
    SessionInfo? Session { get; }
    bool Can(Permission permission);
    Task<SessionInfo> LoginAsync(LoginRequest request, CancellationToken token = default);
    Task ChangePasswordAsync(ChangePasswordRequest request, CancellationToken token = default);
    Task<bool> HeartbeatAsync(CancellationToken token = default);
    Task UnlockAsync(string password, CancellationToken token = default);
    Task LogoutAsync(CancellationToken token = default);
}

/// <summary>Luồng đăng nhập: gọi đăng nhập và xử lý trường hợp bắt buộc đổi mật khẩu.</summary>
public sealed class LoginFlow(IAccountGateway gateway)
{
    public async Task<SessionInfo?> SignInAsync(LoginRequest request,
        Func<Task<bool>> changeRequiredPassword, CancellationToken token = default)
    {
        var session = await gateway.LoginAsync(request, token);
        if (session.MustChangePassword)
        {
            if (!await changeRequiredPassword())
            {
                await gateway.LogoutAsync(token);
                return null;
            }
            session = gateway.Session ?? throw new SessionExpiredException();
            if (session.MustChangePassword) throw new SessionExpiredException("Cần đổi mật khẩu trước khi tiếp tục.");
        }
        return session;
    }
}
