using System.Globalization;
using System.Text;

namespace Fnb.Application.Reports;

/// <summary>
/// RFC 4180 CSV writer with UTF-8 BOM (Excel compatible), invariant number formatting, ISO-8601 dates and
/// CSV/formula-injection protection: text cells beginning with = + - @ TAB or CR are prefixed with a single quote.
/// </summary>
public sealed class CsvWriter(Stream output) : IAsyncDisposable
{
    private readonly StreamWriter _writer = new(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 64 * 1024, leaveOpen: true) { NewLine = "\r\n" };

    public Task WriteRowAsync(params object?[] values) => _writer.WriteLineAsync(FormatRow(values));

    public static string FormatRow(IEnumerable<object?> values) => string.Join(',', values.Select(FormatCell));

    public static string FormatCell(object? value) => value switch
    {
        null => "",
        decimal d => d.ToString("0.##", CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        long l => l.ToString(CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("yyyy-MM-ddTHH:mm:ss'Z'", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture),
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Guid g => g.ToString(),
        bool b => b ? "true" : "false",
        _ => Escape(SanitizeText(value.ToString() ?? "")),
    };

    /// <summary>Neutralises spreadsheet formula injection in free-text cells.</summary>
    public static string SanitizeText(string text)
    {
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            return "'" + text;
        }

        return text;
    }

    public static string Escape(string text)
    {
        if (text.IndexOfAny([',', '"', '\n', '\r']) >= 0)
        {
            return "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        return text;
    }

    public async ValueTask DisposeAsync()
    {
        await _writer.FlushAsync();
        await _writer.DisposeAsync();
    }
}
