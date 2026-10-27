using Homestay.DTO.Common;
using Homestay.DTO.Enums;
namespace Homestay.DTO.Contracts;

public sealed record ContractSearchRequest
{
    public PageRequest Page { get; init; } = new();
    public ContractStatus? Status { get; init; }
    public int? HomestayId { get; init; }
}
public sealed record ContractItem(int Id, int HomestayId, string HomestayName, string ContractNo,
    DateOnly StartDate, DateOnly EndDate, decimal CommissionRate, ContractStatus Status,
    int SignedByEmployeeId, DateTime? TerminatedAtUtc, string? TerminateReason, byte[] RowVer);
// Creates Active immediately; schema/spec have no Draft contract, no future activation.
public sealed record CreateContractRequest(int HomestayId, string ContractNo, DateOnly StartDate, DateOnly EndDate,
    decimal CommissionRate, byte[] RowVer);
public sealed record ActivateContractRequest(int ContractId, byte[] RowVer);
public sealed record TerminateContractRequest(int ContractId, string Reason, byte[] RowVer);
public sealed record ExtendContractRequest(int ContractId, DateOnly EndDate, byte[] RowVer);
