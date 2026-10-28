using Microsoft.Data.SqlClient;

namespace Homestay.DAL.Infrastructure;

/// <summary>Phiên làm việc với CSDL trên một kết nối (có thể kèm giao dịch): tạo lệnh, chạy lệnh, lấy giá trị đơn.</summary>
public class DbSession
{
    protected SqlConnection Connection { get; }
    protected SqlTransaction? Transaction { get; }
    private readonly int _commandTimeoutSeconds;

    internal DbSession(SqlConnection connection, SqlTransaction? transaction, int commandTimeoutSeconds)
    {
        Connection = connection;
        Transaction = transaction;
        _commandTimeoutSeconds = commandTimeoutSeconds;
    }

    protected virtual void EnsureUsable() { }

    public SqlCommand CreateCommand(string sql)
    {
        EnsureUsable();
        return new(sql, Connection, Transaction) { CommandTimeout = _commandTimeoutSeconds };
    }

    // Bao cả ExecuteReader và ReadAsync trong chỗ dịch lỗi chung.
    public Task<T> RunAsync<T>(SqlCommand command, Func<SqlCommand, CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
    {
        EnsureUsable();
        if (command.Connection != Connection || command.Transaction != Transaction)
            throw new ArgumentException("Command must belong to this session.", nameof(command));
        return SqlErrorTranslator.ExecuteAsync(ct => operation(command, ct), cancellationToken);
    }

    public Task<int> ExecuteAsync(SqlCommand command, CancellationToken cancellationToken) =>
        RunAsync(command, (cmd, ct) => cmd.ExecuteNonQueryAsync(ct), cancellationToken);

    public Task<object?> ScalarAsync(SqlCommand command, CancellationToken cancellationToken) =>
        RunAsync(command, (cmd, ct) => cmd.ExecuteScalarAsync(ct), cancellationToken);

    public Task<List<T>> QueryAsync<T>(SqlCommand command, Func<SqlDataReader, T> map, CancellationToken cancellationToken) =>
        RunAsync(command, async (cmd, ct) =>
        {
            var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
            await using var readerScope = reader.ConfigureAwait(false);
            var result = new List<T>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false)) result.Add(map(reader));
            return result;
        }, cancellationToken);
}
