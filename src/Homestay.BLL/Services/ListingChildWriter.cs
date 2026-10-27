using System.Globalization;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.BLL.Security;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Enums;
using Homestay.DTO.Homestays;

namespace Homestay.BLL.Services;

// Room/contract writes share the parent lock and the existing M4.2A parent UPDATE.
// The runner returns the reread DTO only after committing the whole operation.
internal sealed class ListingChildWriter(Authorizer authorizer, DataScope scope, ConnectionFactory factory, DbClock clock)
{
    internal void Require(Permission permission)
    {
        authorizer.Require(permission);
        if (authorizer.GetAccess(permission) == AccessLevel.ReadOnly) throw new ForbiddenException();
    }

    internal Task<T> ExecuteAsync<T>(Func<UnitOfWork, CancellationToken, Task<int>> locateHome, byte[] rowVer,
        Permission permission, string action,
        Func<UnitOfWork, HomestayItem, DateTime, CancellationToken, Task<(int Id, object? Before, object? After)>> change,
        Func<UnitOfWork, int, int, DateTime, CancellationToken, Task<T>> read, CancellationToken cancellationToken, string? note = null)
    {
        StaffInputRules.RowVer(rowVer); var version = rowVer.ToArray();
        var current = scope.CurrentSession;
        var runner = new WriteOperationRunner(factory, current, new WriteSessionValidator(new AuthRepository()));
        return runner.ExecuteAsync(async (db, ct) =>
        {
            var home = await locateHome(db, ct).ConfigureAwait(false);
            var filter = scope.HomestayFilter(); var homes = new HomestayRepository();
            var old = await homes.GetForUpdateAsync(db, home, filter.Condition, filter.AddParametersTo, ct).ConfigureAwait(false) ?? throw new NotFoundException();
            if (!old.RowVer.SequenceEqual(version)) throw new ConcurrencyException();
            if (authorizer.GetAccess(permission) == AccessLevel.DraftOnly && old.Status != HomestayStatus.Draft) throw new ForbiddenException();
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            var delta = await change(db, old, now, ct).ConfigureAwait(false);
            var session = current.RequireSession();
            if (await homes.WriteAsync(db, old, null, old.Status, old.StatusReason, session.EmployeeId, now, ct).ConfigureAwait(false) == 0)
                throw new ConcurrencyException();
            await new AuditWriter().WriteAsync(db, new(session.UserId, action, "Homestays", home.ToString(CultureInfo.InvariantCulture),
                new { old.Id, Status = old.Status.ToString(), Change = delta.Before },
                new { old.Id, Status = old.Status.ToString(), Change = delta.After }, note, Environment.MachineName), ct).ConfigureAwait(false);
            return await read(db, home, delta.Id, now, ct).ConfigureAwait(false);
        }, cancellationToken);
    }

    internal static async Task LockTypesAsync(UnitOfWork db, int home, CancellationToken ct)
    {
        foreach (var type in (await new RoomRepository().ListTypesAsync(db, home, ct).ConfigureAwait(false)).OrderBy(t => t.Id))
            await db.AcquireAppLockAsync($"RoomType:{type.Id}", 5000, ct).ConfigureAwait(false);
    }
}
