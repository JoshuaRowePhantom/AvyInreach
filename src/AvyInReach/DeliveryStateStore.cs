namespace AvyInReach;

internal sealed class DeliveryStateStore(AppPaths paths)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<DeliveryStateRecord> GetAsync(string inReachAddress, string provider, string region, CancellationToken cancellationToken)
        => await GetAsync(inReachAddress, provider, [region], cancellationToken);

    public async Task<DeliveryStateRecord> GetAsync(
        string inReachAddress,
        string provider,
        IEnumerable<string> regions,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await JsonFileStore.AcquireLockAsync(paths.RootDirectory, cancellationToken);
            var file = await LoadAsync(cancellationToken);
            var regionList = regions
                .Where(region => !string.IsNullOrWhiteSpace(region))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var match = file.Entries.FirstOrDefault(entry =>
                string.Equals(entry.InReachAddress, inReachAddress, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
                regionList.Any(region => string.Equals(entry.Region, region, StringComparison.OrdinalIgnoreCase)));

            return match
                   ?? new DeliveryStateRecord
                   {
                       InReachAddress = inReachAddress,
                       Provider = provider,
                        Region = regionList.FirstOrDefault() ?? string.Empty,
                    };
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpsertAsync(DeliveryStateRecord record, CancellationToken cancellationToken)
        => await UpsertAsync(record, [record.Region], cancellationToken);

    public async Task UpsertAsync(
        DeliveryStateRecord record,
        IEnumerable<string> regions,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await JsonFileStore.AcquireLockAsync(paths.RootDirectory, cancellationToken);
            var file = await LoadAsync(cancellationToken);
            var regionList = regions
                .Append(record.Region)
                .Where(region => !string.IsNullOrWhiteSpace(region))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            file.Entries.RemoveAll(entry =>
                string.Equals(entry.InReachAddress, record.InReachAddress, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Provider, record.Provider, StringComparison.OrdinalIgnoreCase) &&
                regionList.Any(region => string.Equals(entry.Region, region, StringComparison.OrdinalIgnoreCase)));

            file.Entries.Add(record);

            await JsonFileStore.WriteAsync(paths.DeliveryStatePath, file, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<RecipientDeliveryStateRecord> GetRecipientAsync(
        string inReachAddress,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await JsonFileStore.AcquireLockAsync(paths.RootDirectory, cancellationToken);
            var file = await LoadAsync(cancellationToken);
            var match = file.Recipients.FirstOrDefault(entry =>
                string.Equals(entry.InReachAddress, inReachAddress, StringComparison.OrdinalIgnoreCase));

            return match
                   ?? new RecipientDeliveryStateRecord
                   {
                       InReachAddress = inReachAddress,
                   };
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpsertRecipientAsync(
        RecipientDeliveryStateRecord record,
        CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await JsonFileStore.AcquireLockAsync(paths.RootDirectory, cancellationToken);
            var file = await LoadAsync(cancellationToken);
            file.Recipients.RemoveAll(entry =>
                string.Equals(entry.InReachAddress, record.InReachAddress, StringComparison.OrdinalIgnoreCase));
            file.Recipients.Add(record);
            await JsonFileStore.WriteAsync(paths.DeliveryStatePath, file, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private Task<DeliveryStateFile> LoadAsync(CancellationToken cancellationToken) =>
        JsonFileStore.ReadAsync(paths.DeliveryStatePath, new DeliveryStateFile(), cancellationToken);
}

internal sealed class DeliveryStateFile
{
    public List<DeliveryStateRecord> Entries { get; init; } = [];

    public List<RecipientDeliveryStateRecord> Recipients { get; init; } = [];
}

internal sealed class DeliveryStateRecord
{
    public string InReachAddress { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string? LastForecastFingerprint { get; set; }

    public string? LastSummaryFingerprint { get; set; }

    public string? LastSummary { get; set; }

    public DateTimeOffset? LastCheckedUtc { get; set; }

    public DateTimeOffset? LastSentUtc { get; set; }

    public DateTimeOffset? MissingForecastSinceUtc { get; set; }

    public bool MissingForecastNoticeSent { get; set; }

    public DateTimeOffset? ErrorSinceUtc { get; set; }

    public bool ErrorNoticeSent { get; set; }

    public string? LastError { get; set; }
}

internal sealed class RecipientDeliveryStateRecord
{
    public string InReachAddress { get; set; } = string.Empty;

    public List<DateTimeOffset> SentReportsUtc { get; set; } = [];
}
