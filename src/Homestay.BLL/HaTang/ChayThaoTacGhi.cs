using Homestay.DAL.Infrastructure;
using Homestay.DTO.Auth;

namespace Homestay.BLL.Infrastructure;

// M1.4 kiểm tra Users/Employees/UserSessions bằng chính transaction của thao tác ghi.
public interface IWriteSessionValidator
{
    Task ValidateAsync(SessionInfo session, UnitOfWork unitOfWork, CancellationToken cancellationToken);
}

public sealed class WriteOperationRunner(ConnectionFactory factory, ICurrentSession currentSession, IWriteSessionValidator sessionValidator)
{
    public Task<T> ExecuteAsync<T>(Func<UnitOfWork, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        => ExecuteCoreAsync(operation, null, cancellationToken);

    // Manager-changing operations serialize BEFORE taking session/account locks. This avoids
    // two managers each holding their own account while waiting to demote the other.
    internal Task ExecuteSerializedAsync(string resource, Func<UnitOfWork, CancellationToken, Task> operation, CancellationToken cancellationToken) =>
        ExecuteCoreAsync(async (db, ct) => { await operation(db, ct).ConfigureAwait(false); return true; }, resource, cancellationToken);

    private Task<T> ExecuteCoreAsync<T>(Func<UnitOfWork, CancellationToken, Task<T>> operation, string? resource, CancellationToken cancellationToken)
    {
        var session = currentSession.RequireSession();
        return TransactionOperationRunner.ExecuteAsync(factory, async (uow, ct) =>
        {
            if (resource is not null) await uow.AcquireAppLockAsync(resource, 10000, ct).ConfigureAwait(false);
            await sessionValidator.ValidateAsync(session, uow, ct).ConfigureAwait(false);
            return await operation(uow, ct).ConfigureAwait(false);
        }, cancellationToken);
    }

    // Preserve SessionService's established constructor; only BLL can access this read path.
    internal Task<T> ReadAsync<T>(Func<DbSession, CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(operation, ct), cancellationToken);

    // Heartbeat tự kiểm phiên và trả false; logout cần kết thúc được cả phiên không còn hợp lệ.
    internal Task<T> ExecuteMaintenanceAsync<T>(Func<UnitOfWork, CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        TransactionOperationRunner.ExecuteAsync(factory, operation, cancellationToken);

    public async Task ExecuteAsync(Func<UnitOfWork, CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        await ExecuteAsync(async (uow, ct) =>
        {
            await operation(uow, ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }
}
