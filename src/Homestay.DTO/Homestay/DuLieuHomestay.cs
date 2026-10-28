using Homestay.DTO.Bookings;
using Homestay.DTO.Catalog;
using Homestay.DTO.Common;
using Homestay.DTO.Contracts;
using Homestay.DTO.Enums;
using Homestay.DTO.Owners;
using Homestay.DTO.Rooms;
namespace Homestay.DTO.Homestays;

/// <summary>Các thao tác có thể làm trên một homestay (sửa, quản lý ảnh/giấy tờ/phòng, gửi duyệt, duyệt, tạm ngưng…).</summary>
public enum HomestayAction
{
    Edit, SetAmenities, ManageImages, ManageDocuments, ManageRooms, AddPrice, ManageCancellationPolicy,
    Submit, Approve, Return, Suspend, Reopen, End, BlockRoom
}
/// <summary>Điều kiện tìm homestay: trạng thái, khu vực, chủ nhà, chỉ hồ sơ chờ duyệt.</summary>
public sealed record HomestaySearchRequest
{
    public PageRequest Page { get; init; } = new();
    public HomestayStatus? Status { get; init; }
    public int? AreaId { get; init; }
    public int? OwnerId { get; init; }
    public bool PendingReviewOnly { get; init; }
}
/// <summary>Một homestay hiển thị trên danh sách/hồ sơ.</summary>
public sealed record HomestayItem
{
    public int Id { get; init; }
    public int OwnerId { get; init; }
    public string OwnerName { get; init; } = "";
    public int AreaId { get; init; }
    public string AreaName { get; init; } = "";
    public string Name { get; init; } = "";
    public string Address { get; init; } = "";
    public string? ContactPhone { get; init; }
    public string? Description { get; init; }
    public string? CheckinGuide { get; init; }
    public TimeOnly CheckinTime { get; init; }
    public TimeOnly CheckoutTime { get; init; }
    public HomestayStatus Status { get; init; }
    public string? StatusReason { get; init; }
    public DateTime? SubmittedAtUtc { get; init; }
    public int? ApprovedByEmployeeId { get; init; }
    public DateTime? ApprovedAtUtc { get; init; }
    public int CreatedByUserId { get; init; }
    public int RoomTypeCount { get; init; }
    public int RoomCount { get; init; }
    public string? ActiveContractNo { get; init; }
    public DateOnly? ContractEndDate { get; init; }
    public decimal? CommissionRate { get; init; }
    public byte[] RowVer { get; init; } = [];
}
/// <summary>Thông tin tập tin đính kèm của tin đăng (tên, loại, dung lượng, mã băm).</summary>
public sealed record ListingFileMetadata(Guid FileId, string FileName, string ContentType, int SizeBytes, byte[] Sha256);
/// <summary>Một ảnh của homestay/loại phòng (chú thích, thứ tự, có phải ảnh bìa).</summary>
public sealed record HomestayImageItem(int Id, int? RoomTypeId, string? Caption, int SortOrder, bool IsCover, ListingFileMetadata File);
/// <summary>Một giấy tờ pháp lý của homestay (loại giấy tờ, ngày hết hạn, tập tin).</summary>
public sealed record VerificationDocumentItem(int Id, DocumentType DocumentType, DateOnly? ExpiryDate,
    int UploadedByUserId, bool IsSuperseded, ListingFileMetadata File);
