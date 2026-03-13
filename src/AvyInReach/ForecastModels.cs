namespace AvyInReach;

internal sealed record ForecastRegion(
    string ProviderId,
    string DisplayName,
    string ReportId,
    string AreaId,
    string ForecastUrl);

internal sealed record AvalancheForecast(
    ForecastRegion Region,
    string ForecastUrl,
    string Title,
    string OwnerName,
    DateTimeOffset IssuedAt,
    DateTimeOffset ValidUntil,
    string TimezoneId,
    DangerRatings CurrentDangerRatings,
    IReadOnlyList<ForecastProblem> Problems,
    string Highlights,
    string AvalancheSummary,
    string SnowpackSummary,
    string WeatherSummary,
    string? Message);

internal sealed record DangerRatings(int? BelowTreeline, int? Treeline, int? Alpine)
{
    public string ToCompactString() =>
        $"{Format(BelowTreeline)}/{Format(Treeline)}/{Format(Alpine)}";

    private static string Format(int? value) => value?.ToString() ?? "NR";
}

internal sealed record ForecastProblem(
    string Name,
    bool BelowTreeline,
    bool Treeline,
    bool Alpine,
    decimal? SizeMin,
    decimal? SizeMax,
    IReadOnlyList<string> Aspects,
    string Comment)
{
    public string PresenceString() =>
        $"{Presence(BelowTreeline)}/{Presence(Treeline)}/{Presence(Alpine)}";

    public string SizeRangeString()
    {
        if (SizeMin is null && SizeMax is null)
        {
            return "NA";
        }

        if (SizeMin is not null && SizeMax is not null)
        {
            return $"{Format(SizeMin.Value)}-{Format(SizeMax.Value)}";
        }

        return Format(SizeMin ?? SizeMax ?? 0m);
    }

    public string AspectString()
    {
        if (Aspects.Count == 0)
        {
            return "ALL";
        }

        var normalized = Aspects
            .Select(AspectFormat.Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return normalized.Count == 8
            ? "ALL"
            : string.Join('-', normalized);
    }

    private static string Presence(bool isPresent) => isPresent ? "O" : "X";

    private static string Format(decimal value)
    {
        var whole = decimal.Truncate(value);
        return value == whole ? whole.ToString("0") : value.ToString("0.#");
    }
}
