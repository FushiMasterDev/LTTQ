using Homestay.DAL.Infrastructure;

namespace Homestay.DAL.Repositories;

/// <summary>Truy vấn bảng phiên bản schema CSDL đã chạy.</summary>
public sealed class SchemaVersionRepository
{
    public async Task<IReadOnlyList<int>> ListVersionsAsync(DbSession db, CancellationToken ct)
    {
        using var command = db.CreateCommand("SELECT [Version] FROM dbo.SchemaVersions ORDER BY [Version];");
        return await db.QueryAsync(command, r => r.GetInt32("Version"), ct).ConfigureAwait(false);
    }
}
