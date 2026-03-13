namespace AvyInReach;

internal sealed class SmtpConfigurationStore(AppPaths paths)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<SmtpConfiguration?> GetAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await JsonFileStore.AcquireLockAsync(paths.RootDirectory, cancellationToken);
            if (!File.Exists(paths.SmtpConfigurationPath))
            {
                return null;
            }

            return await JsonFileStore.ReadAsync(paths.SmtpConfigurationPath, new SmtpConfiguration(), cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<SmtpConfiguration> GetRequiredAsync(CancellationToken cancellationToken)
    {
        var configuration = await GetAsync(cancellationToken);
        if (configuration?.Server is null || string.IsNullOrWhiteSpace(configuration.FromAddress))
        {
            throw new InvalidOperationException(
                "SMTP is not configured. Run 'AvyInReach.exe smtp server <host:port> from <address>'.");
        }

        return configuration;
    }

    public async Task<string> GetRequiredFromAddressAsync(CancellationToken cancellationToken)
    {
        var configuration = await GetAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(configuration?.FromAddress))
        {
            throw new InvalidOperationException(
                "A sender address is not configured. Run 'AvyInReach.exe smtp server <host:port> from <address>'.");
        }

        return configuration.FromAddress;
    }

    public async Task ConfigureAsync(SmtpServer server, string fromAddress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fromAddress))
        {
            throw new InvalidOperationException("SMTP from address cannot be empty.");
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await JsonFileStore.AcquireLockAsync(paths.RootDirectory, cancellationToken);
            var existing = File.Exists(paths.SmtpConfigurationPath)
                ? await JsonFileStore.ReadAsync(paths.SmtpConfigurationPath, new SmtpConfiguration(), cancellationToken)
                : new SmtpConfiguration();

            var updated = existing with
            {
                Server = server,
                FromAddress = fromAddress.Trim(),
            };

            await JsonFileStore.WriteAsync(paths.SmtpConfigurationPath, updated, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }
}

internal sealed record SmtpConfiguration
{
    public SmtpServer? Server { get; init; }

    public string? FromAddress { get; init; }

    public bool EnableSsl { get; init; } = false;

    public bool UseDefaultCredentials { get; init; } = true;

    public string? Username { get; init; }

    public string? Password { get; init; }
}

internal sealed record SmtpServer(string Host, int Port)
{
    public string Value => $"{Host}:{Port}";
}
