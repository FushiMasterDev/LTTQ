using System.Text.Json;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Bookings;
using Homestay.DTO.Catalog;
using Homestay.DTO.Common;
using Homestay.DTO.Enums;
using Homestay.DTO.Homestays;
namespace Homestay.BLL.Services;

public sealed class HomestayService(Authorizer authorizer, ICurrentSession currentSession, DataScope dataScope,
    ConnectionFactory factory, HomestayRepository repository, DbClock clock)
{
    public Task<PagedResult<HomestayItem>> ListAsync(HomestaySearchRequest request, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.HomestayView); ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.Page(request.Page, "Id", "Name", "OwnerName", "AreaName", "Status");
        if (request.AreaId is <= 0 || request.OwnerId is <= 0 || request.Status is { } status && !Enum.IsDefined(status)) throw new BusinessException("Bộ lọc homestay không hợp lệ.");
        if (request.PendingReviewOnly) authorizer.Require(Permission.HomestayReview);
        var scope = dataScope.HomestayFilter();
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        {
            var result = await repository.ListAsync(db, request, scope.Condition, scope.AddParametersTo, token).ConfigureAwait(false);
            return result with { Items = result.Items.Select(Mask).ToArray() };
        }, ct), cancellationToken);
    }
    internal async Task<HomestayItem> ReadHeadAsync(DbSession db, int home, CancellationToken ct)
    {
        var scope = dataScope.HomestayFilter();
        return await repository.GetAsync(db, home, scope.Condition, scope.AddParametersTo, ct).ConfigureAwait(false) ?? throw new NotFoundException();
    }
    public Task<HomestayDetail> GetAsync(int homestayId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.HomestayView);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        {
            var head = await ReadHeadAsync(db, homestayId, token).ConfigureAwait(false);
            var now = await clock.GetUtcNowAsync(db, token).ConfigureAwait(false);
            var approval = await repository.ApprovalAsync(db, homestayId, now, token).ConfigureAwait(false);
            var session = currentSession.RequireSession(); var rooms = new RoomRepository();
            var owners = new OwnerRepository(); var ownerService = new OwnerService(authorizer, dataScope, factory, owners);
            var canDocuments = CanViewDocuments(head); var canContracts = authorizer.Can(Permission.ContractManage);
            var auditAccess = authorizer.GetAccess(Permission.AuditView);
            var audit = auditAccess is AccessLevel.All or AccessLevel.Own
                ? await repository.AuditAsync(db, homestayId, auditAccess == AccessLevel.Own ? session.UserId : null, token).ConfigureAwait(false) : [];
            return new HomestayDetail
            {
                Homestay = Mask(head),
                Owner = ownerService.Mask(await owners.GetAsync(db, head.OwnerId, "1=1", _ => { }, token).ConfigureAwait(false) ?? throw new NotFoundException()),
                Amenities = await repository.AmenitiesAsync(db, homestayId, token).ConfigureAwait(false),
                Images = await repository.ImagesAsync(db, homestayId, token).ConfigureAwait(false),
                CanViewLegalDocuments = canDocuments,
                Documents = canDocuments ? await repository.DocumentsAsync(db, homestayId, token).ConfigureAwait(false) : [],
                RoomTypes = await rooms.ListTypesAsync(db, homestayId, token).ConfigureAwait(false),
                Rooms = await rooms.ListRoomsAsync(db, homestayId, token).ConfigureAwait(false),
                Prices = await rooms.ListPricesAsync(db, homestayId, now, token).ConfigureAwait(false),
                CancellationPolicies = await repository.PoliciesAsync(db, homestayId, token).ConfigureAwait(false),
                CanViewContracts = canContracts,
                Contracts = canContracts ? await new ContractRepository().ListForHomestayAsync(db, homestayId, token).ConfigureAwait(false) : [],
                Approval = approval,
                Audit = audit.Select(a => a with { OldValues = SafeAudit(a.OldValues), NewValues = SafeAudit(a.NewValues) }).ToArray(),
                AllowedActions = Actions(head, approval.CanApprove, await repository.CanEndAsync(db, homestayId, now, token).ConfigureAwait(false))
            };
        }, ct), cancellationToken);
    }
    public Task<HomestayApprovalChecklist> GetApprovalConditionsAsync(int homestayId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.HomestayView);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        { _ = await ReadHeadAsync(db, homestayId, token).ConfigureAwait(false); return await repository.ApprovalAsync(db, homestayId, await clock.GetUtcNowAsync(db, token).ConfigureAwait(false), token).ConfigureAwait(false); }, ct), cancellationToken);
    }
    public Task<IReadOnlyList<CancellationPolicyItem>> ListCancellationPoliciesAsync(int homestayId, CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.HomestayView);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync(async (db, token) =>
        { _ = await ReadHeadAsync(db, homestayId, token).ConfigureAwait(false); return await repository.PoliciesAsync(db, homestayId, token).ConfigureAwait(false); }, ct), cancellationToken);
    }
    public Task<IReadOnlyList<Lookup>> ListHomestaysAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.HomestayView); var scope = dataScope.HomestayFilter();
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync((db, token) => repository.LookupAsync(db, scope.Condition, scope.AddParametersTo, token), ct), cancellationToken);
    }
    public Task<IReadOnlyList<Lookup>> ListAreasAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.HomestayView); var scope = dataScope.AreaFilter();
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync((db, token) => repository.AreasAsync(db, scope.Condition, scope.AddParametersTo, token), ct), cancellationToken);
    }
    public Task<IReadOnlyList<Lookup>> ListAmenitiesAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.HomestayView);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync((db, token) => repository.AmenitiesAsync(db, null, token), ct), cancellationToken);
    }
    public Task<IReadOnlyList<Lookup>> ListRoomCategoriesAsync(CancellationToken cancellationToken = default)
    {
        authorizer.Require(Permission.HomestayView);
        return BusinessErrorTranslator.ExecuteAsync(ct => factory.ReadAsync((db, token) => repository.CategoriesAsync(db, token), ct), cancellationToken);
    }
    private HomestayItem Mask(HomestayItem item) => currentSession.RequireSession().Position == Position.Staff ? item with { CommissionRate = null } : item;
    private bool CanViewDocuments(HomestayItem item) => authorizer.GetAccess(Permission.LegalDocumentView) is AccessLevel.All
        || authorizer.GetAccess(Permission.LegalDocumentView) == AccessLevel.DraftOnly && item.Status == HomestayStatus.Draft;
    private IReadOnlyList<HomestayAction> Actions(HomestayItem head, bool ready, bool canEnd)
    {
        var session = currentSession.RequireSession(); var result = new List<HomestayAction>();
        // The head has already passed DataScope. DraftOnly has no creator restriction in spec 5.2/G4.
        foreach (var (permission, action) in new[] {
            (Permission.HomestayEdit,HomestayAction.Edit),(Permission.HomestayEdit,HomestayAction.SetAmenities),
            (Permission.HomestayEdit,HomestayAction.ManageImages),(Permission.HomestayEdit,HomestayAction.ManageDocuments),
            (Permission.RoomManage,HomestayAction.ManageRooms),(Permission.RoomPriceAdd,HomestayAction.AddPrice),
            (Permission.HomestayCancellationPolicyManage,HomestayAction.ManageCancellationPolicy),(Permission.RoomBlockManage,HomestayAction.BlockRoom) })
        {
            var access = authorizer.GetAccess(permission);
            if (access == AccessLevel.All || access == AccessLevel.DraftOnly && head.Status == HomestayStatus.Draft) result.Add(action);
        }
        var actor = HomestayStateMachine.Actor(session.Position);
        foreach (var (target, action, permission, condition) in new[] {
            (HomestayStatus.PendingReview,HomestayAction.Submit,Permission.HomestaySubmit,head.Status==HomestayStatus.Draft),
            (HomestayStatus.Active,HomestayAction.Approve,Permission.HomestayReview,head.Status==HomestayStatus.PendingReview && ready),
            (HomestayStatus.Draft,HomestayAction.Return,Permission.HomestayReview,true),
            (HomestayStatus.Suspended,HomestayAction.Suspend,Permission.HomestayReview,true),
            (HomestayStatus.Active,HomestayAction.Reopen,Permission.HomestayReview,head.Status==HomestayStatus.Suspended && ready),
            (HomestayStatus.Ended,HomestayAction.End,Permission.HomestayReview,canEnd) })
            if (condition && authorizer.Can(permission) && HomestayStateMachine.CanTransition(head.Status, target, actor, head.CreatedByUserId, session.UserId)) result.Add(action);
        return result;
    }
    // Read only an allowlist of known non-secret fields, even if legacy SQL inserted raw values in audit JSON.
    private static string? SafeAudit(string? value)
    {
        if (value is null) return null;
        try
        {
            using var document = JsonDocument.Parse(value);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return "[Nội dung đã ẩn]";
            var allowed = new[] { "Id", "OwnerId", "AreaId", "Name", "Address", "ContactPhone", "Description", "CheckinGuide", "CheckinTime", "CheckoutTime", "Status", "StatusReason", "SubmittedAt", "ApprovedAt", "ApprovedByEmployeeId", "CreatedByUserId" };
            return JsonSerializer.Serialize(document.RootElement.EnumerateObject().Where(p => allowed.Contains(p.Name, StringComparer.OrdinalIgnoreCase) && p.Value.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)).ToDictionary(p => p.Name, p => p.Value.Clone()));
        }
        catch (JsonException) { return "[Nội dung đã ẩn]"; }
    }
    public Task<HomestayDetail> CreateDraftAsync(CreateHomestayRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<HomestayDetail> UpdateAsync(UpdateHomestayRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<HomestayDetail> SetAmenitiesAsync(SetHomestayAmenitiesRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<HomestayDetail> AddImageAsync(AddHomestayImageRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<HomestayDetail> UpdateImageAsync(UpdateHomestayImageRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<HomestayDetail> RemoveImageAsync(RemoveHomestayImageRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<HomestayDetail> AddDocumentAsync(AddVerificationDocumentRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<HomestayDetail> RemoveDocumentAsync(RemoveVerificationDocumentRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<HomestayDetail> SubmitAsync(HomestayStatusRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<HomestayDetail> ApproveAsync(HomestayStatusRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<HomestayDetail> ReturnAsync(HomestayStatusRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<HomestayDetail> SuspendAsync(HomestayStatusRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<HomestayDetail> ReopenAsync(HomestayStatusRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<HomestayDetail> EndAsync(HomestayStatusRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<HomestayDetail> SetCancellationPolicyAsync(SetHomestayCancellationPolicyRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    public Task<HomestayDetail> DisableCancellationPolicyAsync(DisableHomestayCancellationPolicyRequest request, CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
