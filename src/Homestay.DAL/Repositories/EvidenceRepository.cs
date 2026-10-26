using Homestay.DAL.Infrastructure;
using Homestay.DTO.Files;

namespace Homestay.DAL.Repositories;

public sealed class EvidenceRepository
{
    public async Task<EvidenceFile?> GetAsync(DbSession db, int bookingId, Guid fileId, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            SELECT f.Id,f.FileName,f.ContentType,f.Content FROM dbo.StoredFiles f WHERE f.Id=@file AND f.Category='Evidence'
            AND EXISTS(SELECT 1 FROM dbo.Payments p WHERE p.BookingId=@booking AND p.EvidenceFileId=f.Id);
            """);
        command.Parameters.AddInt32("@booking", bookingId);
        command.Parameters.AddGuid("@file", fileId);
        return (await db.QueryAsync(command, r => new EvidenceFile(r.GetGuid("Id"), r.GetString("FileName"), r.GetString("ContentType"), r.GetFieldValue<byte[]>(r.GetOrdinal("Content"))), ct).ConfigureAwait(false)).SingleOrDefault();
    }
}
