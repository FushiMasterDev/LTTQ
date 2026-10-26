using Homestay.BLL.Infrastructure;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;

namespace Homestay.BLL.Security;

// Kết quả sai phải được commit trước khi service ném lỗi cho GUI.
internal static class LoginAttemptRecorder
{
    internal static async Task<bool> RecordAsync(AuthRepository repository, SettingsProvider settings, UnitOfWork db, AuthAccount account, DateTime now, CancellationToken ct)
    {
        var maximum = await settings.GetLoginMaxFailedAsync(ct, db).ConfigureAwait(false);
        var minutes = await settings.GetLoginLockoutMinutesAsync(ct, db).ConfigureAwait(false);
        if (maximum <= 0 || minutes <= 0 || minutes > (DateTime.MaxValue - now).TotalMinutes)
            throw new BusinessException("Cấu hình khóa tạm không hợp lệ. Vui lòng báo Quản lý kiểm tra Login.MaxFailed và Login.LockoutMinutes.");
        var locked = account.FailedLoginCount >= maximum - 1;
        if (await repository.SetFailureAsync(db, account, locked ? 0 : checked(account.FailedLoginCount + 1),
            locked ? now.AddMinutes(minutes) : null, now, ct).ConfigureAwait(false) == 0) throw new ConcurrencyException();
        return locked;
    }
}
