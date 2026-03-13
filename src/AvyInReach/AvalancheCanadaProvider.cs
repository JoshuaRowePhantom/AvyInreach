using System.Text;
using System.Text.Json;

namespace AvyInReach;

internal sealed partial class AvalancheCanadaProvider(HttpClient httpClient, IProcessRunner processRunner) : IAvalancheProvider
{
    private const string MetadataUrl = "https://api.avalanche.ca/forecasts/en/metadata";
    private const string ProductsUrl = "https://api.avalanche.ca/forecasts/en/products";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public string Id => "avalanche-canada";

    public IReadOnlyList<string> Aliases => ["avalanche-canada", "avalanchecanada", "ac"];

    public async Task<IReadOnlyList<ForecastRegion>> GetRegionsAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(MetadataUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<List<MetadataItem>>(stream, SerializerOptions, cancellationToken)
            ?? [];

        return payload
            .Where(item => item.Product?.Type == "avalancheforecast")
            .Select(item => new ForecastRegion(
                Id,
                item.Product!.Title ?? item.Area?.Name ?? item.Product.ReportId ?? "(unnamed region)",
                item.Product.ReportId ?? item.Product.Id ?? throw new InvalidOperationException("Missing report id."),
                item.Area?.Id ?? string.Empty,
                item.Url ?? $"https://avalanche.ca/en/forecasts/{item.Product.Slug}"))
            .GroupBy(item => item.ReportId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public async Task<ForecastRegion?> ResolveRegionAsync(string regionName, CancellationToken cancellationToken)
    {
        var regions = await GetRegionsAsync(cancellationToken);
        var normalizedInput = ForecastText.NormalizeKey(regionName);

        var directMatch = regions.FirstOrDefault(region =>
            ForecastText.NormalizeKey(region.DisplayName) == normalizedInput ||
            ForecastText.NormalizeKey(region.ReportId) == normalizedInput ||
            ForecastText.NormalizeKey(region.AreaId) == normalizedInput);
        if (directMatch is not null)
        {
            return directMatch;
        }

        var containsMatches = regions.Where(region =>
                ForecastText.NormalizeKey(region.DisplayName).Contains(normalizedInput, StringComparison.Ordinal) ||
                normalizedInput.Contains(ForecastText.NormalizeKey(region.DisplayName), StringComparison.Ordinal))
            .ToList();
        if (containsMatches.Count == 1)
        {
            return containsMatches[0];
        }

        return await ResolveRegionWithCopilotAsync(regionName, regions, cancellationToken);
    }

    public async Task<AvalancheForecast?> GetForecastAsync(ForecastRegion region, CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync(ProductsUrl, cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<List<ProductItem>>(stream, SerializerOptions, cancellationToken)
            ?? [];

        var product = payload.FirstOrDefault(item =>
            string.Equals(item.Report?.Id, region.ReportId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.Report?.Title, region.DisplayName, StringComparison.OrdinalIgnoreCase));

        if (product is null || product.Report is null)
        {
            return null;
        }

        var rating = product.Report.DangerRatings?.FirstOrDefault()?.Ratings;
        var problems = product.Report.Problems?
            .Take(2)
            .Select(problem => new ForecastProblem(
                problem.Type?.Display ?? problem.Type?.Value ?? "Problem",
                problem.Data?.Elevations?.Any(item => item.Value == "btl") == true,
                problem.Data?.Elevations?.Any(item => item.Value == "tln") == true,
                problem.Data?.Elevations?.Any(item => item.Value == "alp") == true,
                ParseLikelihoodValue(problem.Data?.Likelihood?.Value ?? problem.Data?.Likelihood?.Display),
                ParseDecimal(problem.Data?.ExpectedSize?.Min),
                ParseDecimal(problem.Data?.ExpectedSize?.Max),
                problem.Data?.Aspects?.Select(item => item.Value ?? item.Display ?? string.Empty)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList() ?? [],
                ForecastText.ToPlainText(problem.Comment)))
            .ToList()
            ?? [];

        return new AvalancheForecast(
            region,
            product.Url ?? region.ForecastUrl,
            product.Report.Title ?? region.DisplayName,
            product.Owner?.Display ?? product.Report.Forecaster ?? "Avalanche Canada",
            product.Report.DateIssued ?? DateTimeOffset.MinValue,
            product.Report.ValidUntil ?? DateTimeOffset.MinValue,
            product.Report.Timezone ?? "America/Vancouver",
            new DangerRatings(
                ParseDangerValue(rating?.Btl?.Rating?.Display),
                ParseDangerValue(rating?.Tln?.Rating?.Display),
                ParseDangerValue(rating?.Alp?.Rating?.Display)),
            problems,
            ForecastText.ToPlainText(product.Report.Highlights),
            GetSummary(product.Report.Summaries, "avalanche-summary"),
            GetSummary(product.Report.Summaries, "snowpack-summary"),
            GetSummary(product.Report.Summaries, "weather-summary"),
            ForecastText.ToPlainText(product.Report.Message));
    }

    private static string GetSummary(IReadOnlyList<SummaryItem>? summaries, string typeValue) =>
        ForecastText.ToPlainText(
            summaries?.FirstOrDefault(summary =>
                string.Equals(summary.Type?.Value, typeValue, StringComparison.OrdinalIgnoreCase))?.Content);

    private static int? ParseDangerValue(string? display)
    {
        if (string.IsNullOrWhiteSpace(display))
        {
            return null;
        }

        return int.TryParse(display.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), out var value)
            ? value
            : null;
    }

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value, out var result) ? result : null;

    private static int? ParseLikelihoodValue(string? value) =>
        ForecastText.NormalizeKey(value ?? string.Empty) switch
        {
            "unlikely" => 1,
            "possible" => 2,
            "likely" => 3,
            "verylikely" => 4,
            "certain" => 5,
            _ => null,
        };

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
        builder.AppendLine("You map a user-provided mountain or location to the best Avalanche Canada forecast region for today.");
        builder.AppendLine("Return exactly one line.");
        builder.AppendLine("Return only one exact candidate region name from the list below, or NONE.");
        builder.AppendLine("Do not explain your answer.");
        builder.AppendLine("Use general geographic knowledge to choose the best matching Avalanche Canada region for the requested location.");
        builder.AppendLine();
        builder.AppendLine($"Requested location: {requestedLocation}");
        builder.AppendLine("Candidate region names:");
        foreach (var region in regions)
        {
            builder.AppendLine($"- {region.DisplayName}");
        }

        return builder.ToString();
    }

