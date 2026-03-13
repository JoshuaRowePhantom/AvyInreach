using System.Text.Json;
using System.Text.RegularExpressions;

namespace AvyInReach;

internal sealed partial class AvalancheCanadaProvider(HttpClient httpClient) : IAvalancheProvider
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
        var normalizedInput = Normalize(regionName);

        return regions.FirstOrDefault(region =>
            Normalize(region.DisplayName) == normalizedInput ||
            Normalize(region.ReportId) == normalizedInput ||
            Normalize(region.AreaId) == normalizedInput);
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
                ParseDecimal(problem.Data?.ExpectedSize?.Min),
                ParseDecimal(problem.Data?.ExpectedSize?.Max),
                problem.Data?.Aspects?.Select(item => item.Value ?? item.Display ?? string.Empty)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .ToList() ?? [],
                ToPlainText(problem.Comment)))
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
            ToPlainText(product.Report.Highlights),
            GetSummary(product.Report.Summaries, "avalanche-summary"),
            GetSummary(product.Report.Summaries, "snowpack-summary"),
            GetSummary(product.Report.Summaries, "weather-summary"),
            ToPlainText(product.Report.Message));
    }

    private static string GetSummary(IReadOnlyList<SummaryItem>? summaries, string typeValue) =>
        ToPlainText(
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

    private static string ToPlainText(string? html)
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

    private static string Normalize(string value) =>
        new string(value.Where(ch => char.IsLetterOrDigit(ch)).ToArray()).ToLowerInvariant();

    [GeneratedRegex("<.*?>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();

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
