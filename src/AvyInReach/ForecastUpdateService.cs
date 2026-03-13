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
    DeliveryStateStore stateStore,
    IClock clock,
    ConsoleLog log)
{
    private static readonly TimeSpan RetryNoticeDelay = TimeSpan.FromHours(1);
    private static readonly JsonSerializerOptions FingerprintSerializerOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> GenerateSummaryAsync(
        string providerName,
        string regionName,
        CancellationToken cancellationToken)
    {
        var forecast = await GetForecastOrThrowAsync(providerName, regionName, cancellationToken);
        log.Info("Generating Copilot summary...");
        return await summarizer.GenerateSummaryAsync(forecast, cancellationToken);
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

            log.Info("Generating Copilot summary...");
            var summary = await summarizer.GenerateSummaryAsync(forecast, cancellationToken);
            state.LastForecastFingerprint = fingerprint;
            if (mode == DeliveryMode.Update
                && string.Equals(state.LastSummary, summary, StringComparison.Ordinal))
            {
                await stateStore.UpsertAsync(state, [regionName, forecast.Region.DisplayName], cancellationToken);
                log.Info("Summary unchanged; no update sent.");
                return;
            }

            log.Info("Sending summary...");
            await emailSender.SendAsync(
                inReachAddress,
                $"AvyInReach {forecast.Region.DisplayName}",
                summary,
                cancellationToken);

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
}
