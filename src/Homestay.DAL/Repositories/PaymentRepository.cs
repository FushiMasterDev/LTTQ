using Homestay.DAL.Infrastructure;
using Homestay.DTO.Payments;

namespace Homestay.DAL.Repositories;

public sealed class PaymentRepository
{
    public async Task<int> InsertReceiptAsync(UnitOfWork db, RecordReceiptRequest request, string voucherNo,
        DateTime transferredAt, int userId, DateTime now, Guid? fileId, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            INSERT dbo.Payments(VoucherNo,BookingId,Direction,Type,Method,Amount,TransferredAt,EvidenceFileId,
                PayerOrPayeeName,PayerOrPayeeAccount,TransferContent,Status,RecordedByUserId,RecordedAt)
            VALUES(@voucher,@booking,'In','Deposit',@method,@amount,@transferred,@file,@name,@account,@content,'Pending',@user,@now);
            SELECT CONVERT(int,SCOPE_IDENTITY());
            """);
        command.Parameters.AddVarChar("@voucher", 20, voucherNo);
        command.Parameters.AddInt32("@booking", request.BookingId);
        command.Parameters.AddEnum("@method", 20, (Homestay.DTO.Enums.PaymentMethod?)request.Method);
        command.Parameters.AddMoney("@amount", request.Amount);
        command.Parameters.AddUtcDateTime("@transferred", transferredAt);
        command.Parameters.AddGuid("@file", fileId);
        command.Parameters.AddNVarChar("@name", 100, request.PayerName);
        command.Parameters.AddVarChar("@account", 30, request.PayerAccount);
        command.Parameters.AddNVarChar("@content", 255, request.TransferContent);
        command.Parameters.AddInt32("@user", userId);
        command.Parameters.AddUtcDateTime("@now", now);
        return (int)(await db.ScalarAsync(command, ct).ConfigureAwait(false))!;
    }
}
