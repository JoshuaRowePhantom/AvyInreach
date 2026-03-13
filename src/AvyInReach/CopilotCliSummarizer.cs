using System.Text;

namespace AvyInReach;

internal interface IForecastSummarizer
{
    Task<string> GenerateSummaryAsync(AvalancheForecast forecast, CancellationToken cancellationToken);
}

internal sealed class CopilotCliSummarizer(ProcessRunner processRunner) : IForecastSummarizer
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

        if (!summary.Contains($"valid to {validUntil}", StringComparison.Ordinal))
        {
            summary = $"{summary.TrimEnd()} valid to {validUntil}";
        }

        return summary.Replace(Environment.NewLine, " ").Trim();
    }

    private static string BuildPrompt(AvalancheForecast forecast, string validUntil)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You format avalanche forecasts for a Garmin InReach message.");
        builder.AppendLine("Return exactly one ASCII line. No bullets. No markdown. No quotes.");
        builder.AppendLine("Keep it under 320 characters if possible.");
        builder.AppendLine($"Include the exact text: valid to {validUntil}");
        builder.AppendLine("Start with danger ratings as below/treeline/alpine.");
        builder.AppendLine("Then summarize up to two avalanche problems as:");
        builder.AppendLine("<name> <O/X for below/treeline/alpine> <size range> <aspect set>.");
        builder.AppendLine("Use ALL when all aspects apply.");
        builder.AppendLine("Keep weather terse.");
        builder.AppendLine("If there is a special notice or highlight, include only a very brief phrase.");
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
                $"Problem {index + 1}: {problem.Name}; elevations {problem.PresenceString()}; size {problem.SizeRangeString()}; aspects {problem.AspectString()}; notes {problem.Comment}");
        }

        builder.AppendLine($"Forecast URL: {forecast.ForecastUrl}");
        return builder.ToString();
    }
}
