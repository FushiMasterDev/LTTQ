using Homestay.DAL.Infrastructure;

namespace Homestay.BLL.Infrastructure;

/// <summary>Lỗi nghiệp vụ gốc: mang câu báo lỗi tiếng Việt để giao diện hiển thị thẳng cho người dùng.</summary>
public class BusinessException(string message = "Dữ liệu không hợp lệ, vui lòng kiểm tra lại.", Exception? innerException = null) : Exception(message, innerException)
{
    public string? ConstraintName => (InnerException as DbConstraintViolationException)?.ConstraintName;
}

/// <summary>Lỗi không có quyền thực hiện thao tác.</summary>
public sealed class ForbiddenException(string message = "Bạn không có quyền thực hiện thao tác này") : BusinessException(message);
/// <summary>Lỗi không tìm thấy dữ liệu (hoặc dữ liệu nằm ngoài phạm vi được xem).</summary>
public sealed class NotFoundException(string message = "Không tìm thấy dữ liệu") : BusinessException(message);
/// <summary>Lỗi xung đột: dữ liệu đã bị người khác sửa trước (RowVer không khớp), cần tải lại rồi thao tác lại.</summary>
public sealed class ConcurrencyException(string message = "Dữ liệu vừa được người khác thay đổi. Đã tải lại, vui lòng kiểm tra và thao tác lại") : BusinessException(message);
/// <summary>Lỗi phiên đăng nhập đã hết hiệu lực (hết hạn, bị buộc đăng xuất), cần đăng nhập lại.</summary>
public sealed class SessionExpiredException(string message = "Phiên đăng nhập đã hết hiệu lực. Vui lòng đăng nhập lại.") : BusinessException(message);
