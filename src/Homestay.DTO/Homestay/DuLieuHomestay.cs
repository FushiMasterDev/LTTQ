using Homestay.DTO.Bookings;
using Homestay.DTO.Catalog;
using Homestay.DTO.Common;
using Homestay.DTO.Contracts;
using Homestay.DTO.Enums;
using Homestay.DTO.Owners;
using Homestay.DTO.Rooms;
namespace Homestay.DTO.Homestays;

public enum HomestayAction
{
    Edit, SetAmenities, ManageImages, ManageDocuments, ManageRooms, AddPrice, ManageCancellationPolicy,
    Submit, Approve, Return, Suspend, Reopen, End, BlockRoom
}
public sealed record HomestaySearchRequest
{
    public PageRequest Page { get; init; } = new();
    public HomestayStatus? Status { get; init; }
    public int? AreaId { get; init; }
    public int? OwnerId { get; init; }
    public bool PendingReviewOnly { get; init; }
}
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
public sealed record ListingFileMetadata(Guid FileId, string FileName, string ContentType, int SizeBytes, byte[] Sha256);
public sealed record HomestayImageItem(int Id, int? RoomTypeId, string? Caption, int SortOrder, bool IsCover, ListingFileMetadata File);
public sealed record VerificationDocumentItem(int Id, DocumentType DocumentType, DateOnly? ExpiryDate,
    int UploadedByUserId, bool IsSuperseded, ListingFileMetadata File);
public sealed record ApprovalCondition(string Code, bool IsSatisfied, string Message);
public sealed record HomestayApprovalChecklist(int HomestayId, DateTime ServerNowUtc, DateOnly Today,
    IReadOnlyList<ApprovalCondition> Conditions)
{
    public bool CanApprove => Conditions.All(c => c.IsSatisfied);
}
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
public sealed record UpdateHomestayRequest(int HomestayId, CreateHomestayRequest Values, byte[] RowVer);
public sealed record HomestayStatusRequest(int HomestayId, byte[] RowVer, string? Reason = null);
public sealed record SetHomestayAmenitiesRequest(int HomestayId, IReadOnlyList<int> AmenityIds, byte[] RowVer);
public sealed record AddHomestayImageRequest(int HomestayId, byte[] Content, string FileName, int? RoomTypeId,
    string? Caption, int SortOrder, bool IsCover, byte[] RowVer);
public sealed record UpdateHomestayImageRequest(int HomestayId, int ImageId, int? RoomTypeId,
    string? Caption, int SortOrder, bool IsCover, byte[] RowVer);
public sealed record RemoveHomestayImageRequest(int HomestayId, int ImageId, byte[] RowVer);
public sealed record AddVerificationDocumentRequest(int HomestayId, DocumentType DocumentType, byte[] Content,
    string FileName, DateOnly? ExpiryDate, byte[] RowVer);
public sealed record RemoveVerificationDocumentRequest(int HomestayId, int DocumentId, string Reason, byte[] RowVer);
public sealed record SetHomestayCancellationPolicyRequest(int HomestayId, CreateCancellationPolicyRequest Policy, byte[] RowVer);
public sealed record DisableHomestayCancellationPolicyRequest(int HomestayId, int PolicyId, byte[] RowVer);
