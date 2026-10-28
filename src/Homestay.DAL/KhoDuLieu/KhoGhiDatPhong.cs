using Homestay.DAL.Infrastructure;
using Homestay.DTO.Enums;
using Microsoft.Data.SqlClient;

namespace Homestay.DAL.Repositories;

/// <summary>Ảnh chụp đơn tại thời điểm khóa để ghi: trạng thái, người phụ trách, RowVer… dùng để kiểm tra trước khi đổi.</summary>
public sealed record BookingWriteSnapshot(int Id, string Code, BookingStatus Status, int? AssignedEmployeeId,
    DateTime HoldExpiresAtUtc, decimal AmountDue, byte[] RowVer);

// Metadata applied atomically with the conditional status update, never in a second status path.
/// <summary>Các giá trị đi kèm khi đổi trạng thái đơn (hạn xác nhận, hạn giữ phòng, tiền hoàn, phòng…).</summary>
public sealed record BookingStatusMetadata
{
    public DateTime? ConfirmDeadlineAtUtc { get; init; }
    public DateTime? HoldExpiresAtUtc { get; init; }
    public bool IncrementReceiptRejectCount { get; init; }
    public decimal? RefundAmount { get; init; }
    public int? RoomId { get; init; }
}

/// <summary>Truy vấn ghi đơn: khóa đơn, giao người phụ trách, đổi trạng thái, xóa đêm phòng, ghi lịch sử trạng thái.</summary>
public sealed partial class BookingRepository
{
    // PaymentRecord deliberately passes the financial BookingFilter, which includes Accountants at All access.
    public async Task<BookingWriteSnapshot?> GetForUpdateAsync(UnitOfWork db, int id, string scopeCondition,
        Action<SqlCommand> addScope, CancellationToken ct)
    {
        using var command = db.CreateCommand($"""
            SELECT b.Id,b.Code,b.Status,b.AssignedEmployeeId,b.HoldExpiresAt,b.DepositAmount-b.DiscountAmount AS AmountDue,b.RowVer
            FROM dbo.Bookings b WITH (UPDLOCK) JOIN dbo.Homestays h ON h.Id=b.HomestayId
            WHERE b.Id=@id AND ({scopeCondition});
            """);
        command.Parameters.AddInt32("@id", id);
        addScope(command);
        return (await db.QueryAsync(command, r => new BookingWriteSnapshot(r.GetInt32("Id"), r.GetString("Code"),
            r.GetEnum<BookingStatus>("Status"), r.GetNullableInt32("AssignedEmployeeId"), r.GetUtcDateTime("HoldExpiresAt"),
            r.GetDecimal("AmountDue"), r.GetFieldValue<byte[]>(r.GetOrdinal("RowVer"))), ct).ConfigureAwait(false)).SingleOrDefault();
    }

    public async Task<bool> HasActiveDepositAsync(DbSession db, int id, CancellationToken ct)
    {
        using var command = db.CreateCommand("SELECT COUNT(*) FROM dbo.Payments WHERE BookingId=@id AND Type='Deposit' AND Status IN ('Pending','Verified');");
        command.Parameters.AddInt32("@id", id);
        return (int)(await db.ScalarAsync(command, ct).ConfigureAwait(false))! > 0;
    }

    public Task<int> AssignAsync(UnitOfWork db, int id, int employeeId, byte[] rowVer, bool claim, DateTime now, CancellationToken ct) =>
        AssignAsync(db, id, (int?)employeeId, rowVer, claim, now, ct);

    public async Task<int> AssignAsync(UnitOfWork db, int id, int? employeeId, byte[] rowVer, bool claim, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand($"""
            UPDATE dbo.Bookings SET AssignedEmployeeId=@employee,UpdatedAt=@now
            WHERE Id=@id AND RowVer=@version {(claim ? "AND AssignedEmployeeId IS NULL" : "")};
            """);
        command.Parameters.AddInt32("@id", id);
        command.Parameters.AddInt32("@employee", employeeId);
        command.Parameters.AddRowVersion("@version", rowVer);
        command.Parameters.AddUtcDateTime("@now", now);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }

    public async Task<int?> GetAssignableUserAsync(UnitOfWork db, int employeeId, int bookingId, CancellationToken ct)
    {
        using var command = db.CreateCommand($"""
            SELECT e.UserId FROM dbo.Employees e WITH (UPDLOCK) JOIN dbo.Users u WITH (UPDLOCK) ON u.Id=e.UserId
            WHERE e.Id=@id AND {AssignableEmployee} AND {AssignableArea};
            """);
        command.Parameters.AddInt32("@id", employeeId);
        command.Parameters.AddInt32("@booking", bookingId);
        return await db.ScalarAsync(command, ct).ConfigureAwait(false) as int?;
    }

