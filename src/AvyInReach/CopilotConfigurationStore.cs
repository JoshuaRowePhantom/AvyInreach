namespace AvyInReach;

internal sealed class CopilotConfigurationStore(AppPaths paths)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<CopilotConfiguration> GetAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await JsonFileStore.AcquireLockAsync(paths.RootDirectory, cancellationToken);
            if (!File.Exists(paths.CopilotConfigurationPath))
            {
                return CopilotConfiguration.Default;
            }

            var configuration = await JsonFileStore.ReadAsync(paths.CopilotConfigurationPath, CopilotConfiguration.Default, cancellationToken);
            return string.IsNullOrWhiteSpace(configuration.Model)
                ? CopilotConfiguration.Default
                : configuration with { Model = configuration.Model.Trim() };
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<CopilotConfiguration> ConfigureAsync(string model, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("Copilot model cannot be empty.");
        }

        var updated = new CopilotConfiguration(model.Trim());

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await JsonFileStore.AcquireLockAsync(paths.RootDirectory, cancellationToken);
            await JsonFileStore.WriteAsync(paths.CopilotConfigurationPath, updated, cancellationToken);
            return updated;
        }
        finally
        {
            _lock.Release();
        }
    }
}

internal sealed record CopilotConfiguration(string Model)
{
    public static CopilotConfiguration Default { get; } = new("gpt-5-mini");
}
