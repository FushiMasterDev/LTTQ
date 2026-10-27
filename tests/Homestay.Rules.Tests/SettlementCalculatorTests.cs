using Homestay.BLL.Rules;
using Homestay.DTO.Enums;
using Homestay.DTO.Finance;

namespace Homestay.Rules.Tests;

public sealed class SettlementCalculatorTests
{
    private static SettlementCalculationInput Booking(BookingStatus status=BookingStatus.Completed)=>new(1,"FJ261026-001",status,new(2026,10,20),false,2_000_000,600_000,600_000,0,0,0,.12m);

    [Fact]
    public void CompletedBookingMatchesSpecExample()=>Assert.Equal(new SettlementLine(1,"FJ261026-001",SettlementItemType.Completed,2_000_000,600_000,0,600_000,240_000,0,360_000,.12m){CheckOut=new(2026,10,20)},SettlementCalculator.Calculate(Booking()));
    [Fact]
    public void CancellationUsesRoundedRefundPercentageAndRetainsOnlyUnrefundedDeposit(){var x=SettlementCalculator.Calculate(Booking(BookingStatus.Cancelled) with{RefundAmount=300_000});Assert.NotNull(x);Assert.Equal(300_000,x.RetainedAmount);Assert.Equal(240_000,x.CommissionAmount);Assert.Equal(60_000,x.PayoutAmount);}
    [Fact]
    public void RejectedBookingOnlySettlesChangeFee(){var x=SettlementCalculator.Calculate(Booking(BookingStatus.Rejected) with{ChangeFeeAmount=400_000});Assert.NotNull(x);Assert.Equal(SettlementItemType.FeeOnly,x.ItemType);Assert.Equal(400_000,x.RetainedAmount);Assert.Equal(48_000,x.CommissionAmount);Assert.Equal(352_000,x.PayoutAmount);Assert.Equal(0,x.DiscountAmount);}
    [Fact]
    public void NoRetainedFundsOrReplacedBookingHasNoLine(){Assert.Null(SettlementCalculator.Calculate(Booking(BookingStatus.Cancelled) with{RefundAmount=600_000}));Assert.Null(SettlementCalculator.Calculate(Booking() with{Replaced=true}));}
    [Theory][InlineData(0,"Không đồng")][InlineData(1,"Một đồng")][InlineData(21,"Hai mươi mốt đồng")][InlineData(105,"Một trăm lẻ năm đồng")][InlineData(1_234_000,"Một triệu hai trăm ba mươi bốn nghìn đồng")]
    public void ReadsVndAmount(decimal amount,string expected)=>Assert.Equal(expected,VietnameseAmountWords.Spell(amount));
}
