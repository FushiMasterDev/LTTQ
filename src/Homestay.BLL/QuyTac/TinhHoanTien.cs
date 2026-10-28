using Homestay.BLL.Infrastructure;
using Homestay.DTO.Bookings;

namespace Homestay.BLL.Rules;

/// <summary>Spec 7.6.3. Uses the policy already stored on the booking; never selects an active policy.</summary>
public static class RefundCalculator
{
    public static RefundCalculation Calculate(RefundCalculationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Policy);
        if (!Enum.IsDefined(request.Reason) || request.AmountDue < 0 || request.TotalAmount is < 0
            || request.CommissionRate is <= 0 or >= 1)
            throw new BusinessException("Dữ liệu tính tiền hoàn không hợp lệ.");
        var days = request.CheckIn.DayNumber - request.CancelledOn.DayNumber;
        var percent = 0;
        if (request.HasCollectedDeposit && request.Reason != RefundReason.UnpaidCancellation)
        {
            if (request.Reason is RefundReason.OwnerRejected or RefundReason.HomestayUnavailable) percent = 100;
            else if (request.Reason == RefundReason.CustomerCancellation)
            {
                CancellationPolicyRules.Validate(request.Policy.Rules);
                // After check-in (negative days) there is no matching nonnegative threshold: refund 0%.
                percent = request.Policy.Rules.Where(r => r.MinDaysBefore <= days)
                    .OrderByDescending(r => r.MinDaysBefore).FirstOrDefault()?.RefundPercent ?? 0;
            }
        }
        var refund = percent == 100 && (request.Reason is RefundReason.OwnerRejected or RefundReason.HomestayUnavailable)
            ? request.AmountDue : PricingCalculator.RoundToThousand(request.AmountDue * percent / 100m);
        // Valid deposit snapshots can contain a non-thousand discount. A policy must not refund more than collected.
        if (refund > request.AmountDue) throw new BusinessException("Tiền hoàn sau làm tròn vượt số đã thu. Vui lòng báo Quản lý kiểm tra dữ liệu đơn.");
        var retained = request.HasCollectedDeposit && request.Reason != RefundReason.UnpaidCancellation ? request.AmountDue - refund : 0;
        decimal? commission = request.TotalAmount is { } total && request.CommissionRate is { } rate
            ? Math.Min(decimal.Round(total * rate, 0, MidpointRounding.AwayFromZero), retained) : null;
        return new(request.Policy.Id, days, percent, refund, retained, commission, commission is { } value ? retained - value : null);
    }
}
