using System.Globalization;
using System.Security.Cryptography;
using Homestay.BLL.Infrastructure;
using Homestay.DTO.Enums;
using Homestay.DTO.Payments;

namespace Homestay.BLL.Rules;

public sealed record ValidatedEvidence(string FileName, string ContentType, byte[] Content, byte[] Sha256);

public static class ReceiptRules
{
    public const int MaxEvidenceBytes = 5 * 1024 * 1024;
    public const string ExpiredMessage = "Đơn đã quá hạn giữ phòng. Vui lòng tạo đơn mới cho khách.";
    public const string InsufficientMessage = "Số tiền thiếu so với số cần chuyển.";

    public static string DetectContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0 || bytes.Length > MaxEvidenceBytes)
            throw new BusinessException("Chứng từ phải có dữ liệu và không quá 5 MB. Vui lòng chọn lại tệp.");
        if (bytes.StartsWith(new byte[] { 0xFF, 0xD8, 0xFF })) return "image/jpeg";
        if (bytes.StartsWith(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        if (bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes.Slice(8, 4).SequenceEqual("WEBP"u8)) return "image/webp";
        if (bytes.StartsWith("%PDF-"u8)) return "application/pdf";
        throw new BusinessException("Chứng từ chỉ nhận JPG, PNG, WEBP hoặc PDF đúng chữ ký tệp. Vui lòng chọn lại tệp.");
    }

    public static ValidatedEvidence? ValidateEvidence(byte[]? bytes, string? fileName)
    {
        if (bytes is null) return null;
        // Own the bytes before validation/hash: caller may reuse its input buffer while the transaction runs/retries.
        if (bytes.Length > MaxEvidenceBytes) throw new BusinessException("Chứng từ không được quá 5 MB. Vui lòng chọn lại tệp.");
        var copy = bytes.ToArray();
        var contentType = DetectContentType(copy);
        var name = fileName?.Replace('\\', '/').Split('/').Last().Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Any(char.IsControl))
            throw new BusinessException("Tên chứng từ cần từ 1 đến 255 ký tự. Vui lòng chọn lại tệp.");
        return new(name, contentType, copy, SHA256.HashData(copy));
    }

    public static RecordReceiptRequest Normalize(RecordReceiptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        StaffInputRules.RowVer(request.RowVer);
        if (request.BookingId <= 0 || request.Amount <= 0 || request.Amount > 999999999999999999m || decimal.Truncate(request.Amount) != request.Amount)
            throw new BusinessException("Số tiền phải là số đồng nguyên lớn hơn 0, tối đa 18 chữ số. Vui lòng nhập lại.");
        if (!Enum.IsDefined(request.Method)) throw new BusinessException("Vui lòng chọn chuyển khoản hoặc tiền mặt.");
        static string? Text(string? value, int max)
        {
            StaffInputRules.Text(value, max);
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        var account = Text(request.PayerAccount, 30);
        if (account?.Any(c => c > 127 || char.IsControl(c)) == true)
            throw new BusinessException("Tài khoản người chuyển không hợp lệ. Vui lòng nhập lại.");
        if (request.Method == PaymentMethod.BankTransfer && request.TransferredAtUtc == default)
            throw new BusinessException("Vui lòng nhập thời điểm khách chuyển tiền (UTC).");
        if (request.TransferredAtUtc != default && request.TransferredAtUtc.Kind != DateTimeKind.Utc)
            throw new BusinessException("Thời điểm chuyển tiền phải là UTC. Vui lòng kiểm tra lại.");
        return request with { PayerName = Text(request.PayerName, 100), PayerAccount = account,
            TransferContent = Text(request.TransferContent, 255), RowVer = request.RowVer.ToArray() };
    }

    public static DateTime TransferredAt(RecordReceiptRequest request, DateTime serverNow)
    {
        var value = request.TransferredAtUtc == default ? serverNow : request.TransferredAtUtc;
        if (value > serverNow.AddMinutes(5)) throw new BusinessException("Thời điểm chuyển tiền ở tương lai quá 5 phút. Vui lòng kiểm tra lại.");
        return value;
    }

    // Never expose a short account in full either; at most four trailing characters, always with a mask.
    public static string? MaskAccount(string? account) => string.IsNullOrWhiteSpace(account) ? null
        : "••••" + account[^Math.Min(4, account.Length)..];
    public static string CounterKey(DateTime serverNowUtc)
    {
        if (serverNowUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("Server time must be UTC.", nameof(serverNowUtc));
        return "PT" + serverNowUtc.AddHours(7).ToString("yyMM", CultureInfo.InvariantCulture);
    }
    public static string VoucherNo(DateTime serverNowUtc, int sequence)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sequence);
        return CounterKey(serverNowUtc) + "-" + sequence.ToString("D4", CultureInfo.InvariantCulture);
    }
}
