using Homestay.DAL.Infrastructure;

namespace Homestay.BLL.Infrastructure;

public class BusinessException(string message = "Dữ liệu không hợp lệ, vui lòng kiểm tra lại.", Exception? innerException = null) : Exception(message, innerException)
{
    public string? ConstraintName => (InnerException as DbConstraintViolationException)?.ConstraintName;
}

public sealed class ForbiddenException(string message = "Bạn không có quyền thực hiện thao tác này") : BusinessException(message);
public sealed class NotFoundException(string message = "Không tìm thấy dữ liệu") : BusinessException(message);
public sealed class ConcurrencyException(string message = "Dữ liệu vừa được người khác thay đổi. Đã tải lại, vui lòng kiểm tra và thao tác lại") : BusinessException(message);
public sealed class SessionExpiredException(string message = "Phiên đăng nhập đã hết hiệu lực. Vui lòng đăng nhập lại.") : BusinessException(message);
