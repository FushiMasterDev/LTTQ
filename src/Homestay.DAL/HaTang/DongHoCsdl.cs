namespace Homestay.DAL.Infrastructure;

/// <summary>Lấy giờ hiện tại (UTC) từ máy chủ CSDL để mọi máy dùng chung một đồng hồ.</summary>
public sealed class DbClock(ConnectionFactory factory)
{
    public Task<DateTime> GetUtcNowAsync(CancellationToken cancellationToken) =>
        factory.ReadAsync((session, ct) => GetUtcNowAsync(session, ct), cancellationToken);

    public async Task<DateTime> GetUtcNowAsync(DbSession session, CancellationToken cancellationToken)
    {
        using var command = session.CreateCommand("SELECT SYSUTCDATETIME();");
        return DateTime.SpecifyKind((DateTime)(await session.ScalarAsync(command, cancellationToken).ConfigureAwait(false))!, DateTimeKind.Utc);
    }
}
