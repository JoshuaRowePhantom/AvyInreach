using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AvyInReach;

internal sealed partial class NwacProvider(HttpClient httpClient, IProcessRunner processRunner) : IAvalancheProvider
{
    private const string ForecastsUrl = "https://nwac.us/api/v2/avalanche-region-forecast";
    private const string WeatherSummaryUrl = "https://nwac.us/weather-forecast-summary/";
    private const string TimezoneId = "America/Los_Angeles";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly TimeZoneInfo PacificTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

    private static readonly IReadOnlyList<NwacRegionDefinition> Regions =
    [
        new("east-slopes-central", "East Central", "East Central", ["eastslopescentral", "eastcentral"]),
        new("east-slopes-north", "East North", "East North", ["eastslopesnorth", "eastnorth"]),
        new("east-slopes-south", "East South", "East South", ["eastslopessouth", "eastsouth"]),
        new("mt-hood", "Mt Hood", "Mt Hood", ["mthood", "mthoodoregon", "mthoodor", "mthoodwa", "mt. hood", "hood"]),
        new("olympics", "Olympics", "Olympics", ["olympic", "olympicmountains"]),
        new("snoqualmie-pass", "Snoqualmie Pass", "Snoqualmie Pass", ["snoqualmie", "snoq", "snoqualmiepass"]),
        new("stevens-pass", "Stevens Pass", "Stevens Pass", ["stevens", "stevenspass"]),
        new("west-slopes-central", "West Central", "West Central", ["westslopescentral", "westcentral"]),
        new("west-slopes-north", "West North", "West North", ["westslopesnorth", "westnorth"]),
        new("west-slopes-south", "West South", "West South", ["westslopessouth", "westsouth"]),
    ];

    public string Id => "nwac";

    public IReadOnlyList<string> Aliases => ["nwac", "northwest-avalanche-center", "northwestavalanchecenter"];

