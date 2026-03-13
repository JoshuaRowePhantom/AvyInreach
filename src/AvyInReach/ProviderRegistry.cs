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
        var normalized = Normalize(providerName);
        var provider = _providers.FirstOrDefault(candidate =>
            candidate.Aliases.Any(alias => Normalize(alias) == normalized));

        if (provider is null)
        {
            throw new InvalidOperationException(
                $"Provider '{providerName}' is not supported. Phase 1 supports only avalanche-canada.");
        }

        return provider;
    }

    private static string Normalize(string value) =>
        new string(value.Where(ch => char.IsLetterOrDigit(ch)).ToArray()).ToLowerInvariant();
}

internal interface IAvalancheProvider
{
    string Id { get; }

    IReadOnlyList<string> Aliases { get; }

    Task<IReadOnlyList<ForecastRegion>> GetRegionsAsync(CancellationToken cancellationToken);

    Task<ForecastRegion?> ResolveRegionAsync(string regionName, CancellationToken cancellationToken);

    Task<AvalancheForecast?> GetForecastAsync(ForecastRegion region, CancellationToken cancellationToken);
}
