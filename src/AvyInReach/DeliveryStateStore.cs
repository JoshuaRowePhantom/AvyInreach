namespace AvyInReach;

internal sealed class DeliveryStateStore(AppPaths paths)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<DeliveryStateRecord> GetAsync(string inReachAddress, string provider, string region, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var file = await LoadAsync(cancellationToken);
            return file.Entries.FirstOrDefault(entry =>
                       string.Equals(entry.InReachAddress, inReachAddress, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(entry.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
                       string.Equals(entry.Region, region, StringComparison.OrdinalIgnoreCase))
                   ?? new DeliveryStateRecord
                   {
                       InReachAddress = inReachAddress,
                       Provider = provider,
                       Region = region,
                   };
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpsertAsync(DeliveryStateRecord record, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            var file = await LoadAsync(cancellationToken);
            var existingIndex = file.Entries.FindIndex(entry =>
                string.Equals(entry.InReachAddress, record.InReachAddress, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Provider, record.Provider, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Region, record.Region, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                file.Entries[existingIndex] = record;
            }
            else
            {
                file.Entries.Add(record);
            }

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
}

internal sealed class DeliveryStateRecord
{
    public string InReachAddress { get; set; } = string.Empty;

    public string Provider { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string? LastSummary { get; set; }

    public DateTimeOffset? LastCheckedUtc { get; set; }

    public DateTimeOffset? LastSentUtc { get; set; }

    public DateTimeOffset? MissingForecastSinceUtc { get; set; }

    public bool MissingForecastNoticeSent { get; set; }

    public DateTimeOffset? ErrorSinceUtc { get; set; }

    public bool ErrorNoticeSent { get; set; }

    public string? LastError { get; set; }
}
