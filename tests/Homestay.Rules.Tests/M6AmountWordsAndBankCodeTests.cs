using Homestay.BLL.Rules;

namespace Homestay.Rules.Tests;

public sealed class M6AmountWordsAndBankCodeTests
{
    [Theory]
    [InlineData(0, "Không đồng")]
    [InlineData(1_000, "Một nghìn đồng")]
    [InlineData(10_000, "Mười nghìn đồng")]
    [InlineData(105_000, "Một trăm lẻ năm nghìn đồng")]
    [InlineData(1_000_005, "Một triệu không trăm lẻ năm đồng")]
    [InlineData(21_000_000, "Hai mươi mốt triệu đồng")]
    [InlineData(1_250_000_000, "Một tỷ hai trăm năm mươi triệu đồng")]
    [InlineData(15_000, "Mười lăm nghìn đồng")]
    [InlineData(25_000, "Hai mươi lăm nghìn đồng")]
    // “Hai mươi bốn” and “hai mươi tư” are both standard; implementation consistently uses “bốn”.
    [InlineData(24_000, "Hai mươi bốn nghìn đồng")]
    [InlineData(14_000, "Mười bốn nghìn đồng")]
    [InlineData(104_000, "Một trăm lẻ bốn nghìn đồng")]
    public void SpellsReceiptAmountsWithVietnameseGroupAndUnitConventions(decimal amount, string expected)
        => Assert.Equal(expected, VietnameseAmountWords.Spell(amount));

    [Fact]
    public void SpellsMaximumEighteenDigitVndAmount()
        => Assert.Equal("Chín trăm chín mươi chín triệu tỷ chín trăm chín mươi chín nghìn tỷ chín trăm chín mươi chín tỷ chín trăm chín mươi chín triệu chín trăm chín mươi chín nghìn chín trăm chín mươi chín đồng",
            VietnameseAmountWords.Spell(999_999_999_999_999_999m));

    [Theory]
    [InlineData("-1")]
    [InlineData("0.5")]
    [InlineData("1000000000000000000")]
    public void RejectsAmountsOutsideIntegerVndContract(string amount)
        => Assert.Throws<ArgumentOutOfRangeException>(() => VietnameseAmountWords.Spell(
            decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("TT FJ261027-001", "FJ261027-001")]
    [InlineData("TT FJ261027001", "FJ261027-001")]
    [InlineData("tt fj261027-001", "FJ261027-001")]
    [InlineData("TT F-J 261027 / 001", "FJ261027-001")]
    // Spec does not choose between multiple valid codes: current rule takes first textual match.
    [InlineData("FJ261027-002 va FJ261027-001", "FJ261027-002")]
    [InlineData("Khach chuyen coc khong ghi ma", null)]
    public void ExtractsBookingCodeAccordingToSpecNormalization(string content, string? expected)
        => Assert.Equal(expected, BankStatementRules.ExtractBookingCode(content));
}
