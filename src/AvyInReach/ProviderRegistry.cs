namespace AvyInReach;

internal sealed class ProviderRegistry
{
    private readonly IReadOnlyList<IAvalancheProvider> _providers;

    public ProviderRegistry(IReadOnlyList<IAvalancheProvider> providers)
    {
        _providers = providers;
    }

    public IReadOnlyList<IAvalancheProvider> GetSupportedProviders() => _providers;

    public IAvalancheProvider GetByName(string providerName)
    {
        var normalized = ForecastText.NormalizeKey(providerName);
        var provider = _providers.FirstOrDefault(candidate =>
            candidate.Aliases.Any(alias => ForecastText.NormalizeKey(alias) == normalized));

        if (provider is null)
        {
            var supportedProviders = string.Join(", ", _providers.Select(candidate => candidate.Id).OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
            throw new InvalidOperationException(
                $"Provider '{providerName}' is not supported. Supported providers: {supportedProviders}.");
        }

        return provider;
    }
}

internal interface IAvalancheProvider
{
    string Id { get; }

    IReadOnlyList<string> Aliases { get; }

    Task<IReadOnlyList<ForecastRegion>> GetRegionsAsync(CancellationToken cancellationToken);

    Task<ForecastRegion?> ResolveRegionAsync(string regionName, CancellationToken cancellationToken);

    Task<AvalancheForecast?> GetForecastAsync(ForecastRegion region, CancellationToken cancellationToken);
}
