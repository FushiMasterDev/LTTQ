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
    public Task<T> ExecuteAsync<T>(Func<UnitOfWork, CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        BusinessErrorTranslator.ExecuteAsync(async ct =>
        {
            var session = currentSession.RequireSession();
            for (var retries = 0; ; retries++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var uow = await UnitOfWork.BeginAsync(factory, ct).ConfigureAwait(false);
                    await using var transactionScope = uow.ConfigureAwait(false);
                    await sessionValidator.ValidateAsync(session, uow, ct).ConfigureAwait(false);
                    var result = await operation(uow, ct).ConfigureAwait(false);
                    await uow.CommitAsync(ct).ConfigureAwait(false);
                    return result;
                }
                catch (DbDeadlockException) when (retries < 3)
                {
                    // Chỉ deadlock mới được gửi lại: 1 lần đầu + tối đa 3 lần thử lại.
                    await Task.Delay(Random.Shared.Next(100, 301), ct).ConfigureAwait(false);
                }
            }
        }, cancellationToken);

    public async Task ExecuteAsync(Func<UnitOfWork, CancellationToken, Task> operation, CancellationToken cancellationToken)
    {
        await ExecuteAsync(async (uow, ct) =>
        {
            await operation(uow, ct).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }
}
