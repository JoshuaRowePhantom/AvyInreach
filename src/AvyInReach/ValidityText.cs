using System.Globalization;

namespace AvyInReach;

internal static class ValidityText
{
    private static readonly IReadOnlyDictionary<string, (string Standard, string Daylight)> TimezoneAbbreviations =
        new Dictionary<string, (string Standard, string Daylight)>(StringComparer.OrdinalIgnoreCase)
        {
            ["America/Vancouver"] = ("PST", "PDT"),
            ["America/Edmonton"] = ("MST", "MDT"),
            ["America/Winnipeg"] = ("CST", "CDT"),
            ["America/Toronto"] = ("EST", "EDT"),
            ["America/Halifax"] = ("AST", "ADT"),
            ["America/St_Johns"] = ("NST", "NDT"),
            ["America/New_York"] = ("EST", "EDT"),
        };

    public static string Format(DateTimeOffset value, string timezoneId)
    {
        var timezone = ResolveTimeZone(timezoneId);
        var local = TimeZoneInfo.ConvertTime(value, timezone);
        var abbreviation = ResolveAbbreviation(timezoneId, timezone, local);
        return $"{local.Month}/{local.Day} {local:HH:mm}{abbreviation}";
    }

    private static TimeZoneInfo ResolveTimeZone(string timezoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            if (TimeZoneInfo.TryConvertIanaIdToWindowsId(timezoneId, out var windowsId))
            {
                return TimeZoneInfo.FindSystemTimeZoneById(windowsId);
            }

            throw;
        }
    }

    private static string ResolveAbbreviation(string timezoneId, TimeZoneInfo timezone, DateTimeOffset local)
    {
        if (TimezoneAbbreviations.TryGetValue(timezoneId, out var abbreviations))
        {
            return timezone.IsDaylightSavingTime(local.DateTime)
                ? abbreviations.Daylight
                : abbreviations.Standard;
        }

        var name = timezone.IsDaylightSavingTime(local.DateTime)
            ? timezone.DaylightName
            : timezone.StandardName;

        var parts = name
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part[0]);

        return new string(parts.ToArray()).ToUpper(CultureInfo.InvariantCulture);
    }
}
