namespace AvyInReach;

internal sealed class DeliveryConfigurationStore(AppPaths paths)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<DeliveryConfiguration> GetAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await JsonFileStore.AcquireLockAsync(paths.RootDirectory, cancellationToken);
            return File.Exists(paths.DeliveryConfigurationPath)
                ? await JsonFileStore.ReadAsync(paths.DeliveryConfigurationPath, new DeliveryConfiguration(), cancellationToken)
                : new DeliveryConfiguration();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ConfigureAsync(int maxReportsPer24Hours, CancellationToken cancellationToken)
    {
        if (maxReportsPer24Hours < 1)
        {
            throw new InvalidOperationException("Daily report limit must be at least 1.");
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await JsonFileStore.AcquireLockAsync(paths.RootDirectory, cancellationToken);
            var existing = File.Exists(paths.DeliveryConfigurationPath)
                ? await JsonFileStore.ReadAsync(paths.DeliveryConfigurationPath, new DeliveryConfiguration(), cancellationToken)
                : new DeliveryConfiguration();

            var updated = existing with
            {
                MaxReportsPer24Hours = maxReportsPer24Hours,
            };

            await JsonFileStore.WriteAsync(paths.DeliveryConfigurationPath, updated, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }
}

internal sealed record DeliveryConfiguration
{
    public int MaxReportsPer24Hours { get; init; } = 4;
}
