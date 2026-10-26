using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Services;

/// <summary>One transactional path for state validation, conditional update, ledger release, history and audit.
/// Callers own scope/business checks, notifications and the enclosing transaction.</summary>
public sealed class BookingStatusWriter(BookingRepository repository, AuditWriter audit)
{
    public async Task ChangeStatusAsync(UnitOfWork db, int id, BookingStatus? from, BookingStatus to, byte[]? rowVer,
        BookingTransitionActor actor, DateTime now, AuditEntry entry, CancellationToken ct)
    {
        var transition = BookingStateMachine.Require(from, to, actor, entry.Note);
        if (from is { } previous)
        {
            StaffInputRules.RowVer(rowVer!);
            if (await repository.UpdateStatusAsync(db, id, previous, to, rowVer!, now, entry.Note, ct).ConfigureAwait(false) == 0)
                throw new ConcurrencyException();
            if (to is BookingStatus.Expired or BookingStatus.Cancelled or BookingStatus.Rejected or BookingStatus.NoShow)
                await repository.DeleteRoomNightsAsync(db, id, ct).ConfigureAwait(false);
        }
        // NULL -> AwaitingPayment accompanies the INSERT performed by CreateAsync (FK requires header first).
        await repository.InsertStatusHistoryAsync(db, id, from, to, actor == BookingTransitionActor.System ? ActorType.System : ActorType.Employee,
            entry.UserId, transition.OnBehalfOfOwner, entry.Note, now, ct).ConfigureAwait(false);
        await audit.WriteAsync(db, entry, ct).ConfigureAwait(false);
    }
}
