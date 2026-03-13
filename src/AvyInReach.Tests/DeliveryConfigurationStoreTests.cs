namespace AvyInReach.Tests;

public sealed class DeliveryConfigurationStoreTests
{
    [Fact]
    public async Task GetAsync_defaults_report_limit_to_four()
    {
        var store = new DeliveryConfigurationStore(new AppPathsForTests());

        var configuration = await store.GetAsync(CancellationToken.None);

        Assert.Equal(4, configuration.MaxReportsPer24Hours);
    }

    [Fact]
    public async Task ConfigureAsync_writes_report_limit()
    {
        var paths = new AppPathsForTests();
        var store = new DeliveryConfigurationStore(paths);

        await store.ConfigureAsync(6, CancellationToken.None);

        var configuration = await store.GetAsync(CancellationToken.None);

        Assert.Equal(6, configuration.MaxReportsPer24Hours);
        Assert.True(File.Exists(paths.DeliveryConfigurationPath));
    }
}
