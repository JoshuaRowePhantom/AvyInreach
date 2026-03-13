using System.Text.RegularExpressions;

namespace AvyInReach;

internal static partial class ForecastText
{
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var withLineBreaks = html
            .Replace("</p>", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", " ", StringComparison.OrdinalIgnoreCase);

        var stripped = HtmlTagRegex().Replace(withLineBreaks, " ");
        stripped = System.Net.WebUtility.HtmlDecode(stripped);
        return WhitespaceRegex().Replace(stripped, " ").Trim();
    }

    public static string NormalizeKey(string value) =>
        new string(value.Where(ch => char.IsLetterOrDigit(ch)).ToArray()).ToLowerInvariant();

    [GeneratedRegex("<.*?>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();
}
