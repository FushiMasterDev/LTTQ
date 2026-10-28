using System.Data;
using System.Globalization;
using System.Text;

namespace Homestay.GUI.Common;

public static class CsvExporter
{
    public static string Cell(object? value)
    {
        var text = value switch
        {
            null or DBNull => "",
            DateTime date => date.ToString("dd/MM/yyyy HH:mm", UiFormat.Vietnamese),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
            _ => value.ToString() ?? ""
        };
        // Protect Excel from treating customer-supplied text as a formula.
        if (value is string && text.TrimStart().StartsWithAny('=', '+', '-', '@'))
            text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
    public static async Task WriteAsync(string path, IReadOnlyList<string> headers,
        IEnumerable<object?[]> rows, CancellationToken token = default)
    {
        await using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        await using var writer = new StreamWriter(stream, new UTF8Encoding(true));
        await writer.WriteLineAsync(string.Join(",", headers.Select(Cell)).AsMemory(), token);
        foreach (var row in rows)
        {
            token.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(string.Join(",", row.Select(Cell)).AsMemory(), token);
        }
    }
    private static bool StartsWithAny(this string value, params char[] chars)
        => value.Length > 0 && chars.Contains(value[0]);
}
