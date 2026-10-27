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
    {
        var session = currentSession.RequireSession();
        return TransactionOperationRunner.ExecuteAsync(factory, async (uow, ct) =>
        {
            await sessionValidator.ValidateAsync(session, uow, ct).ConfigureAwait(false);
            return await operation(uow, ct).ConfigureAwait(false);
        }, cancellationToken);
    }

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
