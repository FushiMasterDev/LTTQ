using Homestay.BLL.Infrastructure;
using Homestay.DAL.Infrastructure;
using Homestay.DTO.Auth;

namespace Homestay.Rules.Tests;

public sealed class WriteOperationRunnerTests
{
    private sealed class UnusedValidator : IWriteSessionValidator
    {
        public Task ValidateAsync(SessionInfo session, UnitOfWork unitOfWork, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Không được mở database trong test này.");
    }

    [Fact]
    public async Task MissingSessionIsRejectedBeforeOpeningConnection()
    {
        var runner = new WriteOperationRunner(new ConnectionFactory("Server=unused;Database=unused", 30), new CurrentSession(), new UnusedValidator());
        await Assert.ThrowsAsync<SessionExpiredException>(() => runner.ExecuteAsync<int>((_, _) => Task.FromResult(1), CancellationToken.None));
    }

    [Fact]
    public async Task CancellationIsNotTranslatedOrRetried()
    {
        var session = new CurrentSession();
        session.Set(new SessionInfo());
        var runner = new WriteOperationRunner(new ConnectionFactory("Server=unused;Database=unused", 30), session, new UnusedValidator());
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.ExecuteAsync<int>((_, _) => Task.FromResult(1), source.Token));
    }
}
