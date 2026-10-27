using Homestay.DTO.Payments;
namespace Homestay.BLL.Services;

public sealed partial class PaymentService
{
    public Task<RefundAccountResult> UpdateRefundAccountAsync(UpdateRefundAccountRequest request, CancellationToken cancellationToken = default)
        => throw new NotImplementedException();
}
