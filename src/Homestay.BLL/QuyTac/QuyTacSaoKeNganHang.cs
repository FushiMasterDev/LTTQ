using System.Text.RegularExpressions;

namespace Homestay.BLL.Rules;

public static class BankStatementRules
{
    public static string? ExtractBookingCode(string? content){if(string.IsNullOrWhiteSpace(content))return null;var match=Regex.Match(content.ToUpperInvariant(),@"F[^A-Z0-9]*J[^A-Z0-9]*((?:[0-9][^A-Z0-9]*){8}[0-9])(?![0-9])",RegexOptions.CultureInvariant);if(!match.Success)return null;var digits=Regex.Replace(match.Groups[1].Value,"[^0-9]","");return $"FJ{digits[..6]}-{digits[6..]}";}
}
