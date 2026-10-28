using Homestay.DAL.Infrastructure;
using Homestay.DTO.Enums;

namespace Homestay.DAL.Repositories;

/// <summary>Truy vấn đơn đang mở của một nhân viên (dùng khi quản lý chuyển giao trước lúc cho nghỉ việc).</summary>
public sealed partial class BookingRepository
{
    public async Task<IReadOnlyList<BookingWriteSnapshot>> ListEmployeeOpenBookingsAsync(UnitOfWork db, int employeeId, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            SELECT Id,Code,Status,AssignedEmployeeId,HoldExpiresAt,DepositAmount-DiscountAmount AS AmountDue,RowVer
            FROM dbo.Bookings WITH (UPDLOCK) WHERE AssignedEmployeeId=@employee
            AND Status IN ('AwaitingPayment','PaymentSubmitted','Deposited','Confirmed','CheckedIn') ORDER BY Id;
            """);
        command.Parameters.AddInt32("@employee", employeeId);
        return await db.QueryAsync(command, r => new BookingWriteSnapshot(r.GetInt32("Id"), r.GetString("Code"), r.GetEnum<BookingStatus>("Status"),
            r.GetNullableInt32("AssignedEmployeeId"), r.GetUtcDateTime("HoldExpiresAt"), r.GetDecimal("AmountDue"), r.GetRowVersion("RowVer")), ct).ConfigureAwait(false);
    }
}
