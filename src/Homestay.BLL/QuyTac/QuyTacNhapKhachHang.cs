using System.Net.Mail;
using Homestay.BLL.Infrastructure;
using Homestay.DTO.Customers;

namespace Homestay.BLL.Rules;

/// <summary>Kiểm tra và chuẩn hóa dữ liệu nhập của khách hàng: họ tên, email, giấy tờ tùy thân, ghi chú.</summary>
internal static class CustomerInputRules
{
    internal static CreateCustomerRequest Normalize(CreateCustomerRequest request) => request with
    {
        FullName = Name(request.FullName), Phone = PhoneNumber.Normalize(request.Phone),
        Email = Email(request.Email), IdNumber = Identity(request.IdNumber), Note = Note(request.Note)
    };
    internal static string Name(string? value)
    {
        var name = value?.Trim() ?? "";
        if (name.Length is < 1 or > 100) throw new BusinessException("Họ tên phải có từ 1 đến 100 ký tự. Vui lòng nhập lại.");
        return name;
    }
    internal static string? Email(string? value)
    {
        var email = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (email is not null && (email.Length > 256 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email))
            throw new BusinessException("Email không hợp lệ. Vui lòng nhập lại hoặc để trống.");
        return email;
    }
    internal static string? Identity(string? value)
    {
        var identity = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (identity is not null && (identity.Length > 20 || identity.Any(c => !char.IsAsciiLetterOrDigit(c))))
            throw new BusinessException("CCCD/hộ chiếu tối đa 20 chữ hoặc số. Không nhập lại giá trị đã che.");
        return identity;
    }
    internal static string? Note(string? value) { StaffInputRules.Text(value, 1000); return string.IsNullOrWhiteSpace(value) ? null : value.Trim(); }
}
