namespace AvyInReach.Tests;

public sealed class ForecastUpdateServiceTests
{
    [Fact]
    public async Task Update_skips_summarization_when_forecast_inputs_are_unchanged()
    {
        var paths = new AppPathsForTests();
        var stateStore = new DeliveryStateStore(paths);
        var deliveryConfigurationStore = new DeliveryConfigurationStore(paths);
        var provider = new SequentialForecastProvider([BuildForecast(), BuildForecast()]);
        var summarizer = new FakeSummarizer(["first summary", "second summary"]);
        var emailSender = new FakeEmailSender();
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 13, 18, 0, 0, TimeSpan.Zero));
        var service = new ForecastUpdateService(
            new ProviderRegistry([provider]),
            summarizer,
            emailSender,
            deliveryConfigurationStore,
            stateStore,
            clock,
            new ConsoleLog());

        await service.ProcessAsync(DeliveryMode.Update, "user@inreach.garmin.com", "avalanche-canada", "Glacier", CancellationToken.None);
        await service.ProcessAsync(DeliveryMode.Update, "user@inreach.garmin.com", "avalanche-canada", "Glacier", CancellationToken.None);

        Assert.Single(emailSender.SentBodies);
        Assert.Equal("first summary", emailSender.SentBodies[0]);
        Assert.Equal(1, summarizer.CallCount);
    }

    [Fact]
    public async Task Update_deduplicates_when_requested_location_resolves_to_same_forecast_region()
    {
        var paths = new AppPathsForTests();
        var stateStore = new DeliveryStateStore(paths);
        var deliveryConfigurationStore = new DeliveryConfigurationStore(paths);
        var provider = new SequentialForecastProvider([BuildForecast(), BuildForecast()]);
        var summarizer = new FakeSummarizer(["first summary", "second summary"]);
        var emailSender = new FakeEmailSender();
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 13, 18, 0, 0, TimeSpan.Zero));
        var service = new ForecastUpdateService(
            new ProviderRegistry([provider]),
            summarizer,
            emailSender,
            deliveryConfigurationStore,
            stateStore,
            clock,
            new ConsoleLog());

        await service.ProcessAsync(DeliveryMode.Update, "user@inreach.garmin.com", "avalanche-canada", "Glacier", CancellationToken.None);
        await service.ProcessAsync(DeliveryMode.Update, "user@inreach.garmin.com", "avalanche-canada", "Valhalla", CancellationToken.None);

        Assert.Single(emailSender.SentBodies);
        Assert.Equal("first summary", emailSender.SentBodies[0]);
        Assert.Equal(1, summarizer.CallCount);
    }

    [Fact]
    public async Task Update_does_not_send_when_forecast_changes_but_summary_matches()
    {
        var paths = new AppPathsForTests();
        var stateStore = new DeliveryStateStore(paths);
        var deliveryConfigurationStore = new DeliveryConfigurationStore(paths);
        var provider = new SequentialForecastProvider(
            [
                BuildForecast(),
                BuildForecast(weatherSummary: "updated weather summary"),
            ]);
        var summarizer = new FakeSummarizer(["same summary", "same summary"]);
        var emailSender = new FakeEmailSender();
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 13, 18, 0, 0, TimeSpan.Zero));
        var service = new ForecastUpdateService(
            new ProviderRegistry([provider]),
            summarizer,
            emailSender,
            deliveryConfigurationStore,
            stateStore,
            clock,
            new ConsoleLog());

        await service.ProcessAsync(DeliveryMode.Update, "user@inreach.garmin.com", "avalanche-canada", "Glacier", CancellationToken.None);
        await service.ProcessAsync(DeliveryMode.Update, "user@inreach.garmin.com", "avalanche-canada", "Glacier", CancellationToken.None);

        Assert.Equal(2, summarizer.CallCount);
        Assert.Single(emailSender.SentBodies);
        Assert.Equal("same summary", emailSender.SentBodies[0]);
    }

    [Fact]
    public async Task Update_sends_when_forecast_and_summary_change()
    {
        var paths = new AppPathsForTests();
        var stateStore = new DeliveryStateStore(paths);
        var deliveryConfigurationStore = new DeliveryConfigurationStore(paths);
        var provider = new SequentialForecastProvider(
            [
                BuildForecast(),
                BuildForecast(weatherSummary: "updated weather summary"),
            ]);
        var summarizer = new FakeSummarizer(["first summary", "second summary"]);
        var emailSender = new FakeEmailSender();
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 13, 18, 0, 0, TimeSpan.Zero));
        var service = new ForecastUpdateService(
            new ProviderRegistry([provider]),
            summarizer,
            emailSender,
            deliveryConfigurationStore,
            stateStore,
            clock,
            new ConsoleLog());

        await service.ProcessAsync(DeliveryMode.Update, "user@inreach.garmin.com", "avalanche-canada", "Glacier", CancellationToken.None);
        await service.ProcessAsync(DeliveryMode.Update, "user@inreach.garmin.com", "avalanche-canada", "Glacier", CancellationToken.None);

        Assert.Equal(2, summarizer.CallCount);
        Assert.Equal(2, emailSender.SentBodies.Count);
        Assert.Equal(["first summary", "second summary"], emailSender.SentBodies);
    }

    [Fact]
    public async Task Missing_forecast_notice_sends_after_one_hour()
    {
        var paths = new AppPathsForTests();
        var stateStore = new DeliveryStateStore(paths);
        var deliveryConfigurationStore = new DeliveryConfigurationStore(paths);
        var provider = new MissingForecastProvider();
        var emailSender = new FakeEmailSender();
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 13, 18, 0, 0, TimeSpan.Zero));
        var service = new ForecastUpdateService(
            new ProviderRegistry([provider]),
            new FakeSummarizer([]),
            emailSender,
            deliveryConfigurationStore,
            stateStore,
            clock,
            new ConsoleLog());

        await service.ProcessAsync(DeliveryMode.Update, "user@inreach.garmin.com", "avalanche-canada", "Glacier", CancellationToken.None);
        clock.UtcNowValue = clock.UtcNowValue.AddHours(1).AddMinutes(1);
        await service.ProcessAsync(DeliveryMode.Update, "user@inreach.garmin.com", "avalanche-canada", "Glacier", CancellationToken.None);

        Assert.Single(emailSender.SentBodies);
        Assert.Contains("still retrying", emailSender.SentBodies[0]);
    }

    [Fact]
    public async Task Update_stops_sending_after_daily_report_limit_is_reached()
    {
        var paths = new AppPathsForTests();
        var stateStore = new DeliveryStateStore(paths);
        var deliveryConfigurationStore = new DeliveryConfigurationStore(paths);
        await deliveryConfigurationStore.ConfigureAsync(4, CancellationToken.None);
        var provider = new SequentialForecastProvider(
            [
                BuildForecast(weatherSummary: "weather 1"),
                BuildForecast(weatherSummary: "weather 2"),
                BuildForecast(weatherSummary: "weather 3"),
                BuildForecast(weatherSummary: "weather 4"),
                BuildForecast(weatherSummary: "weather 5"),
            ]);
        var summarizer = new FakeSummarizer(["summary 1", "summary 2", "summary 3", "summary 4", "summary 5"]);
        var emailSender = new FakeEmailSender();
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 13, 18, 0, 0, TimeSpan.Zero));
        var service = new ForecastUpdateService(
            new ProviderRegistry([provider]),
            summarizer,
            emailSender,
            deliveryConfigurationStore,
            stateStore,
            clock,
            new ConsoleLog());

        await service.ProcessAsync(DeliveryMode.Update, "user@inreach.garmin.com", "avalanche-canada", "Glacier", CancellationToken.None);
        clock.UtcNowValue = clock.UtcNowValue.AddHours(1);
        await service.ProcessAsync(DeliveryMode.Update, "user@inreach.garmin.com", "avalanche-canada", "Glacier", CancellationToken.None);
        clock.UtcNowValue = clock.UtcNowValue.AddHours(1);
        await service.ProcessAsync(DeliveryMode.Update, "user@inreach.garmin.com", "avalanche-canada", "Glacier", CancellationToken.None);
        clock.UtcNowValue = clock.UtcNowValue.AddHours(1);
        await service.ProcessAsync(DeliveryMode.Update, "user@inreach.garmin.com", "avalanche-canada", "Glacier", CancellationToken.None);
        clock.UtcNowValue = clock.UtcNowValue.AddHours(1);
        await service.ProcessAsync(DeliveryMode.Update, "user@inreach.garmin.com", "avalanche-canada", "Glacier", CancellationToken.None);

        Assert.Equal(["summary 1", "summary 2", "summary 3", "summary 4"], emailSender.SentBodies);
    }

    [Fact]
    public async Task Update_allows_sending_again_after_24_hour_window_expires()
    {
        var paths = new AppPathsForTests();
        var stateStore = new DeliveryStateStore(paths);
        var deliveryConfigurationStore = new DeliveryConfigurationStore(paths);
        await deliveryConfigurationStore.ConfigureAsync(4, CancellationToken.None);
        var provider = new SequentialForecastProvider(
            [
                BuildForecast(weatherSummary: "weather 1"),
                BuildForecast(weatherSummary: "weather 2"),
                BuildForecast(weatherSummary: "weather 3"),
                BuildForecast(weatherSummary: "weather 4"),
                BuildForecast(weatherSummary: "weather 5"),
                BuildForecast(weatherSummary: "weather 6"),
            ]);
        var summarizer = new FakeSummarizer(["summary 1", "summary 2", "summary 3", "summary 4", "summary 5", "summary 6"]);
        var emailSender = new FakeEmailSender();
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 13, 18, 0, 0, TimeSpan.Zero));
        var service = new ForecastUpdateService(
            new ProviderRegistry([provider]),
            summarizer,
            emailSender,
            deliveryConfigurationStore,
            stateStore,
            clock,
            new ConsoleLog());

        for (var i = 0; i < 4; i++)
        {
            await service.ProcessAsync(DeliveryMode.Update, "user@inreach.garmin.com", "avalanche-canada", "Glacier", CancellationToken.None);
            clock.UtcNowValue = clock.UtcNowValue.AddHours(1);
        }

        await service.ProcessAsync(DeliveryMode.Update, "user@inreach.garmin.com", "avalanche-canada", "Glacier", CancellationToken.None);
        clock.UtcNowValue = clock.UtcNowValue.AddHours(21).AddMinutes(1);
        await service.ProcessAsync(DeliveryMode.Update, "user@inreach.garmin.com", "avalanche-canada", "Glacier", CancellationToken.None);

        Assert.Equal(["summary 1", "summary 2", "summary 3", "summary 4", "summary 6"], emailSender.SentBodies);
    }

    [Fact]
    public async Task Send_bypasses_daily_report_limit()
    {
        var paths = new AppPathsForTests();
        var stateStore = new DeliveryStateStore(paths);
        var deliveryConfigurationStore = new DeliveryConfigurationStore(paths);
        await deliveryConfigurationStore.ConfigureAsync(1, CancellationToken.None);
        await stateStore.UpsertRecipientAsync(
            new RecipientDeliveryStateRecord
            {
                InReachAddress = "user@inreach.garmin.com",
                SentReportsUtc = [new DateTimeOffset(2026, 3, 13, 17, 0, 0, TimeSpan.Zero)],
            },
            CancellationToken.None);
        var provider = new SequentialForecastProvider([BuildForecast()]);
        var summarizer = new FakeSummarizer(["manual summary"]);
        var emailSender = new FakeEmailSender();
        var clock = new FakeClock(new DateTimeOffset(2026, 3, 13, 18, 0, 0, TimeSpan.Zero));
        var service = new ForecastUpdateService(
            new ProviderRegistry([provider]),
            summarizer,
            emailSender,
            deliveryConfigurationStore,
            stateStore,
            clock,
            new ConsoleLog());

        await service.ProcessAsync(DeliveryMode.Send, "user@inreach.garmin.com", "avalanche-canada", "Glacier", CancellationToken.None);

        Assert.Equal(["manual summary"], emailSender.SentBodies);
    }

    private static AvalancheForecast BuildForecast(string weatherSummary = "weather summary") =>
        new(
            new ForecastRegion("avalanche-canada", "Glacier", "report-1", "area-1", "https://example.com"),
            "https://example.com",
            "Glacier",
            "Avalanche Canada",
            new DateTimeOffset(2026, 3, 13, 12, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 3, 13, 23, 0, 0, TimeSpan.Zero),
            "America/Vancouver",
            new DangerRatings(1, 2, 3),
            [new ForecastProblem("Storm slab", true, true, true, 3, 1, 2, ["n", "e"], "notes")],
            "highlights",
            "avalanche summary",
            "snowpack summary",
            weatherSummary,
            null);
}

