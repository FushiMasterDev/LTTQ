using Homestay.DTO.Common;
using Homestay.DTO.Enums;

namespace Homestay.DTO.Customers;

public sealed record CustomerSearchRequest { public PageRequest Page { get; init; } = new(); }
public sealed record CustomerItem(int Id, string FullName, string Phone, string? Email, string? IdNumber,
    bool IsIdentityMasked, string? Note, bool HasBookings, byte[] RowVer);
public sealed record CustomerBookingItem(int Id, string Code, string HomestayName, DateOnly CheckIn, DateOnly CheckOut,
    BookingStatus Status, PaymentState PaymentState, string PaymentStateName);
public sealed record CustomerVoucherItem(int Id, string Code, decimal Balance, DateOnly ExpiresOn);
public sealed record CustomerDetail(CustomerItem Customer, IReadOnlyList<CustomerBookingItem> RecentBookings,
    IReadOnlyList<CustomerVoucherItem> UsableVouchers);
public sealed record CreateCustomerRequest
{
    public string FullName { get; init; } = "";
    public string Phone { get; init; } = "";
    public string? Email { get; init; }
    public string? IdNumber { get; init; }
    public string? Note { get; init; }
}
public sealed record UpdateCustomerRequest
{
    public int CustomerId { get; init; }
    public string FullName { get; init; } = "";
    public string? Email { get; init; }
    // null = giữ nguyên. ClearIdNumber=true = xóa. Không gửi lại chuỗi CCCD đã che.
    public string? IdNumber { get; init; }
    public bool ClearIdNumber { get; init; }
    public string? Note { get; init; }
    public byte[] RowVer { get; init; } = [];
}
public sealed record ChangeCustomerPhoneRequest(int CustomerId, string Phone, byte[] RowVer);