    // The single conditional status update used by services and HoldExpiry.
    public Task<int> UpdateStatusAsync(UnitOfWork db, int id, BookingStatus from, BookingStatus to, byte[] rowVer,
        DateTime now, string? note, CancellationToken ct) => UpdateStatusAsync(db, id, from, to, rowVer, now, note, null, ct);

    public async Task<int> UpdateStatusAsync(UnitOfWork db, int id, BookingStatus from, BookingStatus to, byte[] rowVer,
        DateTime now, string? note, BookingStatusMetadata? metadata, CancellationToken ct)
    {
        metadata ??= new();
        var fields = to switch
        {
            BookingStatus.PaymentSubmitted => ",PaymentSubmittedAt=@now",
            BookingStatus.Cancelled => ",CancelledAt=@now,CancelReason=@note,RefundAmount=@refund",
            BookingStatus.AwaitingPayment when metadata.HoldExpiresAtUtc is not null => ",HoldExpiresAt=@hold",
            BookingStatus.Deposited when metadata.ConfirmDeadlineAtUtc is not null => ",DepositedAt=@now,ConfirmDeadlineAt=@deadline,ConfirmedAt=NULL,ConfirmAlertSentAt=NULL",
            BookingStatus.Confirmed => ",ConfirmedAt=@now",
            BookingStatus.CheckedIn => ",CheckedInAt=@now",
            BookingStatus.Completed => ",CompletedAt=@now",
            // Schema 001 stores rejection reason in CancelReason; event times are in status history.
            BookingStatus.Rejected when metadata.RefundAmount is not null => ",CancelReason=@note,RefundAmount=@refund",
            BookingStatus.NoShow => ",RefundAmount=0",
            BookingStatus.Expired => "",
            _ => throw new ArgumentOutOfRangeException(nameof(to), "Timestamp contract for this transition is not implemented yet.")
        };
        if (metadata.IncrementReceiptRejectCount) fields += ",ReceiptRejectCount=ReceiptRejectCount+1";
        if (metadata.RoomId is not null) fields += ",RoomId=@room";
        // Keep the job's final eligibility guard as well as the optimistic status/version predicate.
        var eligibility = from == BookingStatus.AwaitingPayment && to == BookingStatus.Expired
            ? "AND b.HoldExpiresAt<@now AND NOT EXISTS(SELECT 1 FROM dbo.Payments p WHERE p.BookingId=b.Id AND p.Type='Deposit' AND p.Status='Pending')"
            : "";
        using var command = db.CreateCommand($"UPDATE b SET Status=@to,UpdatedAt=@now{fields} FROM dbo.Bookings b WHERE b.Id=@id AND b.Status=@from AND b.RowVer=@version {eligibility};");
        command.Parameters.AddInt32("@id", id);
        command.Parameters.AddEnum("@from", 20, (BookingStatus?)from);
        command.Parameters.AddEnum("@to", 20, (BookingStatus?)to);
        command.Parameters.AddRowVersion("@version", rowVer);
        command.Parameters.AddUtcDateTime("@now", now);
        command.Parameters.AddNVarChar("@note", 500, note);
        command.Parameters.AddUtcDateTime("@hold", metadata.HoldExpiresAtUtc);
        command.Parameters.AddUtcDateTime("@deadline", metadata.ConfirmDeadlineAtUtc);
        command.Parameters.AddMoney("@refund", metadata.RefundAmount);
        command.Parameters.AddInt32("@room", metadata.RoomId);
        return await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }

    public async Task DeleteRoomNightsAsync(UnitOfWork db, int id, CancellationToken ct)
    {
        using var command = db.CreateCommand("DELETE dbo.RoomNights WHERE BookingId=@id;");
        command.Parameters.AddInt32("@id", id);
        await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }

    public async Task InsertStatusHistoryAsync(UnitOfWork db, int id, BookingStatus? from, BookingStatus to, ActorType actorType,
        int? userId, bool onBehalfOfOwner, string? note, DateTime now, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            INSERT dbo.BookingStatusHistories(BookingId,FromStatus,ToStatus,ActorType,ActorUserId,OnBehalfOfOwner,Note,ChangedAt)
            VALUES(@id,@from,@to,@actor,@user,@behalf,@note,@now);
            """);
        command.Parameters.AddInt32("@id", id);
        command.Parameters.AddEnum("@from", 20, from);
        command.Parameters.AddEnum("@to", 20, (BookingStatus?)to);
        command.Parameters.AddEnum("@actor", 20, (ActorType?)actorType);
        command.Parameters.AddInt32("@user", userId);
        command.Parameters.AddBoolean("@behalf", onBehalfOfOwner);
        command.Parameters.AddNVarChar("@note", 500, note);
        command.Parameters.AddUtcDateTime("@now", now);
        await db.ExecuteAsync(command, ct).ConfigureAwait(false);
    }
}