internal sealed class SequentialForecastProvider(IReadOnlyList<AvalancheForecast> forecasts) : IAvalancheProvider
{
    public string Id => "avalanche-canada";

    private int _index;

    public IReadOnlyList<string> Aliases => ["avalanche-canada"];

    public Task<IReadOnlyList<ForecastRegion>> GetRegionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ForecastRegion>>(
            [new ForecastRegion(Id, "Glacier", "report-1", "area-1", "https://example.com")]);

    public Task<ForecastRegion?> ResolveRegionAsync(string regionName, CancellationToken cancellationToken) =>
        Task.FromResult<ForecastRegion?>(
            new ForecastRegion(Id, "Glacier", "report-1", "area-1", "https://example.com"));

    public Task<AvalancheForecast?> GetForecastAsync(ForecastRegion region, CancellationToken cancellationToken) =>
        Task.FromResult<AvalancheForecast?>(forecasts[Math.Min(_index++, forecasts.Count - 1)]);
}

internal sealed class MissingForecastProvider : IAvalancheProvider
{
    public string Id => "avalanche-canada";

    public IReadOnlyList<string> Aliases => ["avalanche-canada"];

    public Task<IReadOnlyList<ForecastRegion>> GetRegionsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ForecastRegion>>([]);

    public Task<ForecastRegion?> ResolveRegionAsync(string regionName, CancellationToken cancellationToken) =>
        Task.FromResult<ForecastRegion?>(null);

