namespace Homestay.DTO.Enums;

/// <summary>Loại dòng trong bảng đối soát: đơn hoàn tất, hủy giữ cọc, không đến, chỉ thu phí.</summary>
public enum SettlementItemType
{
    Completed,
    CancelledRetained,
    NoShow,
    FeeOnly,
}
