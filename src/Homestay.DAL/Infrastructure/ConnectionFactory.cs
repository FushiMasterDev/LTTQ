using Microsoft.Data.SqlClient;

namespace Homestay.DAL.Infrastructure;

public sealed class ConnectionFactory
{
    private readonly string _connectionString;
    public int CommandTimeoutSeconds { get; }

    public ConnectionFactory(string connectionString, int commandTimeoutSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(commandTimeoutSeconds);
        _connectionString = connectionString;
        CommandTimeoutSeconds = commandTimeoutSeconds;
    }

    public Task<SqlConnection> OpenAsync(CancellationToken cancellationToken) => SqlErrorTranslator.ExecuteAsync(async ct =>
    {
        var connection = new SqlConnection(_connectionString);
        try
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }, cancellationToken);

    // Dành cho truy vấn đọc; delegate không được giữ session sau khi trả về.
    public async Task<T> ReadAsync<T>(Func<DbSession, CancellationToken, Task<T>> query, CancellationToken cancellationToken)
    {
        var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var connectionScope = connection.ConfigureAwait(false);
        return await SqlErrorTranslator.ExecuteAsync(ct => query(new DbSession(connection, null, CommandTimeoutSeconds), ct), cancellationToken).ConfigureAwait(false);
    }
}
