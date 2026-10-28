using Homestay.BLL.Infrastructure;
using Homestay.DTO.Homestays;
using Homestay.DTO.Owners;

namespace Homestay.BLL.Rules;

internal static class ListingWriteRules
{
    internal static string? Text(string? value, int max)
    {
        var text = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        StaffInputRules.Text(text, max);
        return text;
    }
    internal static string Required(string? value, int max, string label) => Text(value, max)
        ?? throw new BusinessException($"Vui lòng nhập {label} từ 1 đến {max} ký tự.");
    internal static string Reason(string? value) => Required(value, 500, "lý do");
    internal static string? Ascii(string? value, int max, string label)
    {
        var text = Text(value, max);
        if (text?.Any(c => c > 127 || char.IsControl(c)) == true)
            throw new BusinessException($"{label} chỉ nhận ký tự ASCII, không nhận ký tự điều khiển hoặc giá trị đã che. Vui lòng nhập lại.");
        return text;
    }
    internal static CreateOwnerRequest Owner(CreateOwnerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request with { FullName = CustomerInputRules.Name(request.FullName), Phone = PhoneNumber.Normalize(request.Phone),
            Email = CustomerInputRules.Email(request.Email), Address = Text(request.Address, 300),
            IdNumber = Ascii(request.IdNumber, 20, "Số giấy tờ"), BankName = Text(request.BankName, 100),
            BankAccountNo = Ascii(request.BankAccountNo, 30, "Số tài khoản"), BankAccountName = Text(request.BankAccountName, 100), Note = Text(request.Note, 1000) };
    }
    internal static CreateHomestayRequest Home(CreateHomestayRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.OwnerId <= 0 || request.AreaId <= 0) throw new BusinessException("Vui lòng chọn chủ và khu vực hợp lệ.");
        return request with { Name = Required(request.Name, 200, "tên homestay"), Address = Required(request.Address, 300, "địa chỉ"),
            ContactPhone = Text(request.ContactPhone, 16) is { } phone ? PhoneNumber.Normalize(phone) : null,
            Description = Text(request.Description, 4000), CheckinGuide = Text(request.CheckinGuide, 2000),
            CheckinTime = new TimeOnly(request.CheckinTime.Hour, request.CheckinTime.Minute, request.CheckinTime.Second),
            CheckoutTime = new TimeOnly(request.CheckoutTime.Hour, request.CheckoutTime.Minute, request.CheckoutTime.Second) };
    }
}
