namespace Homestay.DAL.Infrastructure;

/// <summary>Loại ràng buộc CSDL bị vi phạm: khóa chính, duy nhất, khóa ngoại hay CHECK.</summary>
public enum DbConstraintKind { PrimaryKey, Unique, ForeignKey, Check }

/// <summary>Lỗi vi phạm ràng buộc CSDL, kèm tên và loại ràng buộc để tầng trên dịch thành câu dễ hiểu.</summary>
public sealed class DbConstraintViolationException(string? constraintName, DbConstraintKind kind, int errorNumber, Exception? innerException = null)
    : Exception("Database constraint violation.", innerException)
{
    public string? ConstraintName { get; } = constraintName;
    public DbConstraintKind Kind { get; } = kind;
    public int ErrorNumber { get; } = errorNumber;
}

/// <summary>Lỗi deadlock trong CSDL; tầng trên sẽ thử lại thao tác.</summary>
public sealed class DbDeadlockException(Exception? innerException = null) : Exception("Database deadlock.", innerException)
{
    public int ErrorNumber => 1205;
}

/// <summary>Lỗi không kết nối được CSDL (máy chủ tắt, mạng lỗi, hết thời gian chờ).</summary>
public sealed class DbUnavailableException(int errorNumber, Exception? innerException = null) : Exception("Database unavailable.", innerException)
{
    public int ErrorNumber { get; } = errorNumber;
}

/// <summary>Lỗi không lấy được khóa ứng dụng (app lock) trong thời gian cho phép.</summary>
public sealed class DbLockTimeoutException(string resource, int returnCode) : Exception("Application lock acquisition failed.")
{
    public string Resource { get; } = resource;
    public int ReturnCode { get; } = returnCode;
}
