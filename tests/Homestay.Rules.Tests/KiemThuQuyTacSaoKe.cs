using Homestay.BLL.Rules;

namespace Homestay.Rules.Tests;

public sealed class BankStatementRulesTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" \t\n", null)]
    [InlineData("chuyen tien fj261026007", "FJ261026-007")]
    [InlineData("FJ261026-007", "FJ261026-007")]
    [InlineData("FJ.261026.007", "FJ261026-007")]
    [InlineData("FJ261026-007 2 dem", "FJ261026-007")]
    [InlineData("FJ261026007 500000", "FJ261026-007")]
    [InlineData("TT FJ261026 007 1TR", "FJ261026-007")]
    [InlineData("f j / 26 10\t26 _ 0-0.7", "FJ261026-007")]
    [InlineData("FJ261026007ABC", "FJ261026-007")]
    [InlineData("FJ26102600", null)]
    [InlineData("FJ2610260071", null)]
    [InlineData("FJ261026007.1", "FJ261026-007")]
    [InlineData("FJ26102600712345", null)]
    [InlineData("FJ２６１０２６００７", null)]
    [InlineData("FJ261A026007", null)]
    [InlineData("FJ2610260071 FJ261027008", "FJ261027-008")]
    public void ExtractsNineAsciiDigitsWithSeparatorsAndOriginalTextBoundary(string? content, string? expected)
        => Assert.Equal(expected, BankStatementRules.ExtractBookingCode(content));
}
