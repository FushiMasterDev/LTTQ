using Homestay.DAL.Infrastructure;

namespace Homestay.BLL.Infrastructure;

public static class BusinessErrorTranslator
{
    public static Exception Translate(Exception exception) => exception switch
    {
        DbConstraintViolationException e => new BusinessException(ConstraintMessages.GetMessage(e.ConstraintName), e),
        DbUnavailableException e => new BusinessException("Mất kết nối máy chủ dữ liệu, vui lòng thử lại.", e),
        DbLockTimeoutException e => new BusinessException("Hệ thống đang bận xử lý cùng dữ liệu, vui lòng thử lại.", e),
        DbDeadlockException e => new BusinessException("Dữ liệu đang được xử lý đồng thời, vui lòng thử lại.", e),
        _ => exception
    };

    public static async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        try { return await operation(cancellationToken).ConfigureAwait(false); }
        catch (Exception exception)
        {
            var translated = Translate(exception);
            if (ReferenceEquals(exception, translated)) throw;
            throw translated;
        }
    }
}
