using Homestay.DAL.Infrastructure;
using Homestay.DTO.Enums;
using Homestay.DTO.Owners;
using Microsoft.Data.SqlClient;

namespace Homestay.DAL.Repositories;

public sealed partial class OwnerRepository
{
    public async Task<OwnerItem?> GetForUpdateAsync(UnitOfWork db, int id, string scope, Action<SqlCommand> addScope, CancellationToken ct)
    {
        using var command = db.CreateCommand($"SELECT {Columns} FROM dbo.Owners o WITH (UPDLOCK,HOLDLOCK) WHERE o.Id=@id AND ({scope});");
        addScope(command); command.Parameters.AddInt32("@id", id);
        return (await db.QueryAsync(command, Map, ct).ConfigureAwait(false)).SingleOrDefault();
    }
    private static void Values(SqlCommand c, CreateOwnerRequest v)
    {
        c.Parameters.AddNVarChar("@name", 100, v.FullName); c.Parameters.AddVarChar("@phone", 16, v.Phone);
        c.Parameters.AddNVarChar("@email", 256, v.Email); c.Parameters.AddNVarChar("@address", 300, v.Address);
        c.Parameters.AddVarChar("@identity", 20, v.IdNumber); c.Parameters.AddNVarChar("@bank", 100, v.BankName);
        c.Parameters.AddVarChar("@account", 30, v.BankAccountNo); c.Parameters.AddNVarChar("@accountName", 100, v.BankAccountName);
        c.Parameters.AddNVarChar("@note", 1000, v.Note);
    }
    public async Task<int> InsertAsync(UnitOfWork db, CreateOwnerRequest values, int employee, DateTime now, CancellationToken ct)
    {
        using var c = db.CreateCommand("""
            INSERT dbo.Owners(FullName,Phone,Email,Address,IdNumber,BankName,BankAccountNo,BankAccountName,Note,Status,CreatedByEmployeeId,CreatedAt)
            OUTPUT inserted.Id VALUES(@name,@phone,@email,@address,@identity,@bank,@account,@accountName,@note,'Pending',@employee,@now);
            """);
        Values(c, values); c.Parameters.AddInt32("@employee", employee); c.Parameters.AddUtcDateTime("@now", now);
        return (int)(await db.ScalarAsync(c, ct).ConfigureAwait(false))!;
    }
    // The full snapshot is checked under the retained UPDLOCK/HOLDLOCK before calling this writer.
    public async Task UpdateAsync(UnitOfWork db, OwnerItem value, DateTime now, CancellationToken ct)
    {
        using var c = db.CreateCommand("""
            UPDATE dbo.Owners SET FullName=@name,Phone=@phone,Email=@email,Address=@address,IdNumber=@identity,
                BankName=@bank,BankAccountNo=@account,BankAccountName=@accountName,Note=@note,Status=@status,
                ApprovedByEmployeeId=@approver,ApprovedAt=@approved,UpdatedAt=@now WHERE Id=@id;
            """);
        Values(c, new() { FullName = value.FullName, Phone = value.Phone, Email = value.Email, Address = value.Address,
            IdNumber = value.IdNumber, BankName = value.BankName, BankAccountNo = value.BankAccountNo, BankAccountName = value.BankAccountName, Note = value.Note });
        c.Parameters.AddInt32("@id", value.Id); c.Parameters.AddEnum("@status", 20, (OwnerStatus?)value.Status);
        c.Parameters.AddInt32("@approver", value.ApprovedByEmployeeId); c.Parameters.AddUtcDateTime("@approved", value.ApprovedAtUtc); c.Parameters.AddUtcDateTime("@now", now);
        await db.ExecuteAsync(c, ct).ConfigureAwait(false);
    }
}
