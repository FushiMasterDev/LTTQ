using Homestay.BLL.Infrastructure;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;

namespace Homestay.BLL.Services;

// Chạy trước đăng nhập (F04, khởi động) nên không kiểm phiên hay quyền; chỉ đọc, không ghi.
public sealed class StartupService(ConnectionFactory factory)
{
    private readonly SchemaVersionRepository repository = new();

    public Task<IReadOnlyList<int>> GetSchemaVersionsAsync(CancellationToken cancellationToken = default) =>
        BusinessErrorTranslator.ExecuteAsync(token => factory.ReadAsync(repository.ListVersionsAsync, token), cancellationToken);
}
