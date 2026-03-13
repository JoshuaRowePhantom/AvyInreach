namespace AvyInReach;

internal enum DeliveryMode
{
    Send,
    Update,
}

internal sealed class ForecastUpdateService(
    ProviderRegistry providerRegistry,
    IForecastSummarizer summarizer,
    IEmailSender emailSender,
    DeliveryStateStore stateStore,
    IClock clock,
    ConsoleLog log)
{
    private static readonly TimeSpan RetryNoticeDelay = TimeSpan.FromHours(1);

    public async Task ProcessAsync(
        DeliveryMode mode,
        string inReachAddress,
        string providerName,
        string regionName,
        CancellationToken cancellationToken)
    {
        var provider = providerRegistry.GetByName(providerName);
        var state = await stateStore.GetAsync(inReachAddress, provider.Id, regionName, cancellationToken);

        try
        {
            log.Info($"Resolving region '{regionName}' from {provider.Id}...");
            var region = await provider.ResolveRegionAsync(regionName, cancellationToken);
            if (region is null)
            {
                if (mode == DeliveryMode.Send)
                {
                    throw new InvalidOperationException(
                        $"Region '{regionName}' was not found for provider '{provider.Id}'.");
                }

                await HandleMissingForecastAsync(state, inReachAddress, provider.Id, regionName, cancellationToken);
                return;
            }

            log.Info($"Fetching forecast for {region.DisplayName}...");
            var forecast = await provider.GetForecastAsync(region, cancellationToken);
            if (forecast is null)
            {
                if (mode == DeliveryMode.Send)
                {
                    throw new InvalidOperationException($"No forecast was published for '{region.DisplayName}'.");
                }

                await HandleMissingForecastAsync(state, inReachAddress, provider.Id, region.DisplayName, cancellationToken);
                return;
            }

            log.Info("Generating Copilot summary...");
            var summary = await summarizer.GenerateSummaryAsync(forecast, cancellationToken);
            state.Region = forecast.Region.DisplayName;
            state.LastCheckedUtc = clock.UtcNow;
            state.ErrorSinceUtc = null;
            state.ErrorNoticeSent = false;
            state.LastError = null;
            state.MissingForecastSinceUtc = null;
            state.MissingForecastNoticeSent = false;

            if (mode == DeliveryMode.Send || !string.Equals(state.LastSummary, summary, StringComparison.Ordinal))
            {
                log.Info("Sending summary...");
                await emailSender.SendAsync(
                    inReachAddress,
                    $"AvyInReach {forecast.Region.DisplayName}",
                    summary,
                    cancellationToken);

                state.LastSummary = summary;
                state.LastSentUtc = clock.UtcNow;
                await stateStore.UpsertAsync(state, cancellationToken);
                log.Info("Summary sent.");
                return;
            }

            await stateStore.UpsertAsync(state, cancellationToken);
            log.Info("Summary unchanged; no update sent.");
        }
        catch (Exception ex)
        {
            if (mode == DeliveryMode.Send)
            {
                throw;
            }

            log.Warn(ex.Message);
            await HandleErrorAsync(inReachAddress, provider.Id, regionName, ex, cancellationToken);
        }
    }

    private async Task HandleMissingForecastAsync(
        DeliveryStateRecord state,
        string inReachAddress,
        string provider,
        string regionName,
        CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        state.Region = regionName;
        state.LastCheckedUtc = now;
        state.ErrorSinceUtc = null;
        state.ErrorNoticeSent = false;
        state.LastError = null;
        state.MissingForecastSinceUtc ??= now;

        if (!state.MissingForecastNoticeSent && now - state.MissingForecastSinceUtc >= RetryNoticeDelay)
        {
            var body = $"No {provider} forecast published for {regionName} yet; still retrying.";
            await emailSender.SendAsync(inReachAddress, $"AvyInReach {regionName}", body, cancellationToken);
            state.MissingForecastNoticeSent = true;
            log.Warn("Sent one-hour missing-forecast notice.");
        }
        else
        {
            log.Warn("No forecast published yet.");
        }

        await stateStore.UpsertAsync(state, cancellationToken);
    }

    public async Task HandleErrorAsync(
        string inReachAddress,
        string provider,
        string regionName,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var state = await stateStore.GetAsync(inReachAddress, provider, regionName, cancellationToken);
        var now = clock.UtcNow;
        state.Region = regionName;
        state.LastCheckedUtc = now;
        state.LastError = exception.Message;
        state.ErrorSinceUtc ??= now;

        if (!state.ErrorNoticeSent && now - state.ErrorSinceUtc >= RetryNoticeDelay)
        {
            var body = $"Error checking {provider} forecast for {regionName}; still retrying.";
            await emailSender.SendAsync(inReachAddress, $"AvyInReach {regionName}", body, cancellationToken);
            state.ErrorNoticeSent = true;
            log.Warn("Sent one-hour persistent-error notice.");
        }

        await stateStore.UpsertAsync(state, cancellationToken);
    }
}
