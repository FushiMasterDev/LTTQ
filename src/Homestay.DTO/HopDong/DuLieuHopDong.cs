using Homestay.DTO.Common;
using Homestay.DTO.Enums;
namespace Homestay.DTO.Contracts;

/// <summary>Điều kiện tìm hợp đồng chủ nhà: trạng thái, homestay.</summary>
public sealed record ContractSearchRequest
{
    public PageRequest Page { get; init; } = new();
    public ContractStatus? Status { get; init; }
    public int? HomestayId { get; init; }
}
/// <summary>Một hợp đồng chủ nhà hiển thị trên danh sách (số hợp đồng, thời hạn, hoa hồng, trạng thái).</summary>
public sealed record ContractItem(int Id, int HomestayId, string HomestayName, string ContractNo,
    DateOnly StartDate, DateOnly EndDate, decimal CommissionRate, ContractStatus Status,
    int SignedByEmployeeId, DateTime? TerminatedAtUtc, string? TerminateReason, byte[] RowVer);
// Creates Active immediately; schema/spec have no Draft contract, no future activation.
/// <summary>Dữ liệu nhập để tạo hợp đồng với chủ nhà.</summary>
public sealed record CreateContractRequest(int HomestayId, string ContractNo, DateOnly StartDate, DateOnly EndDate,
    decimal CommissionRate, byte[] RowVer);
/// <summary>Yêu cầu kích hoạt hợp đồng.</summary>
public sealed record ActivateContractRequest(int ContractId, byte[] RowVer);
/// <summary>Yêu cầu chấm dứt hợp đồng kèm lý do.</summary>
public sealed record TerminateContractRequest(int ContractId, string Reason, byte[] RowVer);
/// <summary>Yêu cầu gia hạn hợp đồng đến ngày mới.</summary>
public sealed record ExtendContractRequest(int ContractId, DateOnly EndDate, byte[] RowVer);