    public Task<AvalancheForecast?> GetForecastAsync(ForecastRegion region, CancellationToken cancellationToken) =>
        Task.FromResult<AvalancheForecast?>(null);
}

internal sealed class FakeSummarizer(IReadOnlyList<string> results) : IForecastSummarizer
{
    private int _index;

    public int CallCount => _index;

    public Task<string> GenerateSummaryAsync(AvalancheForecast forecast, CancellationToken cancellationToken)
    {
        var value = results[Math.Min(_index, results.Count - 1)];
        _index++;
        return Task.FromResult(value);
    }
}

internal sealed class FakeEmailSender : IEmailSender
{
    public List<string> SentBodies { get; } = [];

    public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken)
    {
        SentBodies.Add(body);
        return Task.CompletedTask;
    }
}

internal sealed class FakeClock(DateTimeOffset utcNow) : IClock
{
    public DateTimeOffset UtcNowValue { get; set; } = utcNow;

    public DateTimeOffset UtcNow => UtcNowValue;
}

internal sealed class AppPathsForTests : AppPaths
{
    public AppPathsForTests()
        : base(Path.Combine(Path.GetTempPath(), "AvyInReachTests", Guid.NewGuid().ToString("N")))
    {
    }

    public string TestRoot => RootDirectory;
}
