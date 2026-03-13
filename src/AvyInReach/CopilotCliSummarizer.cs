using System.Text;

namespace AvyInReach;

internal interface IForecastSummarizer
{
    Task<string> GenerateSummaryAsync(AvalancheForecast forecast, CancellationToken cancellationToken);
}

internal sealed class CopilotCliSummarizer(IProcessRunner processRunner) : IForecastSummarizer
{
    public async Task<string> GenerateSummaryAsync(AvalancheForecast forecast, CancellationToken cancellationToken)
    {
        var validUntil = ValidityText.Format(forecast.ValidUntil, forecast.TimezoneId);
        var prompt = BuildPrompt(forecast, validUntil);

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
            throw new InvalidOperationException(
                $"Copilot summarization failed: {result.CombinedOutput}");
        }

        var summary = result.StandardOutput.Trim();
        if (string.IsNullOrWhiteSpace(summary))
        {
            throw new InvalidOperationException("Copilot returned an empty summary.");
        }

        var validPrefix = $"valid to {validUntil}";
        if (summary.StartsWith(validPrefix, StringComparison.Ordinal))
        {
            return summary.Replace(Environment.NewLine, " ").Trim();
        }

        if (summary.Contains(validPrefix, StringComparison.Ordinal))
        {
            summary = $"{validPrefix} {summary.Replace(validPrefix, string.Empty, StringComparison.Ordinal).Trim()}";
        }
        else
        {
            summary = $"{validPrefix} {summary.Trim()}";
        }

        return summary.Replace(Environment.NewLine, " ").Trim();
    }

    private static string BuildPrompt(AvalancheForecast forecast, string validUntil)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You format avalanche forecasts for a Garmin InReach message.");
        builder.AppendLine("Return exactly one ASCII line. No bullets. No markdown. No quotes.");
        builder.AppendLine("Keep it under 320 characters if possible.");
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
        builder.AppendLine("- If there is no useful notice, omit the notice phrase entirely.");
        builder.AppendLine("- Weather must appear once at the end and must begin with 'WX: '. Keep it terse.");
        builder.AppendLine("- Use abbreviated absolute day names like Fri or Sat. Do not use relative day words like today, tonight, tomorrow, or yesterday.");
        builder.AppendLine("- Do not include the zone name, provider name, URLs, explanations, or any extra text.");
        builder.AppendLine("- Do not infer, normalize, or reorder facts beyond the explicit formatting rules above.");
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
}
