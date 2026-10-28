namespace Homestay.DTO.Enums;

/// <summary>Trạng thái voucher của khách: còn dùng, đã dùng hết, hết hạn, bị thu hồi.</summary>
public enum VoucherStatus
{
    Active,
    UsedUp,
    Expired,
    Revoked,
}