    public Task<IReadOnlyList<ForecastRegion>> GetRegionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ForecastRegion>>(
            Regions
                .Select(region => new ForecastRegion(
                    Id,
                    region.DisplayName,
                    region.ReportId,
                    region.WeatherZone,
                    $"https://nwac.us/avalanche-forecast/#/{region.ReportId}"))
                .OrderBy(region => region.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList());

    public async Task<ForecastRegion?> ResolveRegionAsync(string regionName, CancellationToken cancellationToken)
    {
        var regions = await GetRegionsAsync(cancellationToken);
        var normalizedInput = ForecastText.NormalizeKey(regionName);

        var directMatch = regions.FirstOrDefault(region =>
            ForecastText.NormalizeKey(region.DisplayName) == normalizedInput
            || ForecastText.NormalizeKey(region.ReportId) == normalizedInput
            || ForecastText.NormalizeKey(region.AreaId) == normalizedInput
            || GetAliases(region).Any(alias => ForecastText.NormalizeKey(alias) == normalizedInput));
        if (directMatch is not null)
        {
            return directMatch;
        }

        var containsMatches = regions.Where(region =>
                ForecastText.NormalizeKey(region.DisplayName).Contains(normalizedInput, StringComparison.Ordinal)
                || normalizedInput.Contains(ForecastText.NormalizeKey(region.DisplayName), StringComparison.Ordinal)
                || GetAliases(region).Any(alias =>
                    ForecastText.NormalizeKey(alias).Contains(normalizedInput, StringComparison.Ordinal)
                    || normalizedInput.Contains(ForecastText.NormalizeKey(alias), StringComparison.Ordinal)))
            .ToList();
        if (containsMatches.Count == 1)
        {
            return containsMatches[0];
        }

        return await ResolveRegionWithCopilotAsync(regionName, regions, cancellationToken);
    }

    public async Task<AvalancheForecast?> GetForecastAsync(ForecastRegion region, CancellationToken cancellationToken)
    {
        var match = await FindForecastAsync(region, cancellationToken);
        if (match is null)
        {
            return null;
        }

        var weatherHtml = await GetWeatherSummaryHtmlAsync(region.AreaId, cancellationToken);
        var issuedAt = TryParseIssuedAt(weatherHtml) ?? match.PublishDate;
        var validUntil = BuildValidUntil(match.Day1Date, issuedAt);

        return new AvalancheForecast(
            region,
            region.ForecastUrl,
            match.PrimaryZoneName ?? region.DisplayName,
            "Northwest Avalanche Center",
            issuedAt,
            validUntil,
            TimezoneId,
            new DangerRatings(
                ParseDangerValue(match.Day1DangerElevLow),
                ParseDangerValue(match.Day1DangerElevMiddle),
                ParseDangerValue(match.Day1DangerElevHigh)),
            match.Problems?
                .OrderBy(problem => problem.Order)
                .Take(2)
                .Select(MapProblem)
                .ToList() ?? [],
            FirstNonEmpty(match.BottomLineSummary, match.Day1DetailedForecast),
            FirstNonEmpty(match.Day1DetailedForecast, match.BottomLineSummary),
            ForecastText.ToPlainText(match.SnowpackDiscussion),
            ParseWeatherSummary(weatherHtml),
            FirstNonEmpty(
                match.SpecialStatement,
                BuildWarningText(match.Day1Warning, match.Day1WarningText),
                match.OptionalDiscussion));
    }

    private async Task<ForecastItem?> FindForecastAsync(ForecastRegion region, CancellationToken cancellationToken)
    {
        const int pageSize = 100;
        for (var offset = 0; ; offset += pageSize)
        {
            var forecasts = await GetForecastsAsync(pageSize, offset, cancellationToken);
            var match = forecasts.Objects.FirstOrDefault(item => MatchesRegion(item, region));
            if (match is not null)
            {
                return match;
            }

            if (forecasts.Meta?.Next is null || forecasts.Objects.Count == 0)
            {
                return null;
            }
        }
    }

    private async Task<ForecastCollection> GetForecastsAsync(int limit, int offset, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync($"{ForecastsUrl}?limit={limit}&offset={offset}", cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<ForecastCollection>(stream, SerializerOptions, cancellationToken)
            ?? new ForecastCollection();
    }

    private async Task<string> GetWeatherSummaryHtmlAsync(string weatherZone, CancellationToken cancellationToken)
    {
        var url = new StringBuilder(WeatherSummaryUrl)
            .Append("?zone=")
            .Append(Uri.EscapeDataString(weatherZone))
            .Append("&published_datetime=latest");

        using var response = await httpClient.GetAsync(url.ToString(), cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static bool MatchesRegion(ForecastItem item, ForecastRegion region)
    {
        var normalizedCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ForecastText.NormalizeKey(region.DisplayName),
            ForecastText.NormalizeKey(region.ReportId),
            ForecastText.NormalizeKey(region.AreaId),
        };

        foreach (var alias in GetAliases(region))
        {
            normalizedCandidates.Add(ForecastText.NormalizeKey(alias));
        }

        return item.Zones?.Any(zone =>
            normalizedCandidates.Contains(ForecastText.NormalizeKey(zone.ZoneName ?? string.Empty))
            || normalizedCandidates.Contains(ForecastText.NormalizeKey(zone.ZoneAbbrev ?? string.Empty))
            || normalizedCandidates.Contains(ForecastText.NormalizeKey(zone.Slug ?? string.Empty))) == true;
    }

    private static ForecastProblem MapProblem(ProblemItem problem)
    {
        var activeAspects = GetActiveAspects(problem);
        return new ForecastProblem(
            problem.ProblemType?.Name ?? "Problem",
            HasElevation(problem, "low"),
            HasElevation(problem, "mid"),
            HasElevation(problem, "high"),
            ParseLikelihoodValue(problem.Likelihood),
            ParseScaleValue(problem.MinimumSize),
            ParseScaleValue(problem.MaximumSize),
            activeAspects,
            FirstNonEmpty(problem.ProblemDescription, problem.ProblemType?.RiskManagementDescription));
    }

    private static IReadOnlyList<string> GetActiveAspects(ProblemItem problem)
    {
        var aspects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddAspectIfTrue(aspects, problem.OctagonLowNorth, "N");
        AddAspectIfTrue(aspects, problem.OctagonLowNortheast, "NE");
        AddAspectIfTrue(aspects, problem.OctagonLowEast, "E");
        AddAspectIfTrue(aspects, problem.OctagonLowSoutheast, "SE");
        AddAspectIfTrue(aspects, problem.OctagonLowSouth, "S");
        AddAspectIfTrue(aspects, problem.OctagonLowSouthwest, "SW");
        AddAspectIfTrue(aspects, problem.OctagonLowWest, "W");
        AddAspectIfTrue(aspects, problem.OctagonLowNorthwest, "NW");
        AddAspectIfTrue(aspects, problem.OctagonMidNorth, "N");
        AddAspectIfTrue(aspects, problem.OctagonMidNortheast, "NE");
        AddAspectIfTrue(aspects, problem.OctagonMidEast, "E");
        AddAspectIfTrue(aspects, problem.OctagonMidSoutheast, "SE");
        AddAspectIfTrue(aspects, problem.OctagonMidSouth, "S");
        AddAspectIfTrue(aspects, problem.OctagonMidSouthwest, "SW");
        AddAspectIfTrue(aspects, problem.OctagonMidWest, "W");
        AddAspectIfTrue(aspects, problem.OctagonMidNorthwest, "NW");
        AddAspectIfTrue(aspects, problem.OctagonHighNorth, "N");
        AddAspectIfTrue(aspects, problem.OctagonHighNortheast, "NE");
        AddAspectIfTrue(aspects, problem.OctagonHighEast, "E");
        AddAspectIfTrue(aspects, problem.OctagonHighSoutheast, "SE");
        AddAspectIfTrue(aspects, problem.OctagonHighSouth, "S");
        AddAspectIfTrue(aspects, problem.OctagonHighSouthwest, "SW");
        AddAspectIfTrue(aspects, problem.OctagonHighWest, "W");
        AddAspectIfTrue(aspects, problem.OctagonHighNorthwest, "NW");
        return aspects.ToList();
    }

    private static void AddAspectIfTrue(HashSet<string> aspects, bool? isPresent, string value)
    {
        if (isPresent == true)
        {
            aspects.Add(value);
        }
    }

    private static bool HasElevation(ProblemItem problem, string elevation) =>
        elevation switch
        {
            "low" => Any(problem.OctagonLowNorth, problem.OctagonLowNortheast, problem.OctagonLowEast, problem.OctagonLowSoutheast, problem.OctagonLowSouth, problem.OctagonLowSouthwest, problem.OctagonLowWest, problem.OctagonLowNorthwest),
            "mid" => Any(problem.OctagonMidNorth, problem.OctagonMidNortheast, problem.OctagonMidEast, problem.OctagonMidSoutheast, problem.OctagonMidSouth, problem.OctagonMidSouthwest, problem.OctagonMidWest, problem.OctagonMidNorthwest),
            "high" => Any(problem.OctagonHighNorth, problem.OctagonHighNortheast, problem.OctagonHighEast, problem.OctagonHighSoutheast, problem.OctagonHighSouth, problem.OctagonHighSouthwest, problem.OctagonHighWest, problem.OctagonHighNorthwest),
            _ => false,
        };

    private static bool Any(params bool?[] values) => values.Any(value => value == true);

    private static int? ParseDangerValue(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "low" => 1,
            "moderate" => 2,
            "considerable" => 3,
            "high" => 4,
            "extreme" => 5,
            _ => null,
        };

    private static int? ParseLikelihoodValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Split('-', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).LastOrDefault() ?? value;
        return ForecastText.NormalizeKey(text) switch
        {
            "unlikely" => 1,
            "possible" => 2,
            "likely" => 3,
            "verylikely" => 4,
            "certain" => 5,
            _ => null,
        };
    }

    private static decimal? ParseScaleValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var numericText = value.Split('-', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return decimal.TryParse(numericText, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
    }

    private static DateTimeOffset BuildValidUntil(string? day1Date, DateTimeOffset issuedAt)
    {
        if (!DateOnly.TryParseExact(day1Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            return issuedAt;
        }

        var localDateTime = new DateTime(parsedDate.Year, parsedDate.Month, parsedDate.Day, 23, 59, 0, DateTimeKind.Unspecified);
        return new DateTimeOffset(localDateTime, PacificTimeZone.GetUtcOffset(localDateTime));
    }

    private static DateTimeOffset? TryParseIssuedAt(string html)
    {
        var match = IssuedRegex().Match(html);
        if (!match.Success)
        {
            return null;
        }

        var issuedText = ForecastText.ToPlainText(match.Groups["value"].Value);
        var timezoneToken = issuedText.Contains(" PDT ", StringComparison.Ordinal) ? "PDT" :
            issuedText.Contains(" PST ", StringComparison.Ordinal) ? "PST" :
            null;
        if (timezoneToken is null)
        {
            return null;
        }

        var trimmed = issuedText.Replace($" {timezoneToken} ", " ", StringComparison.Ordinal);
        if (!DateTime.TryParseExact(
                trimmed,
                "h:mm tt dddd, MMMM d, yyyy",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return null;
        }

        var offset = timezoneToken == "PDT" ? TimeSpan.FromHours(-7) : TimeSpan.FromHours(-8);
        return new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), offset);
    }

    private static string ParseWeatherSummary(string html)
    {
        var rows = TableRowRegex().Matches(html)
            .Select(match => ParseTableRow(match.Value))
            .Where(row => row is not null)
            .Cast<TableRow>()
            .ToDictionary(row => row.Header, row => row.Values, StringComparer.OrdinalIgnoreCase);

        var parts = new List<string>();
        if (rows.TryGetValue("5000' Temperatures (Max / Min)", out var temperatures))
        {
            parts.Add($"5000 temps {FormatValues(temperatures)}");
        }

        if (rows.TryGetValue("Ridgeline Winds", out var winds))
        {
            parts.Add($"ridgeline winds {FormatValues(winds)}");
        }

        if (rows.TryGetValue("Weather Forecast", out var forecasts))
        {
            parts.Add($"forecast {FormatValues(forecasts)}");
        }

        if (rows.TryGetValue("Snow Level", out var snowLevels))
        {
            parts.Add($"snow level {FormatValues(snowLevels)}");
        }

        return string.Join(". ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string FormatValues(IReadOnlyList<string> values) =>
        string.Join("; ", values.Select(value => value.Trim().TrimEnd('.')));

    private static TableRow? ParseTableRow(string rowHtml)
    {
        var headerMatch = TableHeaderCellRegex().Match(rowHtml);
        if (!headerMatch.Success)
        {
            return null;
        }

        var header = ForecastText.ToPlainText(headerMatch.Groups["value"].Value);
        var values = TableValueCellRegex().Matches(rowHtml)
            .Select(match => ForecastText.ToPlainText(match.Groups["value"].Value))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        if (string.IsNullOrWhiteSpace(header) || values.Count == 0)
        {
            return null;
        }

        return new TableRow(header, values);
    }

    private async Task<ForecastRegion?> ResolveRegionWithCopilotAsync(
        string requestedLocation,
        IReadOnlyList<ForecastRegion> regions,
        CancellationToken cancellationToken)
    {
        if (regions.Count == 0)
        {
            return null;
        }

        var prompt = BuildLocationResolutionPrompt(requestedLocation, regions);
        var result = await processRunner.RunAsync(
            "copilot",
            [
                "-p",
                prompt,
                "--allow-all",
                "--silent",
                "--output-format",
                "text",
                "--no-color",
            ],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Copilot location lookup failed: {result.CombinedOutput}");
        }

        var response = result.StandardOutput
            .Replace(Environment.NewLine, " ")
            .Trim()
            .Trim('"', '\'', '.', ' ');

        if (string.Equals(response, "NONE", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var normalizedResponse = ForecastText.NormalizeKey(response);
        return regions.FirstOrDefault(region => ForecastText.NormalizeKey(region.DisplayName) == normalizedResponse);
    }

    private static string BuildLocationResolutionPrompt(string requestedLocation, IReadOnlyList<ForecastRegion> regions)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You map a user-provided mountain or location to the best Northwest Avalanche Center forecast region for today.");
        builder.AppendLine("Return exactly one line.");
        builder.AppendLine("Return only one exact candidate region name from the list below, or NONE.");
        builder.AppendLine("Do not explain your answer.");
        builder.AppendLine("Use general geographic knowledge to choose the best matching NWAC region for the requested location.");
        builder.AppendLine();
        builder.AppendLine($"Requested location: {requestedLocation}");
        builder.AppendLine("Candidate region names:");
        foreach (var region in regions)
        {
            builder.AppendLine($"- {region.DisplayName}");
        }

        return builder.ToString();
    }

    private static IEnumerable<string> GetAliases(ForecastRegion region) =>
        Regions.First(definition => string.Equals(definition.ReportId, region.ReportId, StringComparison.OrdinalIgnoreCase)).Aliases;

    private static string FirstNonEmpty(params string?[] values) =>
        values.Select(ForecastText.ToPlainText).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string BuildWarningText(string? warningValue, string? warningText)
    {
        var text = ForecastText.ToPlainText(warningText);
        if (!string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return string.Equals(warningValue, "none", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : warningValue ?? string.Empty;
    }

    [GeneratedRegex(@"<div class=""issued"">\s*Issued on\s+(?<value>.*?)\s*(?:by|</div>)", RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex IssuedRegex();

    [GeneratedRegex(@"<tr>(?<value>.*?)</tr>", RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex TableRowRegex();

    [GeneratedRegex(@"<th[^>]*>(?<value>.*?)</th>", RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex TableHeaderCellRegex();

    [GeneratedRegex(@"<td[^>]*>(?<value>.*?)</td>", RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex TableValueCellRegex();

    private sealed record NwacRegionDefinition(string ReportId, string DisplayName, string WeatherZone, IReadOnlyList<string> Aliases);

    private sealed record TableRow(string Header, IReadOnlyList<string> Values);

    private sealed class ForecastCollection
    {
        public ForecastMeta? Meta { get; init; }

        public IReadOnlyList<ForecastItem> Objects { get; init; } = [];
    }

    private sealed class ForecastMeta
    {
        public string? Next { get; init; }
    }

    private sealed class ForecastItem
    {
        [JsonPropertyName("bottom_line_summary")]
        public string? BottomLineSummary { get; init; }

        [JsonPropertyName("day1_danger_elev_high")]
        public string? Day1DangerElevHigh { get; init; }

        [JsonPropertyName("day1_danger_elev_low")]
        public string? Day1DangerElevLow { get; init; }

        [JsonPropertyName("day1_danger_elev_middle")]
        public string? Day1DangerElevMiddle { get; init; }

        [JsonPropertyName("day1_date")]
        public string? Day1Date { get; init; }

        [JsonPropertyName("day1_detailed_forecast")]
        public string? Day1DetailedForecast { get; init; }

        [JsonPropertyName("day1_warning")]
        public string? Day1Warning { get; init; }

        [JsonPropertyName("day1_warning_text")]
        public string? Day1WarningText { get; init; }

        [JsonPropertyName("optional_discussion")]
        public string? OptionalDiscussion { get; init; }

        public IReadOnlyList<ProblemItem>? Problems { get; init; }

        [JsonPropertyName("publish_date")]
        public DateTimeOffset PublishDate { get; init; }

        [JsonPropertyName("snowpack_discussion")]
        public string? SnowpackDiscussion { get; init; }

        [JsonPropertyName("special_statement")]
        public string? SpecialStatement { get; init; }

        public IReadOnlyList<ZoneItem>? Zones { get; init; }

        public string? PrimaryZoneName => Zones?.FirstOrDefault(zone => zone.Active)?.ZoneName ?? Zones?.FirstOrDefault()?.ZoneName;
    }

    private sealed class ProblemItem
    {
        public string? Likelihood { get; init; }

        [JsonPropertyName("maximum_size")]
        public string? MaximumSize { get; init; }

        [JsonPropertyName("minimum_size")]
        public string? MinimumSize { get; init; }

        [JsonPropertyName("octagon_high_east")]
        public bool? OctagonHighEast { get; init; }

        [JsonPropertyName("octagon_high_north")]
        public bool? OctagonHighNorth { get; init; }

        [JsonPropertyName("octagon_high_northeast")]
        public bool? OctagonHighNortheast { get; init; }

        [JsonPropertyName("octagon_high_northwest")]
        public bool? OctagonHighNorthwest { get; init; }

        [JsonPropertyName("octagon_high_south")]
        public bool? OctagonHighSouth { get; init; }

        [JsonPropertyName("octagon_high_southeast")]
        public bool? OctagonHighSoutheast { get; init; }

        [JsonPropertyName("octagon_high_southwest")]
        public bool? OctagonHighSouthwest { get; init; }

        [JsonPropertyName("octagon_high_west")]
        public bool? OctagonHighWest { get; init; }

        [JsonPropertyName("octagon_low_east")]
        public bool? OctagonLowEast { get; init; }

        [JsonPropertyName("octagon_low_north")]
        public bool? OctagonLowNorth { get; init; }

        [JsonPropertyName("octagon_low_northeast")]
        public bool? OctagonLowNortheast { get; init; }

        [JsonPropertyName("octagon_low_northwest")]
        public bool? OctagonLowNorthwest { get; init; }

        [JsonPropertyName("octagon_low_south")]
        public bool? OctagonLowSouth { get; init; }

        [JsonPropertyName("octagon_low_southeast")]
        public bool? OctagonLowSoutheast { get; init; }

        [JsonPropertyName("octagon_low_southwest")]
        public bool? OctagonLowSouthwest { get; init; }

        [JsonPropertyName("octagon_low_west")]
        public bool? OctagonLowWest { get; init; }

        [JsonPropertyName("octagon_mid_east")]
        public bool? OctagonMidEast { get; init; }

        [JsonPropertyName("octagon_mid_north")]
        public bool? OctagonMidNorth { get; init; }

        [JsonPropertyName("octagon_mid_northeast")]
        public bool? OctagonMidNortheast { get; init; }

        [JsonPropertyName("octagon_mid_northwest")]
        public bool? OctagonMidNorthwest { get; init; }

        [JsonPropertyName("octagon_mid_south")]
        public bool? OctagonMidSouth { get; init; }

        [JsonPropertyName("octagon_mid_southeast")]
        public bool? OctagonMidSoutheast { get; init; }

        [JsonPropertyName("octagon_mid_southwest")]
        public bool? OctagonMidSouthwest { get; init; }

        [JsonPropertyName("octagon_mid_west")]
        public bool? OctagonMidWest { get; init; }

        public int Order { get; init; }

        [JsonPropertyName("problem_description")]
        public string? ProblemDescription { get; init; }

        [JsonPropertyName("problem_type")]
        public ProblemTypeItem? ProblemType { get; init; }
    }

    private sealed class ProblemTypeItem
    {
        public string? Name { get; init; }

        [JsonPropertyName("risk_management_description")]
        public string? RiskManagementDescription { get; init; }
    }

    private sealed class ZoneItem
    {
        public bool Active { get; init; }

        public string? Slug { get; init; }

        [JsonPropertyName("zone_abbrev")]
        public string? ZoneAbbrev { get; init; }

        [JsonPropertyName("zone_name")]
        public string? ZoneName { get; init; }
    }
}
