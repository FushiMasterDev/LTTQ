using Homestay.BLL.Infrastructure;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Files;

namespace Homestay.BLL.Services;

public sealed class FileService(Authorizer authorizer, ConnectionFactory factory, EvidenceRepository repository)
{
    // Trả byte; GUI chọn lưu/mở tệp cục bộ. Không mở ứng dụng hay tạo tệp từ BLL.
    public Task<EvidenceFile> GetEvidenceAsync(int bookingId, Guid fileId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingView);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
            await repository.GetAsync(db, bookingId, fileId, token).ConfigureAwait(false) ?? throw new NotFoundException(), ct), cancellationToken);
    }
}
