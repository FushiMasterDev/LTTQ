using Homestay.DTO.Payments;

namespace Homestay.BLL.Services;

public sealed class PaymentService
{
    /// <summary>M2.4: Require PaymentRecord, kiểm phiên/quyền/phạm vi, RowVer, AwaitingPayment và hạn giữ;
    /// kiểm số tiền/thời điểm/phương thức; ảnh tối đa 5 MB, JPG/PNG/WEBP/PDF theo chữ ký tệp;
    /// tạo phiếu Deposit Pending + chứng từ, chuyển PaymentSubmitted, lịch sử/audit/thông báo trong cùng transaction.
    /// Kế toán được ghi nhận nhưng không tự xác minh. Không lưu thời điểm khách chuyển vào RecordedAt.</summary>
    public Task<RecordReceiptResult> RecordReceiptAsync(RecordReceiptRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException("M2.4: ghi nhận thu cọc chưa triển khai.");
}
