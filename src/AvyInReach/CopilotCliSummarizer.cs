using System.Text;

namespace AvyInReach;

internal interface IForecastSummarizer
{
    Task<string> GenerateSummaryAsync(
        AvalancheForecast forecast,
        SummaryGenerationOptions options,
        CancellationToken cancellationToken);
}

internal sealed class CopilotCliSummarizer(ICopilotCliRunner copilotRunner) : IForecastSummarizer
{
    public async Task<string> GenerateSummaryAsync(
        AvalancheForecast forecast,
        SummaryGenerationOptions options,
        CancellationToken cancellationToken)
    {
        var validUntil = ValidityText.Format(forecast.ValidUntil, forecast.TimezoneId);
        var prompt = BuildPrompt(forecast, validUntil, options);

        var result = await copilotRunner.RunPromptAsync(prompt, cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Copilot summarization failed: {result.CombinedOutput}");
        }

        var summary = result.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new InvalidOperationException("Copilot returned an empty summary.");
        }

        summary = summary.Replace(Environment.NewLine, " ").Trim();
        var validPrefix = $"valid to {validUntil}";
        if (summary.StartsWith(validPrefix, StringComparison.Ordinal))
        {
            return ValidateSummaryLength(summary, options);
        }

        if (summary.Contains(validPrefix, StringComparison.Ordinal))
        {
            summary = $"{validPrefix} {summary.Replace(validPrefix, string.Empty, StringComparison.Ordinal).Trim()}";
        }
        else
        {
            summary = $"{validPrefix} {summary.Trim()}";
        }

        return ValidateSummaryLength(summary.Replace(Environment.NewLine, " ").Trim(), options);
    }

    private static string BuildPrompt(
        AvalancheForecast forecast,
        string validUntil,
        SummaryGenerationOptions options)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You format avalanche forecasts for a Garmin InReach message.");
        builder.AppendLine("Return exactly one ASCII line. No bullets. No markdown. No quotes.");
        builder.AppendLine($"Keep it within {options.SummaryCharacterBudget} ASCII characters.");
        builder.AppendLine("Output must be deterministic: the same input must produce the same wording and field order.");
        builder.AppendLine("Use this exact structure:");
        builder.AppendLine($"valid to {validUntil} <danger> <problem 1>. <problem 2>. <brief notice>. WX: <weather>");
        builder.AppendLine("Rules:");
        builder.AppendLine("- Start with the exact text 'valid to " + validUntil + "'.");
        builder.AppendLine("- Immediately after that, put danger as below/treeline/alpine.");
        builder.AppendLine("- Include up to two avalanche problems, in the same order provided below.");
        builder.AppendLine("- Format each problem exactly as: <name> <O/X for below/treeline/alpine> <likelihood 1-5> <size range> <aspect set>.");
        builder.AppendLine("- Use the problem names, presence values, likelihood values, size ranges, and aspect sets exactly as provided below. Do not paraphrase them.");
        builder.AppendLine("- Use ALL only when the provided aspect set is ALL.");
        builder.AppendLine("- If a current summary is provided below and it still accurately presents the same forecast information within the required structure and budget, return that exact current summary byte-for-byte.");
        builder.AppendLine("- Use the notice only for decision-driving statements that matter for travel choices, prioritizing recent avalanche activity, unusually serious hazards, and notable weak-layer concerns from any forecast section.");
        builder.AppendLine("- If there is no useful decision-driving notice that fits, omit the notice phrase entirely.");
        builder.AppendLine("- Weather must appear once at the end and must begin with 'WX: '. Weather must always include sun/cloud, wind, and low/high temperature in terse form.");
        builder.AppendLine("- In WX, prioritize the main ski day covered by the bulletin. If the bulletin is issued later in the day, treat the next daytime period as primary.");
        builder.AppendLine("- Also include the issue evening or overnight period when the source provides it and it materially helps explain what you will encounter while skiing; compress it tersely rather than dropping the main ski day.");
        builder.AppendLine("- After the main ski-day forecast, prefer previous 24-hour snow or recent storm snow information from the source material before secondary forecast periods.");
        builder.AppendLine("- Prefer the most informative terse weather wording that still fits, including a second day only when it adds useful change context after the primary ski-day forecast and recent snow context are covered.");
        builder.AppendLine("- If snowfall is approximate or qualified in the source, preserve that tersely with markers like '~' or 'up to'.");
        builder.AppendLine("- Prioritize fitting the required structure, the listed problems, any decision-driving notice, and required weather fields within the configured character budget.");
        builder.AppendLine("- Use abbreviated absolute day names like Fri or Sat. Do not use relative day words like today, tonight, tomorrow, or yesterday.");
        builder.AppendLine("- Do not include the zone name, provider name, URLs, explanations, or any extra text.");
        builder.AppendLine("- Do not infer, normalize, or reorder facts beyond the explicit formatting rules above.");
        builder.AppendLine($"Recipient: {options.RecipientAddress}");
        builder.AppendLine($"Transport: {options.Transport.ToConfigValue()}");
        builder.AppendLine($"Character budget: {options.SummaryCharacterBudget}");
        if (!string.IsNullOrWhiteSpace(options.CurrentSummary))
        {
            builder.AppendLine($"Current summary: {options.CurrentSummary}");
        }
        builder.AppendLine();
        builder.AppendLine($"Zone: {forecast.Region.DisplayName}");
        builder.AppendLine($"Danger: {forecast.CurrentDangerRatings.ToCompactString()}");
        builder.AppendLine($"Valid until: {validUntil}");
        builder.AppendLine($"Highlights: {forecast.Highlights}");
        builder.AppendLine($"Message: {forecast.Message}");
        builder.AppendLine($"Avalanche summary: {forecast.AvalancheSummary}");
        builder.AppendLine($"Snowpack summary: {forecast.SnowpackSummary}");
        builder.AppendLine($"Weather summary: {forecast.WeatherSummary}");

        for (var index = 0; index < forecast.Problems.Count; index++)
        {
            var problem = forecast.Problems[index];
            builder.AppendLine(
                $"Problem {index + 1}: {problem.Name}; elevations {problem.PresenceString()}; likelihood {problem.LikelihoodString()}; size {problem.SizeRangeString()}; aspects {problem.AspectString()}; notes {problem.Comment}");
        }

        builder.AppendLine($"Forecast URL: {forecast.ForecastUrl}");
        return builder.ToString();
    }

    private static string ValidateSummaryLength(string summary, SummaryGenerationOptions options)
    {
        if (summary.Length > options.SummaryCharacterBudget)
        {
            throw new InvalidOperationException(
                $"Copilot returned {summary.Length} characters for '{options.RecipientAddress}', exceeding the configured summary budget of {options.SummaryCharacterBudget}.");
        }

        return summary;
    }
}

internal sealed record SummaryGenerationOptions(
    string RecipientAddress,
    RecipientTransport Transport,
    int SummaryCharacterBudget,
    string? CurrentSummary = null);
