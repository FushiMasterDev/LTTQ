using Homestay.BLL.Infrastructure;
using Homestay.DAL.Infrastructure;
using Homestay.DTO.Auth;

namespace Homestay.BLL.Tests;

/// <summary>Kiểm thử bộ chạy thao tác ghi với CSDL thật: thử lại toàn bộ giao dịch khi lỗi và rollback mỗi lần thất bại.</summary>
public sealed class WriteOperationRunnerTests
{
    /// <summary>Bộ kiểm phiên giả, đếm số lần được gọi.</summary>
    private sealed class Validator : IWriteSessionValidator
    {
        public int Calls { get; private set; }
        public Task ValidateAsync(SessionInfo session, UnitOfWork unitOfWork, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Task.CompletedTask;
        }
    }

    [SqlFact]
    public async Task RetriesWholeTransactionThreeTimesAndRollsBackEveryFailedAttempt()
    {
        var factory = SqlFixture.Factory();
        var current = new CurrentSession();
        current.Set(new SessionInfo());
        var validator = new Validator();
        var runner = new WriteOperationRunner(factory, current, validator);
        var attempts = 0;
        var transactions = new HashSet<UnitOfWork>();
        var key = Guid.NewGuid().ToString("N")[..30];
        var error = await Assert.ThrowsAsync<BusinessException>(() => runner.ExecuteAsync<int>(async (uow, ct) =>
        {
            attempts++;
            Assert.True(transactions.Add(uow));
            using var insert = uow.CreateCommand("INSERT dbo.Counters (CounterKey, LastValue) VALUES (@key, 1);");
            insert.Parameters.AddVarChar("@key", 30, key);
            await uow.ExecuteAsync(insert, ct);
            // Mô phỏng lỗi đã được DAL phân loại, sau khi thao tác đã ghi dữ liệu.
            throw new DbDeadlockException();
        }, SqlFixture.Token));
        Assert.IsType<DbDeadlockException>(error.InnerException);
        Assert.Equal(4, attempts);
        Assert.Equal(4, validator.Calls);
        var count = await factory.ReadAsync(async (db, ct) =>
        {
            using var read = db.CreateCommand("SELECT COUNT(*) FROM dbo.Counters WHERE CounterKey=@key;");
            read.Parameters.AddVarChar("@key", 30, key);
            return (int)(await db.ScalarAsync(read, ct))!;
        }, SqlFixture.Token);
        Assert.Equal(0, count);
    }
}
