using System.Globalization;
using Homestay.BLL.Infrastructure;
using Homestay.BLL.Rules;
using Homestay.BLL.Security;
using Homestay.DAL.Infrastructure;
using Homestay.DAL.Repositories;
using Homestay.DTO.Catalog;
using Homestay.DTO.Enums;
using Homestay.DTO.Homestays;

namespace Homestay.BLL.Services;

/// <summary>Phần ghi của dịch vụ homestay: tạo nháp, sửa thông tin, tiện nghi, ảnh, giấy tờ, gửi duyệt và đổi trạng thái.</summary>
public sealed partial class HomestayService
{
    private WriteOperationRunner Writes => new(factory, currentSession, new WriteSessionValidator(new AuthRepository()));
    /// <summary>Mô tả một thay đổi cần ghi (trạng thái, lý do, giá trị mới, bản cũ/mới của mục con) để dùng chung cho việc ghi và nhật ký.</summary>
    private sealed record Change(HomestayStatus? Status = null, string? Reason = null, CreateHomestayRequest? Values = null, object? OldChild = null, object? NewChild = null);
    private void RequireWrite(Permission permission)
    {
        authorizer.Require(permission);
        if (authorizer.GetAccess(permission) == AccessLevel.ReadOnly) throw new ForbiddenException();
    }
    public Task<HomestayDetail> CreateDraftAsync(CreateHomestayRequest request, CancellationToken cancellationToken = default)
    {
        RequireWrite(Permission.HomestayCreate);
        var values = ListingWriteRules.Home(request);
        return Writes.ExecuteAsync(async (db, ct) =>
        {
            await RequireReferencesAsync(db, values, ct, creating: true).ConfigureAwait(false);
            var session = currentSession.RequireSession();
            HomestayStateMachine.Require(null, HomestayStatus.Draft, HomestayStateMachine.Actor(session.Position), session.UserId, session.UserId);
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            var id = await repository.InsertAsync(db, values, session.UserId, now, ct).ConfigureAwait(false);
            var next = await ReadHeadAsync(db, id, ct).ConfigureAwait(false);
            await AuditAsync(db, "Homestay.Create", null, next, new(), ct).ConfigureAwait(false);
            return await ReadDetailAsync(db, id, ct).ConfigureAwait(false);
        }, cancellationToken);
    }
    public Task<HomestayDetail> UpdateAsync(UpdateHomestayRequest request, CancellationToken cancellationToken = default)
    {
        RequireWrite(Permission.HomestayEdit); ArgumentNullException.ThrowIfNull(request);
        var values = ListingWriteRules.Home(request.Values);
        return WriteAsync(request.HomestayId, request.RowVer, Permission.HomestayEdit, "Homestay.Update", async (db, old, _, ct) =>
        {
            await RequireReferencesAsync(db, values, ct).ConfigureAwait(false);
            var review = old.Status == HomestayStatus.Active && (old.Address != values.Address || old.AreaId != values.AreaId);
            return new(review ? HomestayStatus.PendingReview : null, review ? "Địa chỉ hoặc khu vực thay đổi; cần duyệt lại hồ sơ." : null, values);
        }, cancellationToken);
    }
    public Task<HomestayDetail> SetAmenitiesAsync(SetHomestayAmenitiesRequest request, CancellationToken cancellationToken = default)
    {
        RequireWrite(Permission.HomestayEdit); ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(request.AmenityIds);
        var ids = request.AmenityIds.Distinct().Order().ToArray();
        return WriteAsync(request.HomestayId, request.RowVer, Permission.HomestayEdit, "Homestay.SetAmenities", async (db, old, _, ct) =>
        {
            foreach (var id in ids)
                if (!await repository.AmenityActiveAsync(db, id, ct).ConfigureAwait(false)) throw new BusinessException("Tiện nghi không tồn tại hoặc đã ngừng sử dụng. Vui lòng chọn lại.");
            var before = await repository.AmenitiesAsync(db, old.Id, ct).ConfigureAwait(false);
            await repository.SetAmenitiesAsync(db, old.Id, ids, ct).ConfigureAwait(false);
            return new(OldChild: new { AmenityIds = before.Select(a => a.Id).ToArray() }, NewChild: new { AmenityIds = ids });
        }, cancellationToken);
    }
    public Task<HomestayDetail> AddImageAsync(AddHomestayImageRequest request, CancellationToken cancellationToken = default)
    {
        RequireWrite(Permission.HomestayEdit); ArgumentNullException.ThrowIfNull(request);
        var file = RequiredEvidence(request.Content, request.FileName); var caption = ListingWriteRules.Text(request.Caption, 200);
        return WriteAsync(request.HomestayId, request.RowVer, Permission.HomestayEdit, "Homestay.AddImage", async (db, old, now, ct) =>
        {
            await RequireTypeAsync(db, old.Id, request.RoomTypeId, ct).ConfigureAwait(false);
            if (await repository.DuplicateFileAsync(db, old.Id, false, file.Sha256, null, ct).ConfigureAwait(false)) throw new BusinessException("Ảnh này đã có trong hồ sơ. Vui lòng chọn ảnh khác.");
            var before = await repository.ImagesAsync(db, old.Id, ct).ConfigureAwait(false);
            var fileId = await StoreAsync(db, "HomestayImage", file, ct).ConfigureAwait(false);
            if (request.IsCover) await repository.ClearCoverAsync(db, old.Id, now, ct).ConfigureAwait(false);
            var id = await repository.InsertImageAsync(db, old.Id, fileId, request.RoomTypeId, caption, request.SortOrder, request.IsCover, now, ct).ConfigureAwait(false);
            return new(OldChild: new { Images = before }, NewChild: new { ImageId = id, FileId = fileId, request.RoomTypeId, Caption = caption, request.SortOrder, request.IsCover });
        }, cancellationToken);
    }
    public Task<HomestayDetail> UpdateImageAsync(UpdateHomestayImageRequest request, CancellationToken cancellationToken = default)
    {
        RequireWrite(Permission.HomestayEdit); ArgumentNullException.ThrowIfNull(request); var caption = ListingWriteRules.Text(request.Caption, 200);
        return WriteAsync(request.HomestayId, request.RowVer, Permission.HomestayEdit, "Homestay.UpdateImage", async (db, old, now, ct) =>
        {
            await RequireTypeAsync(db, old.Id, request.RoomTypeId, ct).ConfigureAwait(false);
            var images = await repository.ImagesAsync(db, old.Id, ct).ConfigureAwait(false);
            var image = images.SingleOrDefault(i => i.Id == request.ImageId) ?? throw new NotFoundException();
            RequireCover(old, image, images, request.IsCover);
            if (request.IsCover) await repository.ClearCoverAsync(db, old.Id, now, ct).ConfigureAwait(false);
            await repository.UpdateImageAsync(db, old.Id, image.Id, request.RoomTypeId, caption, request.SortOrder, request.IsCover, now, ct).ConfigureAwait(false);
            return new(OldChild: new { Images = images }, NewChild: new { ImageId = image.Id, request.RoomTypeId, Caption = caption, request.SortOrder, request.IsCover });
        }, cancellationToken);
    }
    public Task<HomestayDetail> RemoveImageAsync(RemoveHomestayImageRequest request, CancellationToken cancellationToken = default)
    {
        RequireWrite(Permission.HomestayEdit); ArgumentNullException.ThrowIfNull(request);
        return WriteAsync(request.HomestayId, request.RowVer, Permission.HomestayEdit, "Homestay.RemoveImage", async (db, old, _, ct) =>
        {
            var images = await repository.ImagesAsync(db, old.Id, ct).ConfigureAwait(false);
            var image = images.SingleOrDefault(i => i.Id == request.ImageId) ?? throw new NotFoundException();
            RequireCover(old, image, images, false);
            await repository.RemoveImageAsync(db, old.Id, image.Id, ct).ConfigureAwait(false);
            return new(OldChild: image, NewChild: new { RemovedImageId = image.Id });
        }, cancellationToken);
    }
    public Task<HomestayDetail> AddDocumentAsync(AddVerificationDocumentRequest request, CancellationToken cancellationToken = default)
    {
        RequireWrite(Permission.HomestayEdit); ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.DocumentType)) throw new BusinessException("Loại giấy tờ không hợp lệ. Vui lòng chọn lại.");
        var file = RequiredEvidence(request.Content, request.FileName);
        return WriteAsync(request.HomestayId, request.RowVer, Permission.HomestayEdit, "Homestay.AddDocument", async (db, old, now, ct) =>
        {
            if (await repository.DuplicateFileAsync(db, old.Id, true, file.Sha256, request.DocumentType, ct).ConfigureAwait(false))
                throw new BusinessException("Giấy tờ này đang có trong hồ sơ. Vui lòng chọn tệp khác.");
            var legal = IsLegal(request.DocumentType);
            var before = await repository.DocumentsAsync(db, old.Id, ct).ConfigureAwait(false);
            var fileId = await StoreAsync(db, "Document", file, ct).ConfigureAwait(false);
            if (legal) await repository.SupersedeDocumentsAsync(db, old.Id, request.DocumentType, null, now, ct).ConfigureAwait(false);
            var id = await repository.InsertDocumentAsync(db, old.Id, request.DocumentType, fileId, request.ExpiryDate, currentSession.RequireSession().UserId, now, ct).ConfigureAwait(false);
            var review = legal && old.Status == HomestayStatus.Active;
            return new(review ? HomestayStatus.PendingReview : null, review ? "Giấy phép kinh doanh hoặc PCCC được tải lại; cần duyệt lại hồ sơ." : null,
                OldChild: new { Documents = before }, NewChild: new { DocumentId = id, DocumentType = request.DocumentType.ToString(), FileId = fileId, request.ExpiryDate });
        }, cancellationToken);
    }
    public Task<HomestayDetail> RemoveDocumentAsync(RemoveVerificationDocumentRequest request, CancellationToken cancellationToken = default)
    {
        RequireWrite(Permission.HomestayEdit); ArgumentNullException.ThrowIfNull(request); var reason = ListingWriteRules.Reason(request.Reason);
        return WriteAsync(request.HomestayId, request.RowVer, Permission.HomestayEdit, "Homestay.RemoveDocument", async (db, old, now, ct) =>
        {
            var document = (await repository.DocumentsAsync(db, old.Id, ct).ConfigureAwait(false)).SingleOrDefault(d => d.Id == request.DocumentId) ?? throw new NotFoundException();
            if (document.IsSuperseded) throw new BusinessException("Giấy tờ đã ngừng hiện hành. Vui lòng tải lại hồ sơ.");
            await repository.SupersedeDocumentsAsync(db, old.Id, null, document.Id, now, ct).ConfigureAwait(false);
            var review = IsLegal(document.DocumentType) && old.Status == HomestayStatus.Active;
            return new(review ? HomestayStatus.PendingReview : null, reason, OldChild: document, NewChild: new { DocumentId = document.Id, IsSuperseded = true });
        }, cancellationToken);
    }
    public Task<HomestayDetail> SubmitAsync(HomestayStatusRequest request, CancellationToken cancellationToken = default) => TransitionAsync(request, HomestayStatus.PendingReview, Permission.HomestaySubmit, "Homestay.Submit", false, false, cancellationToken);
    public Task<HomestayDetail> ApproveAsync(HomestayStatusRequest request, CancellationToken cancellationToken = default) => TransitionAsync(request, HomestayStatus.Active, Permission.HomestayReview, "Homestay.Approve", true, false, cancellationToken);
    public Task<HomestayDetail> ReturnAsync(HomestayStatusRequest request, CancellationToken cancellationToken = default) => TransitionAsync(request, HomestayStatus.Draft, Permission.HomestayReview, "Homestay.Return", false, true, cancellationToken);
    public Task<HomestayDetail> SuspendAsync(HomestayStatusRequest request, CancellationToken cancellationToken = default) => TransitionAsync(request, HomestayStatus.Suspended, Permission.HomestayReview, "Homestay.Suspend", false, true, cancellationToken);
    public Task<HomestayDetail> ReopenAsync(HomestayStatusRequest request, CancellationToken cancellationToken = default) => TransitionAsync(request, HomestayStatus.Active, Permission.HomestayReview, "Homestay.Reopen", true, true, cancellationToken);
    public Task<HomestayDetail> EndAsync(HomestayStatusRequest request, CancellationToken cancellationToken = default) => TransitionAsync(request, HomestayStatus.Ended, Permission.HomestayReview, "Homestay.End", false, true, cancellationToken);

    private Task<HomestayDetail> TransitionAsync(HomestayStatusRequest request, HomestayStatus target, Permission permission, string action, bool approval, bool requiresReason, CancellationToken cancellationToken)
    {
        RequireWrite(permission); ArgumentNullException.ThrowIfNull(request);
        var reason = requiresReason ? ListingWriteRules.Reason(request.Reason) : request.Reason is null ? null : ListingWriteRules.Reason(request.Reason);
        return WriteAsync(request.HomestayId, request.RowVer, permission, action, async (db, old, now, ct) =>
        {
            // Each endpoint represents one transition, not a generic status setter.
            if (action == "Homestay.Approve" && old.Status != HomestayStatus.PendingReview
                || action == "Homestay.Reopen" && old.Status != HomestayStatus.Suspended
                || action == "Homestay.Submit" && old.Status != HomestayStatus.Draft)
                throw new BusinessException("Không thể chuyển trạng thái homestay này. Vui lòng tải lại hồ sơ.");
            var session = currentSession.RequireSession();
            HomestayStateMachine.Require(old.Status, target, HomestayStateMachine.Actor(session.Position), old.CreatedByUserId, session.UserId, reason);
            if (approval)
            {
                _ = await new OwnerRepository().GetForUpdateAsync(db, old.OwnerId, "1=1", _ => { }, ct).ConfigureAwait(false) ?? throw new NotFoundException();
                var checklist = await repository.ApprovalAsync(db, old.Id, now, ct).ConfigureAwait(false);
                if (!checklist.CanApprove) throw new BusinessException(string.Join(Environment.NewLine, checklist.Conditions.Where(c => !c.IsSatisfied).Select(c => c.Message)));
            }
            if (target == HomestayStatus.Ended)
            {
                await LockRoomTypesAsync(db, old.Id, ct).ConfigureAwait(false);
                if (await repository.HasActiveContractAsync(db, old.Id, ct).ConfigureAwait(false))
                    throw new BusinessException("Homestay còn hợp đồng Active. Vui lòng chấm dứt hợp đồng trước khi kết thúc.");
                if (!await repository.CanEndAsync(db, old.Id, now, ct).ConfigureAwait(false))
                    throw new BusinessException("Homestay còn đơn đang chiếm phòng. Vui lòng xử lý các đơn trước khi kết thúc.");
            }
            return new(target, reason);
        }, cancellationToken);
    }
    public Task<HomestayDetail> SetCancellationPolicyAsync(SetHomestayCancellationPolicyRequest request, CancellationToken cancellationToken = default)
    {
        RequireWrite(Permission.HomestayCancellationPolicyManage); ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(request.Policy);
        var policy = request.Policy with { Name = ListingWriteRules.Required(request.Policy.Name, 100, "tên chính sách"), Rules = request.Policy.Rules?.ToArray()! };
        if (!Enum.IsDefined(policy.Scope)) throw new BusinessException("Phạm vi chính sách không hợp lệ. Vui lòng chọn lại.");
        CancellationPolicyRules.Validate(policy.Rules);
        return WriteAsync(request.HomestayId, request.RowVer, Permission.HomestayCancellationPolicyManage, "Policy.Set", async (db, old, now, ct) =>
        {
            var before = await repository.PoliciesAsync(db, old.Id, ct).ConfigureAwait(false);
            var id = await repository.InsertPolicyAsync(db, old.Id, policy, now, ct).ConfigureAwait(false);
            return new(OldChild: new { Policies = before }, NewChild: new { PolicyId = id, policy.Name, Scope = policy.Scope.ToString(), policy.Activate, policy.Rules });
        }, cancellationToken);
    }
    public Task<HomestayDetail> DisableCancellationPolicyAsync(DisableHomestayCancellationPolicyRequest request, CancellationToken cancellationToken = default)
    {
        RequireWrite(Permission.HomestayCancellationPolicyManage); ArgumentNullException.ThrowIfNull(request);
        return WriteAsync(request.HomestayId, request.RowVer, Permission.HomestayCancellationPolicyManage, "Policy.Disable", async (db, old, now, ct) =>
        {
            var policy = (await repository.PoliciesAsync(db, old.Id, ct).ConfigureAwait(false)).SingleOrDefault(p => p.Id == request.PolicyId) ?? throw new NotFoundException();
            if (!policy.IsActive) throw new BusinessException("Chính sách đã ngừng áp dụng. Vui lòng tải lại hồ sơ.");
            await repository.DisablePolicyAsync(db, old.Id, policy.Id, now, ct).ConfigureAwait(false);
            return new(OldChild: policy, NewChild: new { PolicyId = policy.Id, IsActive = false });
        }, cancellationToken);
    }
    private Task<HomestayDetail> WriteAsync(int home, byte[] version, Permission permission, string action,
        Func<UnitOfWork, HomestayItem, DateTime, CancellationToken, Task<Change>> change, CancellationToken cancellationToken)
    {
        StaffInputRules.RowVer(version); var token = version.ToArray();
        return Writes.ExecuteAsync(async (db, ct) =>
        {
            var scope = dataScope.HomestayFilter();
            var old = await repository.GetForUpdateAsync(db, home, scope.Condition, scope.AddParametersTo, ct).ConfigureAwait(false) ?? throw new NotFoundException();
            if (!old.RowVer.SequenceEqual(token)) throw new ConcurrencyException();
            if (authorizer.GetAccess(permission) == AccessLevel.DraftOnly && old.Status != HomestayStatus.Draft) throw new ForbiddenException();
            var now = await clock.GetUtcNowAsync(db, ct).ConfigureAwait(false);
            var delta = await change(db, old, now, ct).ConfigureAwait(false);
            var target = delta.Status ?? old.Status;
            if (target != old.Status)
            {
                var session = currentSession.RequireSession();
                var actor = old.Status == HomestayStatus.Active && target == HomestayStatus.PendingReview
                    ? HomestayTransitionActor.System : HomestayStateMachine.Actor(session.Position);
                HomestayStateMachine.Require(old.Status, target, actor, old.CreatedByUserId, session.UserId, delta.Reason);
                if (old.Status == HomestayStatus.Active) await LockRoomTypesAsync(db, old.Id, ct).ConfigureAwait(false);
            }
            if (await repository.WriteAsync(db, old, delta.Values, target, target == old.Status ? old.StatusReason : delta.Reason,
                currentSession.RequireSession().EmployeeId, now, ct).ConfigureAwait(false) == 0) throw new ConcurrencyException();
            var next = await ReadHeadAsync(db, home, ct).ConfigureAwait(false);
            await AuditAsync(db, action, old, next, delta, ct).ConfigureAwait(false);
            return await ReadDetailAsync(db, home, ct).ConfigureAwait(false);
        }, cancellationToken);
    }
    private async Task RequireReferencesAsync(UnitOfWork db, CreateHomestayRequest values, CancellationToken ct, bool creating = false)
    {
        var area = dataScope.AreaFilter();
        if (!await repository.AreaExistsAsync(db, values.AreaId, area.Condition, area.AddParametersTo, ct).ConfigureAwait(false)) throw new NotFoundException();
        var owner = dataScope.OwnerFilter();
        _ = await new OwnerRepository().GetForUpdateAsync(db, values.OwnerId, creating ? "1=1" : owner.Condition, creating ? _ => { } : owner.AddParametersTo, ct).ConfigureAwait(false) ?? throw new NotFoundException();
    }
    private async Task RequireTypeAsync(UnitOfWork db, int home, int? type, CancellationToken ct)
    {
        if (type is { } id && !await repository.RoomTypeBelongsAsync(db, home, id, ct).ConfigureAwait(false)) throw new NotFoundException();
    }
    private async Task LockRoomTypesAsync(UnitOfWork db, int home, CancellationToken ct)
    {
        foreach (var type in (await new RoomRepository().ListTypesAsync(db, home, ct).ConfigureAwait(false)).OrderBy(t => t.Id))
            await db.AcquireAppLockAsync($"RoomType:{type.Id}", 5000, ct).ConfigureAwait(false);
    }
    private static bool IsLegal(DocumentType type) => type is DocumentType.BusinessLicense or DocumentType.FireSafety;
    private static ValidatedEvidence RequiredEvidence(byte[] content, string name) => ReceiptRules.ValidateEvidence(content, name)
        ?? throw new BusinessException("Vui lòng chọn tệp có dữ liệu.");
    private Task<Guid> StoreAsync(UnitOfWork db, string category, ValidatedEvidence file, CancellationToken ct) =>
        new EvidenceRepository().InsertAsync(db, category, file.FileName, file.ContentType, file.Content, file.Sha256, currentSession.RequireSession().UserId, ct);
    private static void RequireCover(HomestayItem home, HomestayImageItem image, IReadOnlyList<HomestayImageItem> images, bool cover)
    {
        if (home.Status == HomestayStatus.Active && image.IsCover && !cover && !images.Any(i => i.Id != image.Id && i.IsCover))
            throw new BusinessException("Homestay đang hoạt động phải có ảnh bìa. Vui lòng chọn ảnh bìa thay thế trước.");
    }
    private Task AuditAsync(UnitOfWork db, string action, HomestayItem? old, HomestayItem next, Change delta, CancellationToken ct)
    {
        static object? Safe(HomestayItem? h, object? child) => h is null ? null : new { h.Id, h.OwnerId, h.AreaId, h.Name, h.Address, h.ContactPhone, h.Description,
            h.CheckinGuide, h.CheckinTime, h.CheckoutTime, Status = h.Status.ToString(), h.StatusReason, SubmittedAt = h.SubmittedAtUtc,
            ApprovedAt = h.ApprovedAtUtc, h.ApprovedByEmployeeId, h.CreatedByUserId, Change = child };
        return new AuditWriter().WriteAsync(db, new(currentSession.RequireSession().UserId, action, "Homestays", next.Id.ToString(CultureInfo.InvariantCulture),
            Safe(old, delta.OldChild), Safe(next, delta.NewChild), delta.Reason, Environment.MachineName), ct);
    }
}
