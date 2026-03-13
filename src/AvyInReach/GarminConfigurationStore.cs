namespace AvyInReach;

internal sealed class GarminConfigurationStore(AppPaths paths)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<GarminRecipientSettings?> GetAsync(string inReachAddress, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await JsonFileStore.AcquireLockAsync(paths.RootDirectory, cancellationToken);
            if (!File.Exists(paths.GarminConfigurationPath))
            {
                return null;
            }

            var file = await JsonFileStore.ReadAsync(paths.GarminConfigurationPath, new GarminConfigurationFile(), cancellationToken);
            var entry = file.Entries.FirstOrDefault(item =>
                string.Equals(item.InReachAddress, inReachAddress, StringComparison.OrdinalIgnoreCase));

            return entry is null
                ? null
                : new GarminRecipientSettings(
                    new Uri(entry.ReplyLink, UriKind.Absolute),
                    entry.MaxMessages is > 0 ? entry.MaxMessages.Value : 3);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<GarminRecipientSettings> GetRequiredAsync(string inReachAddress, CancellationToken cancellationToken)
    {
        var settings = await GetAsync(inReachAddress, cancellationToken);
        if (settings is null)
        {
            throw new InvalidOperationException(
                $"Garmin reply link is not configured for '{inReachAddress}'. Run 'AvyInReach.exe garmin link <inreach> <reply-url>'.");
        }

        return settings;
    }

    public async Task ConfigureAsync(
        string inReachAddress,
        Uri replyLink,
        int maxMessages,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(inReachAddress))
        {
            throw new InvalidOperationException("Garmin InReach address cannot be empty.");
        }

        if (maxMessages < 1)
        {
            throw new InvalidOperationException("Garmin max messages must be at least 1.");
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await JsonFileStore.AcquireLockAsync(paths.RootDirectory, cancellationToken);
            var file = File.Exists(paths.GarminConfigurationPath)
                ? await JsonFileStore.ReadAsync(paths.GarminConfigurationPath, new GarminConfigurationFile(), cancellationToken)
                : new GarminConfigurationFile();

            var normalizedAddress = inReachAddress.Trim();
            file.Entries.RemoveAll(entry =>
                string.Equals(entry.InReachAddress, normalizedAddress, StringComparison.OrdinalIgnoreCase));
            file.Entries.Add(new GarminRecipientConfiguration(normalizedAddress, replyLink.AbsoluteUri, maxMessages));

            await JsonFileStore.WriteAsync(paths.GarminConfigurationPath, file, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }
}

internal sealed class GarminConfigurationFile
{
    public List<GarminRecipientConfiguration> Entries { get; init; } = [];
}

internal sealed record GarminRecipientConfiguration(string InReachAddress, string ReplyLink, int? MaxMessages = null);

internal sealed record GarminRecipientSettings(Uri ReplyLink, int MaxMessages);
