namespace Homestay.DAL.Infrastructure;

public enum DbConstraintKind { PrimaryKey, Unique, ForeignKey, Check }

public sealed class DbConstraintViolationException(string? constraintName, DbConstraintKind kind, int errorNumber, Exception? innerException = null)
    : Exception("Database constraint violation.", innerException)
{
    public string? ConstraintName { get; } = constraintName;
    public DbConstraintKind Kind { get; } = kind;
    public int ErrorNumber { get; } = errorNumber;
}

public sealed class DbDeadlockException(Exception? innerException = null) : Exception("Database deadlock.", innerException)
{
    public int ErrorNumber => 1205;
}

public sealed class DbUnavailableException(int errorNumber, Exception? innerException = null) : Exception("Database unavailable.", innerException)
{
    public int ErrorNumber { get; } = errorNumber;
}

public sealed class DbLockTimeoutException(string resource, int returnCode) : Exception("Application lock acquisition failed.")
{
    public string Resource { get; } = resource;
    public int ReturnCode { get; } = returnCode;
}
