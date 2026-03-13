namespace AvyInReach.Tests;

public sealed class RecipientConfigurationValidatorTests
{
    [Fact]
    public async Task EnsureScheduledRecipientConfiguredAsync_throws_when_recipient_settings_are_missing()
    {
        var paths = new AppPathsForTests();
        var validator = new RecipientConfigurationValidator(
            new RecipientConfigurationStore(paths),
            new GarminConfigurationStore(paths));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.EnsureScheduledRecipientConfiguredAsync("user@example.com", CancellationToken.None));

        Assert.Contains("Recipient settings are not configured", ex.Message);
    }

    [Fact]
    public async Task EnsureScheduledRecipientConfiguredAsync_throws_for_garmin_when_reply_link_is_missing()
    {
        var paths = new AppPathsForTests();
        var recipientStore = new RecipientConfigurationStore(paths);
        await recipientStore.ConfigureAsync(
            "user@inreach.garmin.com",
            RecipientTransport.InReach,
            summaryCharacterBudget: null,
            CancellationToken.None);
        var validator = new RecipientConfigurationValidator(recipientStore, new GarminConfigurationStore(paths));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            validator.EnsureScheduledRecipientConfiguredAsync("user@inreach.garmin.com", CancellationToken.None));

        Assert.Contains("Garmin reply link is not configured", ex.Message);
    }

    [Fact]
    public async Task EnsureScheduledRecipientConfiguredAsync_passes_when_required_recipient_info_exists()
    {
        var paths = new AppPathsForTests();
        var recipientStore = new RecipientConfigurationStore(paths);
        var garminStore = new GarminConfigurationStore(paths);
        await recipientStore.ConfigureAsync(
            "user@inreach.garmin.com",
            RecipientTransport.InReach,
            summaryCharacterBudget: null,
            CancellationToken.None);
        await garminStore.ConfigureAsync(
            "user@inreach.garmin.com",
            new Uri("https://inreachlink.com/example"),
            3,
            CancellationToken.None);
        var validator = new RecipientConfigurationValidator(recipientStore, garminStore);

        await validator.EnsureScheduledRecipientConfiguredAsync("user@inreach.garmin.com", CancellationToken.None);
    }
}
