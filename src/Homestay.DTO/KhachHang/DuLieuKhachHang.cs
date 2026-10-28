using Homestay.DTO.Common;
using Homestay.DTO.Enums;

namespace Homestay.DTO.Customers;

/// <summary>Điều kiện tìm khách hàng (phân trang và từ khóa).</summary>
public sealed record CustomerSearchRequest { public PageRequest Page { get; init; } = new(); }
/// <summary>Một khách hàng hiển thị trên danh sách/chi tiết.</summary>
public sealed record CustomerItem(int Id, string FullName, string Phone, string? Email, string? IdNumber,
    bool IsIdentityMasked, string? Note, bool HasBookings, byte[] RowVer);
/// <summary>Một đơn gần đây của khách, hiển thị trong hồ sơ khách.</summary>
public sealed record CustomerBookingItem(int Id, string Code, string HomestayName, DateOnly CheckIn, DateOnly CheckOut,
    BookingStatus Status, PaymentState PaymentState, string PaymentStateName);
/// <summary>Một voucher còn của khách (mã, số dư, ngày hết hạn).</summary>
public sealed record CustomerVoucherItem(int Id, string Code, decimal Balance, DateOnly ExpiresOn);
/// <summary>Hồ sơ đầy đủ của khách: thông tin, đơn gần đây và voucher.</summary>
public sealed record CustomerDetail(CustomerItem Customer, IReadOnlyList<CustomerBookingItem> RecentBookings,
    IReadOnlyList<CustomerVoucherItem> UsableVouchers);
/// <summary>Dữ liệu nhập để tạo khách hàng mới.</summary>
public sealed record CreateCustomerRequest
{
    public string FullName { get; init; } = "";
    public string Phone { get; init; } = "";
    public string? Email { get; init; }
    public string? IdNumber { get; init; }
    public string? Note { get; init; }
}
/// <summary>Yêu cầu sửa thông tin khách hàng (không gồm số điện thoại).</summary>
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
/// <summary>Yêu cầu đổi số điện thoại của khách (thao tác riêng vì là khóa tra cứu).</summary>
public sealed record ChangeCustomerPhoneRequest(int CustomerId, string Phone, byte[] RowVer);
