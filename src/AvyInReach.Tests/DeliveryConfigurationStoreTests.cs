namespace AvyInReach.Tests;

public sealed class DeliveryConfigurationStoreTests
{
    [Fact]
    public async Task GetAsync_defaults_report_limit_to_one()
    {
        var store = new DeliveryConfigurationStore(new AppPathsForTests());

        var configuration = await store.GetAsync(CancellationToken.None);

        Assert.Equal(1, configuration.MaxReportsPerWindow);
    }

    [Fact]
    public async Task ConfigureAsync_writes_report_limit()
    {
        var paths = new AppPathsForTests();
        var store = new DeliveryConfigurationStore(paths);

        await store.ConfigureAsync(6, CancellationToken.None);

        var configuration = await store.GetAsync(CancellationToken.None);

        Assert.Equal(6, configuration.MaxReportsPerWindow);
        Assert.True(File.Exists(paths.DeliveryConfigurationPath));
    }
}
