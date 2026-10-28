using Homestay.DTO.Common;
using Homestay.DTO.Enums;
namespace Homestay.DTO.Owners;

/// <summary>Điều kiện tìm chủ nhà: phân trang và trạng thái.</summary>
public sealed record OwnerSearchRequest
{
    public PageRequest Page { get; init; } = new();
    public OwnerStatus? Status { get; init; }
}
/// <summary>Một chủ nhà hiển thị trên danh sách/chi tiết (thông tin liên hệ, ngân hàng, trạng thái duyệt).</summary>
public sealed record OwnerItem
{
    public int Id { get; init; }
    public string FullName { get; init; } = "";
    public string Phone { get; init; } = "";
    public string? Email { get; init; }
    public string? Address { get; init; }
    public string? IdNumber { get; init; }
    public string? BankName { get; init; }
    public string? BankAccountNo { get; init; }
    public string? BankAccountName { get; init; }
    public bool IsSensitiveMasked { get; init; }
    public OwnerStatus Status { get; init; }
    public int? ApprovedByEmployeeId { get; init; }
    public DateTime? ApprovedAtUtc { get; init; }
    public int? CreatedByEmployeeId { get; init; }
    public string? Note { get; init; }
    // Owners has no SQL rowversion. Opaque SHA-256 snapshot token, 32 bytes; see M4.1 notes.
    public byte[] RowVer { get; init; } = [];
}
/// <summary>Dữ liệu nhập để tạo hồ sơ chủ nhà mới.</summary>
public sealed record CreateOwnerRequest
{
    public string FullName { get; init; } = "";
    public string Phone { get; init; } = "";
    public string? Email { get; init; }
    public string? Address { get; init; }
    public string? IdNumber { get; init; }
    public string? BankName { get; init; }
    public string? BankAccountNo { get; init; }
    public string? BankAccountName { get; init; }
    public string? Note { get; init; }
}
/// <summary>Yêu cầu sửa hồ sơ chủ nhà (kèm RowVer để chống ghi đè).</summary>
public sealed record UpdateOwnerRequest(int OwnerId, CreateOwnerRequest Values, byte[] RowVer, bool ClearIdNumber = false);
/// <summary>Yêu cầu đổi tài khoản ngân hàng của chủ nhà, bắt buộc có ghi chú xác minh.</summary>
public sealed record ChangeOwnerBankRequest(int OwnerId, string BankName, string BankAccountNo, string BankAccountName, string VerificationNote, byte[] RowVer);
/// <summary>Yêu cầu đổi trạng thái chủ nhà (duyệt, tạm ngưng, từ chối) kèm lý do.</summary>
public sealed record OwnerStatusRequest(int OwnerId, OwnerStatus Status, string? Reason, byte[] RowVer);
