using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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
    DeliveryConfigurationStore deliveryConfigurationStore,
    RecipientConfigurationStore recipientConfigurationStore,
    DeliveryStateStore stateStore,
    IClock clock,
    ConsoleLog log)
{
    private static readonly TimeSpan RetryNoticeDelay = TimeSpan.FromHours(1);
    private static readonly TimeSpan ReportWindow = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions FingerprintSerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> GenerateSummaryAsync(
        string recipientAddress,
        string providerName,
        string regionName,
        CancellationToken cancellationToken)
    {
        var forecast = await GetForecastOrThrowAsync(providerName, regionName, cancellationToken);
        var options = await GetSummaryGenerationOptionsAsync(recipientAddress, cancellationToken);
        log.Info("Generating Copilot summary...");
        return await summarizer.GenerateSummaryAsync(forecast, options, cancellationToken);
    }

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

            state = await stateStore.GetAsync(
                inReachAddress,
                provider.Id,
                [regionName, forecast.Region.DisplayName],
                cancellationToken);

            var fingerprint = ComputeForecastFingerprint(forecast);
            state.Region = regionName;
            state.LastCheckedUtc = clock.UtcNow;
            state.ErrorSinceUtc = null;
            state.ErrorNoticeSent = false;
            state.LastError = null;
            state.MissingForecastSinceUtc = null;
            state.MissingForecastNoticeSent = false;

            if (mode == DeliveryMode.Update
                && string.Equals(state.LastForecastFingerprint, fingerprint, StringComparison.Ordinal))
            {
                await stateStore.UpsertAsync(state, [regionName, forecast.Region.DisplayName], cancellationToken);
                log.Info("Forecast unchanged; no update sent.");
                return;
            }

            var options = await GetSummaryGenerationOptionsAsync(inReachAddress, cancellationToken);
            log.Info("Generating Copilot summary...");
            var summary = await summarizer.GenerateSummaryAsync(forecast, options, cancellationToken);
            var summaryFingerprint = ComputeTextFingerprint(summary);
            if (mode == DeliveryMode.Update
                && string.Equals(state.LastSummaryFingerprint, summaryFingerprint, StringComparison.Ordinal))
            {
                state.LastForecastFingerprint = fingerprint;
                state.LastSummaryFingerprint = summaryFingerprint;
                state.LastSummary = summary;
                await stateStore.UpsertAsync(state, [regionName, forecast.Region.DisplayName], cancellationToken);
                log.Info("Summary unchanged; no update sent.");
                return;
            }

            log.Info("Sending summary...");
            if (mode == DeliveryMode.Send)
            {
                await emailSender.SendAsync(
                    inReachAddress,
                    $"AvyInReach {forecast.Region.DisplayName}",
                    summary,
                    cancellationToken);
            }
            else
            {
                var sent = await TrySendReportAsync(
                    inReachAddress,
                    $"AvyInReach {forecast.Region.DisplayName}",
                    summary,
                    cancellationToken);
                if (!sent)
                {
                    await stateStore.UpsertAsync(state, [regionName, forecast.Region.DisplayName], cancellationToken);
                    return;
                }
            }

            state.LastForecastFingerprint = fingerprint;
            state.LastSummaryFingerprint = summaryFingerprint;
            state.LastSummary = summary;
            state.LastSentUtc = clock.UtcNow;
            await stateStore.UpsertAsync(state, [regionName, forecast.Region.DisplayName], cancellationToken);
            log.Info("Summary sent.");
            return;
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

    private async Task<SummaryGenerationOptions> GetSummaryGenerationOptionsAsync(
        string recipientAddress,
        CancellationToken cancellationToken)
    {
        var settings = await recipientConfigurationStore.GetRequiredAsync(recipientAddress, cancellationToken);
        return new SummaryGenerationOptions(
            settings.RecipientAddress,
            settings.Transport,
            settings.SummaryCharacterBudget);
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
            if (await TrySendReportAsync(inReachAddress, $"AvyInReach {regionName}", body, cancellationToken))
            {
                state.MissingForecastNoticeSent = true;
                log.Warn("Sent one-hour missing-forecast notice.");
            }
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
            if (await TrySendReportAsync(inReachAddress, $"AvyInReach {regionName}", body, cancellationToken))
            {
                state.ErrorNoticeSent = true;
                log.Warn("Sent one-hour persistent-error notice.");
            }
        }

        await stateStore.UpsertAsync(state, cancellationToken);
    }

    private async Task<bool> TrySendReportAsync(
        string inReachAddress,
        string subject,
        string body,
        CancellationToken cancellationToken)
    {
        var configuration = await deliveryConfigurationStore.GetAsync(cancellationToken);
        var recipientState = await stateStore.GetRecipientAsync(inReachAddress, cancellationToken);
        var now = clock.UtcNow;
        recipientState.SentReportsUtc = recipientState.SentReportsUtc
            .Where(sentAt => now - sentAt < ReportWindow)
            .OrderBy(sentAt => sentAt)
            .ToList();

        if (recipientState.SentReportsUtc.Count >= configuration.MaxReportsPer24Hours)
        {
            await stateStore.UpsertRecipientAsync(recipientState, cancellationToken);
            log.Warn($"24-hour report limit reached for '{inReachAddress}'; not sending.");
            return false;
        }

        await emailSender.SendAsync(inReachAddress, subject, body, cancellationToken);
        recipientState.SentReportsUtc.Add(now);
        await stateStore.UpsertRecipientAsync(recipientState, cancellationToken);
        return true;
    }

    private async Task<AvalancheForecast> GetForecastOrThrowAsync(
        string providerName,
        string regionName,
        CancellationToken cancellationToken)
    {
        var provider = providerRegistry.GetByName(providerName);
        log.Info($"Resolving region '{regionName}' from {provider.Id}...");
        var region = await provider.ResolveRegionAsync(regionName, cancellationToken);
        if (region is null)
        {
            throw new InvalidOperationException(
                $"Region '{regionName}' was not found for provider '{provider.Id}'.");
        }

        log.Info($"Fetching forecast for {region.DisplayName}...");
        var forecast = await provider.GetForecastAsync(region, cancellationToken);
        if (forecast is null)
        {
            throw new InvalidOperationException($"No forecast was published for '{region.DisplayName}'.");
        }

        return forecast;
    }

    private static string ComputeForecastFingerprint(AvalancheForecast forecast)
    {
        var payload = JsonSerializer.Serialize(
            new
            {
                Region = new
                {
                    forecast.Region.ProviderId,
                    forecast.Region.DisplayName,
                    forecast.Region.ReportId,
                    forecast.Region.AreaId,
                    forecast.Region.ForecastUrl,
                },
                forecast.ForecastUrl,
                forecast.Title,
                forecast.OwnerName,
                IssuedAt = forecast.IssuedAt.ToUniversalTime(),
                ValidUntil = forecast.ValidUntil.ToUniversalTime(),
                forecast.TimezoneId,
                DangerRatings = new
                {
                    forecast.CurrentDangerRatings.BelowTreeline,
                    forecast.CurrentDangerRatings.Treeline,
                    forecast.CurrentDangerRatings.Alpine,
                },
                Problems = forecast.Problems.Select(problem => new
                {
                    problem.Name,
                    problem.BelowTreeline,
                    problem.Treeline,
                    problem.Alpine,
                    problem.SizeMin,
                    problem.SizeMax,
                    Aspects = problem.Aspects.Select(AspectFormat.Normalize).ToArray(),
                    problem.Comment,
                }).ToArray(),
                forecast.Highlights,
                forecast.AvalancheSummary,
                forecast.SnowpackSummary,
                forecast.WeatherSummary,
                forecast.Message,
            },
            FingerprintSerializerOptions);

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash);
    }

    private static string ComputeTextFingerprint(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
        return Convert.ToHexString(hash);
    }
}
