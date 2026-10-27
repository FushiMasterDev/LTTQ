using Homestay.DAL.Infrastructure;
using Homestay.DTO.Files;

namespace Homestay.DAL.Repositories;

public sealed class EvidenceRepository
{
    public async Task<Guid> InsertAsync(UnitOfWork db, string fileName, string contentType, byte[] content, byte[] sha256, int userId, CancellationToken ct)
    {
        using var command = db.CreateCommand("""
            INSERT dbo.StoredFiles(Category,FileName,ContentType,SizeBytes,Sha256,Content,UploadedByUserId)
            OUTPUT inserted.Id VALUES('Evidence',@name,@type,@size,@sha,@bytes,@user);
            """);
        command.Parameters.AddNVarChar("@name", 255, fileName);
        command.Parameters.AddVarChar("@type", 100, contentType);
        command.Parameters.AddInt32("@size", content.Length);
        command.Parameters.AddVarBinary("@sha", sha256);
        command.Parameters.AddVarBinary("@bytes", content);
        command.Parameters.AddInt32("@user", userId);
        return (Guid)(await db.ScalarAsync(command, ct).ConfigureAwait(false))!;
    }

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
    public async Task<EvidenceFile?> GetHomestayFileAsync(DbSession db,int homestayId,Guid fileId,bool document,CancellationToken ct)
    {
        using var command=db.CreateCommand(document ?
            "SELECT f.Id,f.FileName,f.ContentType,f.Content FROM dbo.StoredFiles f WHERE f.Id=@file AND f.Category='Document' AND EXISTS(SELECT 1 FROM dbo.VerificationDocuments d WHERE d.HomestayId=@home AND d.FileId=f.Id);" :
            "SELECT f.Id,f.FileName,f.ContentType,f.Content FROM dbo.StoredFiles f WHERE f.Id=@file AND f.Category='HomestayImage' AND EXISTS(SELECT 1 FROM dbo.HomestayImages i WHERE i.HomestayId=@home AND i.FileId=f.Id);");
        command.Parameters.AddInt32("@home",homestayId);command.Parameters.AddGuid("@file",fileId);
        return (await db.QueryAsync(command,r=>new EvidenceFile(r.GetGuid("Id"),r.GetString("FileName"),r.GetString("ContentType"),r.GetFieldValue<byte[]>(r.GetOrdinal("Content"))),ct).ConfigureAwait(false)).SingleOrDefault();
    }
}
