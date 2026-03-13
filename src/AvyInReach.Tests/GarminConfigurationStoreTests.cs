namespace AvyInReach.Tests;

public sealed class GarminConfigurationStoreTests
{
    [Fact]
    public async Task ConfigureAsync_writes_reply_link_for_inreach_address()
    {
        var paths = new AppPathsForTests();
        var store = new GarminConfigurationStore(paths);

        await store.ConfigureAsync(
            "user@inreach.garmin.com",
            new Uri("https://inreachlink.com/example"),
            4,
            CancellationToken.None);

        var settings = await store.GetRequiredAsync("user@inreach.garmin.com", CancellationToken.None);

        Assert.Equal(new Uri("https://inreachlink.com/example"), settings.ReplyLink);
        Assert.Equal(4, settings.MaxMessages);
        Assert.True(File.Exists(paths.GarminConfigurationPath));
    }

    [Fact]
    public async Task GetRequiredAsync_defaults_max_messages_to_three_for_legacy_entries()
    {
        var paths = new AppPathsForTests();
        await JsonFileStore.WriteAsync(
            paths.GarminConfigurationPath,
            new GarminConfigurationFile
            {
                Entries = [new GarminRecipientConfiguration("user@inreach.garmin.com", "https://inreachlink.com/example")],
            },
            CancellationToken.None);
        var store = new GarminConfigurationStore(paths);

        var settings = await store.GetRequiredAsync("user@inreach.garmin.com", CancellationToken.None);

        Assert.Equal(3, settings.MaxMessages);
    }
}
