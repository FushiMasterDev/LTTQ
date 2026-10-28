using System.Data;
using Microsoft.Data.SqlClient;

namespace Homestay.DAL.Infrastructure;

/// <summary>Một giao dịch CSDL: mở, commit/rollback và lấy khóa ứng dụng (app lock) để tránh ghi chồng.</summary>
public sealed class UnitOfWork : DbSession, IAsyncDisposable
{
    private bool _committed;
    private bool _disposed;

    private UnitOfWork(SqlConnection connection, SqlTransaction transaction, int timeout) : base(connection, transaction, timeout) { }

    public static async Task<UnitOfWork> BeginAsync(ConnectionFactory factory, CancellationToken cancellationToken)
    {
        var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await SqlErrorTranslator.ExecuteAsync(async ct =>
            {
                var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, ct).ConfigureAwait(false);
                return new UnitOfWork(connection, transaction, factory.CommandTimeoutSeconds);
            }, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    protected override void EnsureUsable()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_committed) throw new InvalidOperationException("Transaction already committed.");
    }

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        EnsureUsable();
        await SqlErrorTranslator.ExecuteAsync(async ct =>
        {
            await Transaction!.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
        _committed = true;
    }

    public async Task AcquireAppLockAsync(string resource, int timeoutMs, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resource);
        if (resource.Length > 255) throw new ArgumentOutOfRangeException(nameof(resource));
        ArgumentOutOfRangeException.ThrowIfLessThan(timeoutMs, -1);
        using var command = CreateCommand("DECLARE @r int; EXEC @r = sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=@timeout; SELECT @r;");
        command.Parameters.AddNVarChar("@resource", 255, resource);
        command.Parameters.AddInt32("@timeout", timeoutMs);
        var result = (int)(await ScalarAsync(command, cancellationToken).ConfigureAwait(false))!;
        if (result < 0) throw new DbLockTimeoutException(resource, result);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            // SqlTransaction.Dispose tự rollback nếu chưa commit, kể cả khi token đã bị hủy.
            await Transaction!.DisposeAsync().ConfigureAwait(false);
        }
        finally { await Connection.DisposeAsync().ConfigureAwait(false); }
    }
}
