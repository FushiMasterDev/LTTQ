using Homestay.BLL.Infrastructure;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Files;

namespace Homestay.BLL.Services;

public sealed class FileService(Authorizer authorizer, ConnectionFactory factory, EvidenceRepository repository, DataScope? dataScope = null)
{
    // Trả byte; GUI chọn lưu/mở tệp cục bộ. Không mở ứng dụng hay tạo tệp từ BLL.
    public Task<EvidenceFile> GetEvidenceAsync(int bookingId, Guid fileId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.BookingView);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
            await repository.GetAsync(db, bookingId, fileId, token).ConfigureAwait(false) ?? throw new NotFoundException(), ct), cancellationToken);
    }
    public Task<EvidenceFile> GetHomestayImageAsync(int homestayId,Guid fileId,CancellationToken cancellationToken=default)
    {
        authorizer.Require(Permission.HomestayView);
        return GetHomestayFileAsync(homestayId,fileId,false,cancellationToken);
    }
    public Task<EvidenceFile> GetHomestayDocumentAsync(int homestayId,Guid fileId,CancellationToken cancellationToken=default)
    {
        authorizer.Require(Permission.HomestayView);authorizer.Require(Permission.LegalDocumentView);
        return GetHomestayFileAsync(homestayId,fileId,true,cancellationToken);
    }
    private Task<EvidenceFile> GetHomestayFileAsync(int home,Guid file,bool document,CancellationToken cancellationToken)
    {
        var scope=(dataScope ?? throw new InvalidOperationException("Configure FileService with DataScope for homestay files.")).HomestayFilter();
        return BusinessErrorTranslator.ExecuteAsync(ct=>factory.ReadAsync(async(db,token)=>
        {
            var head=await new HomestayRepository().GetAsync(db,home,scope.Condition,scope.AddParametersTo,token).ConfigureAwait(false) ?? throw new NotFoundException();
            if (document && authorizer.GetAccess(Permission.LegalDocumentView)==AccessLevel.DraftOnly && head.Status!=Homestay.DTO.Enums.HomestayStatus.Draft)
                throw new ForbiddenException();
            return await repository.GetHomestayFileAsync(db,home,file,document,token).ConfigureAwait(false) ?? throw new NotFoundException();
        },ct),cancellationToken);
    }
}
