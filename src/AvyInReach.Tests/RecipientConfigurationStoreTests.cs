namespace AvyInReach.Tests;

public sealed class RecipientConfigurationStoreTests
{
    [Fact]
    public async Task ConfigureAsync_seeds_email_budget_by_default()
    {
        var paths = new AppPathsForTests();
        var store = new RecipientConfigurationStore(paths);

        var settings = await store.ConfigureAsync(
            "user@example.com",
            RecipientTransport.Email,
            summaryCharacterBudget: null,
            CancellationToken.None);

        Assert.Equal(1024, settings.SummaryCharacterBudget);
        Assert.Equal(RecipientTransport.Email, settings.Transport);
        Assert.True(File.Exists(paths.RecipientConfigurationPath));
    }

    [Fact]
    public async Task ConfigureAsync_seeds_inreach_budget_by_default()
    {
        var paths = new AppPathsForTests();
        var store = new RecipientConfigurationStore(paths);

        var settings = await store.ConfigureAsync(
            "user@inreach.garmin.com",
            RecipientTransport.InReach,
            summaryCharacterBudget: null,
            CancellationToken.None);

        Assert.Equal(480, settings.SummaryCharacterBudget);
        Assert.Equal(RecipientTransport.InReach, settings.Transport);
    }

    [Fact]
    public async Task ConfigureAsync_preserves_existing_budget_when_transport_is_unchanged()
    {
        var paths = new AppPathsForTests();
        var store = new RecipientConfigurationStore(paths);
        await store.ConfigureAsync("user@example.com", RecipientTransport.Email, 900, CancellationToken.None);

        var settings = await store.ConfigureAsync(
            "user@example.com",
            RecipientTransport.Email,
            summaryCharacterBudget: null,
            CancellationToken.None);

        Assert.Equal(900, settings.SummaryCharacterBudget);
    }
}
