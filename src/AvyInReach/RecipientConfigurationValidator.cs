namespace AvyInReach;

internal sealed class RecipientConfigurationValidator(
    RecipientConfigurationStore recipientConfigurationStore,
    GarminConfigurationStore garminConfigurationStore)
{
    public async Task EnsureScheduledRecipientConfiguredAsync(
        string recipientAddress,
        CancellationToken cancellationToken)
    {
        _ = await recipientConfigurationStore.GetRequiredAsync(recipientAddress, cancellationToken);

        if (RoutingEmailSender.IsGarminInReachAddress(recipientAddress))
        {
            _ = await garminConfigurationStore.GetRequiredAsync(recipientAddress, cancellationToken);
        }
    }
}
