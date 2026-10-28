using Homestay.DAL.Infrastructure;

namespace Homestay.BLL.Infrastructure;

// Dùng chung cho đăng nhập chưa có phiên và runner kiểm phiên trước khi ghi.
/// <summary>Lõi chạy giao dịch dùng chung (mở kết nối, commit/rollback, thử lại, dịch lỗi), dùng được cả khi chưa có phiên như lúc đăng nhập.</summary>
internal static class TransactionOperationRunner
{
    internal static Task<T> ExecuteAsync<T>(ConnectionFactory factory, Func<UnitOfWork, CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        BusinessErrorTranslator.ExecuteAsync(async ct =>
        {
            for (var retries = 0; ; retries++)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var uow = await UnitOfWork.BeginAsync(factory, ct).ConfigureAwait(false);
                    await using var transactionScope = uow.ConfigureAwait(false);
                    var result = await operation(uow, ct).ConfigureAwait(false);
                    await uow.CommitAsync(ct).ConfigureAwait(false);
                    return result;
                }
                catch (DbDeadlockException) when (retries < 3)
                {
                    await Task.Delay(Random.Shared.Next(100, 301), ct).ConfigureAwait(false);
                }
            }
        }, cancellationToken);
}