/// <summary>Một điều kiện cần đạt để duyệt homestay và đã đạt hay chưa.</summary>
public sealed record ApprovalCondition(string Code, bool IsSatisfied, string Message);
/// <summary>Bảng kiểm điều kiện duyệt homestay; chỉ duyệt được khi mọi điều kiện đều đạt.</summary>
public sealed record HomestayApprovalChecklist(int HomestayId, DateTime ServerNowUtc, DateOnly Today,
    IReadOnlyList<ApprovalCondition> Conditions)
{
    public bool CanApprove => Conditions.All(c => c.IsSatisfied);
}
/// <summary>Toàn bộ hồ sơ một homestay: chủ nhà, tiện nghi, ảnh, giấy tờ, loại phòng, phòng, giá, chính sách hủy, hợp đồng, nhật ký.</summary>
public sealed record HomestayDetail
{
    public HomestayItem Homestay { get; init; } = null!;
    public OwnerItem Owner { get; init; } = null!;
    public IReadOnlyList<Lookup> Amenities { get; init; } = [];
    public IReadOnlyList<HomestayImageItem> Images { get; init; } = [];
    public bool CanViewLegalDocuments { get; init; }
    public IReadOnlyList<VerificationDocumentItem> Documents { get; init; } = [];
    public IReadOnlyList<RoomTypeItem> RoomTypes { get; init; } = [];
    public IReadOnlyList<RoomItem> Rooms { get; init; } = [];
    public RoomPriceList Prices { get; init; } = null!;
    public IReadOnlyList<CancellationPolicyItem> CancellationPolicies { get; init; } = [];
    public bool CanViewContracts { get; init; }
    public IReadOnlyList<ContractItem> Contracts { get; init; } = [];
    // Schema has no HomestayStatusHistories. Status history is the scoped AuditLogs projection.
    public IReadOnlyList<BookingAuditItem> Audit { get; init; } = [];
    public HomestayApprovalChecklist Approval { get; init; } = null!;
    public IReadOnlyList<HomestayAction> AllowedActions { get; init; } = [];
}
/// <summary>Dữ liệu nhập để tạo homestay nháp.</summary>
public sealed record CreateHomestayRequest
{
    public int OwnerId { get; init; }
    public int AreaId { get; init; }
    public string Name { get; init; } = "";
    public string Address { get; init; } = "";
    public string? ContactPhone { get; init; }
    public string? Description { get; init; }
    public string? CheckinGuide { get; init; }
    public TimeOnly CheckinTime { get; init; } = new(14, 0);
    public TimeOnly CheckoutTime { get; init; } = new(12, 0);
}
/// <summary>Yêu cầu sửa thông tin homestay.</summary>
public sealed record UpdateHomestayRequest(int HomestayId, CreateHomestayRequest Values, byte[] RowVer);
/// <summary>Yêu cầu đổi trạng thái homestay (gửi duyệt, duyệt, trả về, tạm ngưng…) kèm lý do nếu cần.</summary>
public sealed record HomestayStatusRequest(int HomestayId, byte[] RowVer, string? Reason = null);
/// <summary>Yêu cầu đặt lại danh sách tiện nghi của homestay.</summary>
public sealed record SetHomestayAmenitiesRequest(int HomestayId, IReadOnlyList<int> AmenityIds, byte[] RowVer);
/// <summary>Yêu cầu thêm ảnh cho homestay hoặc loại phòng.</summary>
public sealed record AddHomestayImageRequest(int HomestayId, byte[] Content, string FileName, int? RoomTypeId,
    string? Caption, int SortOrder, bool IsCover, byte[] RowVer);
/// <summary>Yêu cầu sửa chú thích, thứ tự, ảnh bìa của một ảnh.</summary>
public sealed record UpdateHomestayImageRequest(int HomestayId, int ImageId, int? RoomTypeId,
    string? Caption, int SortOrder, bool IsCover, byte[] RowVer);
/// <summary>Yêu cầu xóa một ảnh của homestay.</summary>
public sealed record RemoveHomestayImageRequest(int HomestayId, int ImageId, byte[] RowVer);
/// <summary>Yêu cầu thêm giấy tờ pháp lý cho homestay.</summary>
public sealed record AddVerificationDocumentRequest(int HomestayId, DocumentType DocumentType, byte[] Content,
    string FileName, DateOnly? ExpiryDate, byte[] RowVer);
/// <summary>Yêu cầu gỡ một giấy tờ pháp lý kèm lý do.</summary>
public sealed record RemoveVerificationDocumentRequest(int HomestayId, int DocumentId, string Reason, byte[] RowVer);
/// <summary>Yêu cầu đặt chính sách hủy riêng cho homestay.</summary>
public sealed record SetHomestayCancellationPolicyRequest(int HomestayId, CreateCancellationPolicyRequest Policy, byte[] RowVer);
/// <summary>Yêu cầu tắt chính sách hủy riêng của homestay.</summary>
public sealed record DisableHomestayCancellationPolicyRequest(int HomestayId, int PolicyId, byte[] RowVer);
