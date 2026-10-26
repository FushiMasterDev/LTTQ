using Homestay.DAL.Infrastructure;
using Homestay.DTO.Availability;
using Homestay.DTO.Bookings;
using Homestay.DTO.Enums;

namespace Homestay.DAL.Repositories;

public sealed partial class BookingRepository
{
    public async Task<int?> GetMaxGuestsAsync(DbSession db, int roomTypeId, CancellationToken ct)
    {
        using var command = db.CreateCommand("SELECT MaxGuests FROM dbo.RoomTypes WHERE Id=@type;");
        command.Parameters.AddInt32("@type", roomTypeId);
        return await db.ScalarAsync(command, ct).ConfigureAwait(false) as int?;
    }

    public async Task<bool> IsRoomActiveAsync(UnitOfWork db, int roomId, int roomTypeId, CancellationToken ct)
    {
        using var command = db.CreateCommand("SELECT COUNT(*) FROM dbo.Rooms WHERE Id=@room AND RoomTypeId=@type AND IsActive=1;");
        command.Parameters.AddInt32("@room", roomId);
        command.Parameters.AddInt32("@type", roomTypeId);
        return (int)(await db.ScalarAsync(command, ct).ConfigureAwait(false))! == 1;
    }

    public async Task<int> CountAwaitingAsync(UnitOfWork db, int customerId, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            SELECT COUNT(*) FROM dbo.Bookings b WHERE b.CustomerId=@customer
                AND ((b.Status='AwaitingPayment' AND b.HoldExpiresAt>=@now)
                    OR EXISTS(SELECT 1 FROM dbo.Payments p WHERE p.BookingId=b.Id AND p.Status='Pending'));
            """);
        command.Parameters.AddInt32("@customer", customerId);
        command.Parameters.AddUtcDateTime("@now", now);
        return (int)(await db.ScalarAsync(command, ct).ConfigureAwait(false))!;
    }

    public async Task<int> NextSequenceAsync(UnitOfWork db, string key, CancellationToken ct)
    {
        async Task<int?> IncrementAsync()
        {
            using var update = db.CreateCommand("UPDATE dbo.Counters SET LastValue=LastValue+1,UpdatedAt=SYSUTCDATETIME() OUTPUT inserted.LastValue WHERE CounterKey=@key;");
            update.Parameters.AddVarChar("@key", 30, key);
            return await db.ScalarAsync(update, ct).ConfigureAwait(false) as int?;
        }
        if (await IncrementAsync().ConfigureAwait(false) is { } value) return value;
        try
        {
            using var insert = db.CreateCommand("INSERT dbo.Counters(CounterKey,LastValue) VALUES(@key,1);");
            insert.Parameters.AddVarChar("@key", 30, key);
            await db.ExecuteAsync(insert, ct).ConfigureAwait(false);
            return 1;
        }
        catch (DbConstraintViolationException ex) when (ex.ConstraintName == "PK_Counters" && ex.Kind is DbConstraintKind.PrimaryKey or DbConstraintKind.Unique)
        {
            // Another type's booking initialized today's counter. Increment/read the winning row.
            return await IncrementAsync().ConfigureAwait(false) ?? throw new InvalidOperationException("Counter disappeared.");
        }
    }

    public async Task<int> InsertAsync(UnitOfWork db, CreateBookingRequest request, int customerId, int employeeId,
        AvailabilityCandidate candidate, int roomId, string code, DateTime now, DateTime holdExpiresAt,
        PriceBreakdown price, int policyId, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            INSERT dbo.Bookings(Code,CustomerId,ContactName,ContactPhone,SourceChannel,CreatedByEmployeeId,AssignedEmployeeId,
                HomestayId,RoomTypeId,RoomId,CheckIn,CheckOut,Guests,SpecialRequests,Status,HoldExpiresAt,
                TotalAmount,DepositRate,DepositAmount,DiscountAmount,CommissionRate,CancellationPolicyId,CreatedAt)
            VALUES(@code,@customer,@name,@phone,@source,@employee,@employee,@home,@type,@room,@in,@out,@guests,@special,
                'AwaitingPayment',@hold,@total,@rate,@deposit,0,@commission,@policy,@now);
            SELECT CONVERT(int,SCOPE_IDENTITY());
            """);
        command.Parameters.AddVarChar("@code", 20, code);
        command.Parameters.AddInt32("@customer", customerId);
        command.Parameters.AddNVarChar("@name", 100, request.ContactName);
        command.Parameters.AddVarChar("@phone", 16, request.ContactPhone);
        command.Parameters.AddEnum<SourceChannel>("@source", 20, request.SourceChannel);
        command.Parameters.AddInt32("@employee", employeeId);
        command.Parameters.AddInt32("@home", candidate.HomestayId);
        command.Parameters.AddInt32("@type", request.RoomTypeId);
        command.Parameters.AddInt32("@room", roomId);
        command.Parameters.AddDate("@in", request.CheckIn);
        command.Parameters.AddDate("@out", request.CheckOut);
        command.Parameters.AddInt32("@guests", request.Guests);
        command.Parameters.AddNVarChar("@special", 1000, request.SpecialRequests);
        command.Parameters.AddUtcDateTime("@hold", holdExpiresAt);
        command.Parameters.AddMoney("@total", price.TotalAmount);
        command.Parameters.AddRate("@rate", price.DepositRate);
        command.Parameters.AddMoney("@deposit", price.DepositAmount);
        command.Parameters.AddRate("@commission", candidate.CommissionRate);
        command.Parameters.AddInt32("@policy", policyId);
        command.Parameters.AddUtcDateTime("@now", now);
        return (int)(await db.ScalarAsync(command, ct).ConfigureAwait(false))!;
    }

    public async Task InsertNightsAsync(UnitOfWork db, int bookingId, int roomId, IReadOnlyList<NightlyPrice> nights, CancellationToken ct)
    {
        foreach (var night in nights)
        {
            using var command = db.CreateCommand("""
                INSERT dbo.RoomNights(RoomId,[Date],BookingId) VALUES(@room,@date,@booking);
                INSERT dbo.BookingNights(BookingId,[Date],DayType,Price) VALUES(@booking,@date,@day,@price);
                """);
            command.Parameters.AddInt32("@room", roomId);
            command.Parameters.AddInt32("@booking", bookingId);
            command.Parameters.AddDate("@date", night.Date);
            command.Parameters.AddEnum<DayType>("@day", 10, night.DayType);
            command.Parameters.AddMoney("@price", night.Price);
            await db.ExecuteAsync(command, ct).ConfigureAwait(false);
        }
    }

}
