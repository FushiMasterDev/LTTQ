using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Common;
using Homestay.DTO.Notifications;

namespace Homestay.BLL.Services;

/// <summary>Dịch vụ thông báo: đếm thông báo chưa đọc, xem danh sách, đánh dấu đã đọc.</summary>
public sealed class NotificationService(ICurrentSession currentSession, ConnectionFactory factory, WriteOperationRunner writes,
    NotificationRepository repository, DbClock clock)
{
    public Task<int> CountUnreadAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentSession.RequireSession().UserId;
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync((db, token) => repository.CountUnreadAsync(db, userId, token), ct), cancellationToken);
    }
    public Task<PagedResult<NotificationItem>> ListAsync(PageRequest page, CancellationToken cancellationToken = default)
    {
        var userId = currentSession.RequireSession().UserId;
        StaffInputRules.Page(page, "CreatedAtUtc");
        if (!string.IsNullOrWhiteSpace(page.SearchText)) throw new BusinessException("Danh sách thông báo không hỗ trợ tìm kiếm. Vui lòng để trống từ khóa.");
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync((db, token) => repository.ListAsync(db, userId, page, token), ct), cancellationToken);
    }
    public Task MarkReadAsync(long notificationId, CancellationToken cancellationToken = default)
    {
        var userId = currentSession.RequireSession().UserId;
        return writes.ExecuteAsync(async (db, ct) =>
        {
            if (await repository.MarkReadAsync(db, userId, notificationId, await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false), ct).ConfigureAwait(false) == 0)
                throw new NotFoundException();
        }, cancellationToken);
    }
    public Task MarkAllReadAsync(CancellationToken cancellationToken = default)
    {
        var userId = currentSession.RequireSession().UserId;
        return writes.ExecuteAsync(async (db, ct) =>
        {
            await repository.MarkReadAsync(db, userId, null, await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
        }, cancellationToken);
    }
}
