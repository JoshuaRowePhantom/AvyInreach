namespace AvyInReach.Tests;

public sealed class CopilotCliSummarizerTests
{
    private static readonly SummaryGenerationOptions DefaultOptions =
        new("user@inreach.garmin.com", RecipientTransport.InReach, 480);

    [Fact]
    public async Task GenerateSummaryAsync_includes_likelihood_in_problem_prompt()
    {
        var runner = new CapturingProcessRunner("valid to 3/13 16:00PDT 2/3/3 Wind slab O/O/O 4 1-2 ALL. WX: light snow.");
        var summarizer = new CopilotCliSummarizer(runner);

        _ = await summarizer.GenerateSummaryAsync(BuildForecast(), DefaultOptions, CancellationToken.None);

        Assert.Contains(
            "Format each problem exactly as: <name> <O/X for below/treeline/alpine> <likelihood 1-5> <size range> <aspect set>.",
            runner.Prompt);
        Assert.Contains(
            "Use abbreviated absolute day names like Fri or Sat. Do not use relative day words like today, tonight, tomorrow, or yesterday.",
            runner.Prompt);
        Assert.Contains(
            "Weather must always include sun/cloud, wind, and low/high temperature in terse form.",
            runner.Prompt);
        Assert.Contains(
            "If snowfall is approximate or qualified in the source, preserve that tersely with markers like '~' or 'up to'.",
            runner.Prompt);
        Assert.Contains(
            "Use the notice only for decision-driving statements that matter for travel choices, prioritizing recent avalanche activity, unusually serious hazards, and notable weak-layer concerns from any forecast section.",
            runner.Prompt);
        Assert.Contains(
            "If a current summary is provided below and it still accurately presents the same forecast information within the required structure and budget, return that exact current summary byte-for-byte.",
            runner.Prompt);
        Assert.Contains(
            "Character budget: 480",
            runner.Prompt);
        Assert.Contains(
            "Problem 1: Wind slab; elevations O/O/O; likelihood 4; size 1-2; aspects ALL; notes notes",
            runner.Prompt);
    }

    [Fact]
    public async Task GenerateSummaryAsync_prefixes_validity_when_copilot_omits_it()
    {
        var summarizer = new CopilotCliSummarizer(new FakeProcessRunner("2/3/3 Wind slab O/O/O 1-2 ALL. WX: light snow."));

        var summary = await summarizer.GenerateSummaryAsync(BuildForecast(), DefaultOptions, CancellationToken.None);

        Assert.StartsWith("valid to 3/13 16:00PDT ", summary);
        Assert.Contains("2/3/3", summary);
    }

    [Fact]
    public async Task GenerateSummaryAsync_moves_validity_to_the_front_when_copilot_puts_it_later()
    {
        var summarizer = new CopilotCliSummarizer(new FakeProcessRunner("2/3/3 Wind slab O/O/O 1-2 ALL. valid to 3/13 16:00PDT WX: light snow."));

        var summary = await summarizer.GenerateSummaryAsync(BuildForecast(), DefaultOptions, CancellationToken.None);

        Assert.StartsWith("valid to 3/13 16:00PDT ", summary);
        Assert.DoesNotContain(". valid to 3/13 16:00PDT", summary);
    }

    [Fact]
    public async Task GenerateSummaryAsync_throws_when_output_exceeds_budget()
    {
        var summarizer = new CopilotCliSummarizer(new FakeProcessRunner(new string('x', 490)));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            summarizer.GenerateSummaryAsync(BuildForecast(), DefaultOptions, CancellationToken.None));

        Assert.Contains("configured summary budget of 480", ex.Message);
    }

    [Fact]
    public async Task GenerateSummaryAsync_includes_current_summary_when_provided()
    {
        var runner = new CapturingProcessRunner("valid to 3/13 16:00PDT 2/3/3 Wind slab O/O/O 4 1-2 ALL. WX: Fri sun, W 20, TL -9C.");
        var summarizer = new CopilotCliSummarizer(runner);
        var options = DefaultOptions with { CurrentSummary = "valid to 3/13 16:00PDT 2/3/3 existing summary" };

        _ = await summarizer.GenerateSummaryAsync(BuildForecast(), options, CancellationToken.None);

        Assert.Contains("Current summary: valid to 3/13 16:00PDT 2/3/3 existing summary", runner.Prompt);
    }

    private static AvalancheForecast BuildForecast() =>
        new(
            new ForecastRegion("avalanche-canada", "Glacier", "report-1", "area-1", "https://example.com"),
            "https://example.com",
            "Glacier",
            "Avalanche Canada",
            new DateTimeOffset(2026, 3, 13, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 13, 23, 0, 0, TimeSpan.Zero),
            "America/Vancouver",
            new DangerRatings(2, 3, 3),
            [new ForecastProblem("Wind slab", true, true, true, 4, 1, 2, ["n", "ne", "e", "se", "s", "sw", "w", "nw"], "notes")],
            "highlights",
            "avalanche summary",
            "snowpack summary",
            "weather summary",
            "message");

    private sealed class FakeProcessRunner(string output) : IProcessRunner
    {
        public Task<ProcessRunResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ProcessRunResult(0, output, string.Empty));
        }
    }

    private sealed class CapturingProcessRunner(string output) : IProcessRunner
    {
        public string Prompt { get; private set; } = string.Empty;

        public Task<ProcessRunResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            CancellationToken cancellationToken)
        {
            Prompt = arguments.Skip(1).First();
            return Task.FromResult(new ProcessRunResult(0, output, string.Empty));
        }
    }
}