    private sealed class MetadataItem
    {
        public MetadataProduct? Product { get; init; }

        public MetadataArea? Area { get; init; }

        public string? Url { get; init; }
    }

    private sealed class MetadataProduct
    {
        public string? Slug { get; init; }

        public string? Type { get; init; }

        public string? Title { get; init; }

        public string? Id { get; init; }

        public string? ReportId { get; init; }
    }

    private sealed class MetadataArea
    {
        public string? Id { get; init; }

        public string? Name { get; init; }
    }

    private sealed class ProductItem
    {
        public string? Url { get; init; }

        public ProductReport? Report { get; init; }

        public ProductOwner? Owner { get; init; }
    }

    private sealed class ProductOwner
    {
        public string? Display { get; init; }
    }

    private sealed class ProductReport
    {
        public string? Id { get; init; }

        public string? Title { get; init; }

        public string? Forecaster { get; init; }

        public DateTimeOffset? DateIssued { get; init; }

        public DateTimeOffset? ValidUntil { get; init; }

        public string? Timezone { get; init; }

        public string? Highlights { get; init; }

        public IReadOnlyList<SummaryItem>? Summaries { get; init; }

        public IReadOnlyList<DangerRatingItem>? DangerRatings { get; init; }

        public IReadOnlyList<ProblemItem>? Problems { get; init; }

        public string? Message { get; init; }
    }

    private sealed class SummaryItem
    {
        public SummaryType? Type { get; init; }

        public string? Content { get; init; }
    }

    private sealed class SummaryType
    {
        public string? Value { get; init; }

        public string? Display { get; init; }
    }

    private sealed class DangerRatingItem
    {
        public DangerRatingSet? Ratings { get; init; }
    }

    private sealed class DangerRatingSet
    {
        public DangerRatingLevel? Alp { get; init; }

        public DangerRatingLevel? Tln { get; init; }

        public DangerRatingLevel? Btl { get; init; }
    }

    private sealed class DangerRatingLevel
    {
        public DisplayValue? Rating { get; init; }
    }

    private sealed class ProblemItem
    {
        public DisplayValue? Type { get; init; }

        public string? Comment { get; init; }

        public ProblemData? Data { get; init; }
    }

    private sealed class ProblemData
    {
        public IReadOnlyList<DisplayValue>? Elevations { get; init; }

        public IReadOnlyList<DisplayValue>? Aspects { get; init; }

        public DisplayValue? Likelihood { get; init; }

        public ProblemExpectedSize? ExpectedSize { get; init; }
    }

    private sealed class ProblemExpectedSize
    {
        public string? Min { get; init; }

        public string? Max { get; init; }
    }

    private sealed class DisplayValue
    {
        public string? Value { get; init; }

        public string? Display { get; init; }
    }
}
