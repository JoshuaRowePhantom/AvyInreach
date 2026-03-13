namespace AvyInReach.Tests;

public sealed class CopilotConfigurationStoreTests
{
    [Fact]
    public async Task GetAsync_returns_default_model_when_missing()
    {
        var paths = new AppPathsForTests();
        var store = new CopilotConfigurationStore(paths);

        var configuration = await store.GetAsync(CancellationToken.None);

        Assert.Equal("gpt-5-mini", configuration.Model);
    }

    [Fact]
    public async Task ConfigureAsync_persists_selected_model()
    {
        var paths = new AppPathsForTests();
        var store = new CopilotConfigurationStore(paths);

        await store.ConfigureAsync("gpt-5.4", CancellationToken.None);
        var configuration = await store.GetAsync(CancellationToken.None);

        Assert.Equal("gpt-5.4", configuration.Model);
        Assert.True(File.Exists(paths.CopilotConfigurationPath));
    }
}
