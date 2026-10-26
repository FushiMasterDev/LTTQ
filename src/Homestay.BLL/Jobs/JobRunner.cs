using Homestay.BLL.Infrastructure;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;

namespace Homestay.BLL.Jobs;

public sealed class JobRunner(ConnectionFactory factory, DbClock clock, HoldExpiryRepository repository, HoldExpiry expiry)
{
    // GUI calls only after login, every 60 seconds. No user session is needed by system work.
    // Jobs is Transaction-owned: one transaction wraps the complete run and releases every lock on disposal.
    public Task<int> RunOnceAsync(CancellationToken cancellationToken = default) =>
        TransactionOperationRunner.ExecuteAsync(factory, async (db, ct) =>
        {
            try { await db.AcquireAppLockAsync("Jobs", 0, ct).ConfigureAwait(false); }
            catch (DbLockTimeoutException ex) when (ex.Resource == "Jobs" && ex.ReturnCode == -1) { return 0; }
            var types = await repository.FindRoomTypesAsync(db, await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            var count = 0;
            foreach (var type in types) // SQL orders Id ascending, also the order used by multi-type writers.
            {
                await db.AcquireAppLockAsync($"RoomType:{type}", 5000, ct).ConfigureAwait(false);
                count += await expiry.ExpireAsync(db, type, await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false), ct).ConfigureAwait(false);
            }
            return count;
        }, cancellationToken);
}
