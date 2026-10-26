using System.Globalization;
using Homestay.BLL.Infrastructure;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Enums;

namespace Homestay.BLL.Jobs;

public sealed class HoldExpiry(HoldExpiryRepository repository, AuditWriter audit, string machineName)
{
    // Caller acquires RoomType:{id} in the same transaction, including the job path.
    public async Task<int> ExpireAsync(UnitOfWork db, int roomTypeId, DateTime now, CancellationToken ct)
    {
        var ids = await repository.ExpireAsync(db, roomTypeId, now, ct).ConfigureAwait(false);
        foreach (var id in ids)
            await audit.WriteAsync(db, new(null, "Booking.Expire", "Bookings", id.ToString(CultureInfo.InvariantCulture),
                new { Status = nameof(BookingStatus.AwaitingPayment) }, new { Status = nameof(BookingStatus.Expired) },
                "Hết thời gian giữ phòng", machineName), ct).ConfigureAwait(false);
        return ids.Count;
    }
}
