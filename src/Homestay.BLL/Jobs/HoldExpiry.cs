using System.Globalization;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.BLL.Services;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Jobs;

public sealed class HoldExpiry(HoldExpiryRepository repository, AuditWriter audit, string machineName)
{
    // Caller acquires RoomType:{id} in the same transaction, including the job path.
    public async Task<int> ExpireAsync(UnitOfWork db, int roomTypeId, DateTime now, CancellationToken ct)
    {
        var rows = await repository.FindExpiredAsync(db, roomTypeId, now, ct).ConfigureAwait(false);
        foreach (var row in rows)
            await new BookingStatusWriter(new BookingRepository(), audit).ChangeStatusAsync(db, row.Id, BookingStatus.AwaitingPayment,
                BookingStatus.Expired, row.RowVer, BookingTransitionActor.System, now, new(null, "Booking.Expire", "Bookings", row.Id.ToString(CultureInfo.InvariantCulture),
                new { Status = nameof(BookingStatus.AwaitingPayment) }, new { Status = nameof(BookingStatus.Expired) },
                "Hết thời gian giữ phòng", machineName), ct).ConfigureAwait(false);
        return rows.Count;
    }
}
