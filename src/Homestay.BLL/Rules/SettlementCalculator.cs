using Homestay.BLL.Infrastructure;
using Homestay.DTO.Enums;
using Homestay.DTO.Finance;

namespace Homestay.BLL.Rules;

/// <summary>Single implementation of the settlement formula in spec 7.6.6.</summary>
public static class SettlementCalculator
{
    public static SettlementLine? Calculate(SettlementCalculationInput x)
    {
        ArgumentNullException.ThrowIfNull(x);
        if (x.BookingId <= 0 || x.TotalAmount < 0 || x.AmountDue < 0 || x.DepositAmount < 0 ||
            x.RefundAmount is < 0 || x.ChangeFeeAmount < 0 || x.DiscountAmount < 0 || x.CommissionRate is <= 0 or >= 1)
            throw new BusinessException("Dữ liệu đơn dùng lập đối soát không hợp lệ.");
        if (!Enum.IsDefined(x.Status)) throw new BusinessException("Trạng thái đơn không hợp lệ.");
        if (x.Replaced) return null;
        var feeOnly = x.Status is BookingStatus.Rejected or BookingStatus.Expired ||
            x.Status == BookingStatus.Cancelled && !x.HasVerifiedDeposit;
        if (x.Status is not (BookingStatus.Completed or BookingStatus.NoShow or BookingStatus.Cancelled or BookingStatus.Rejected or BookingStatus.Expired)) return null;
        var refund = x.RefundAmount ?? 0m;
        var percent = x.AmountDue > 0 ? decimal.Round(100m * refund / x.AmountDue, 0, MidpointRounding.AwayFromZero) : 0m;
        if (percent is < 0 or > 100) throw new BusinessException("Tiền hoàn vượt nghĩa vụ thu của đơn.");
        var baseRetained = feeOnly ? 0m : x.DepositAmount - PricingCalculator.RoundToThousand(x.DepositAmount * percent / 100m);
        var baseCommission = Math.Min(decimal.Round(x.TotalAmount * x.CommissionRate, 0, MidpointRounding.AwayFromZero), baseRetained);
        var feeCommission = Math.Min(decimal.Round(x.ChangeFeeAmount * x.CommissionRate, 0, MidpointRounding.AwayFromZero), x.ChangeFeeAmount);
        var retained = baseRetained + x.ChangeFeeAmount;
        if (retained <= 0) return null;
        var commission = baseCommission + feeCommission;
        var discount = feeOnly ? 0m : Math.Max(0m, x.DiscountAmount - PricingCalculator.RoundToThousand(x.DiscountAmount * percent / 100m));
        var type = feeOnly ? SettlementItemType.FeeOnly : x.Status switch
        {
            BookingStatus.Completed => SettlementItemType.Completed,
            BookingStatus.NoShow => SettlementItemType.NoShow,
            BookingStatus.Cancelled => SettlementItemType.CancelledRetained,
            _ => SettlementItemType.FeeOnly
        };
        var depositAmount = x.Status == BookingStatus.Cancelled && !x.HasVerifiedDeposit
            ? x.ChangeFeeAmount
            : x.DepositAmount + x.ChangeFeeAmount;
        return new SettlementLine(x.BookingId, x.BookingCode, type, x.TotalAmount, depositAmount,
            refund, retained, commission, discount, retained - commission, x.CommissionRate) { CheckOut = x.CheckOut };
    }
}
