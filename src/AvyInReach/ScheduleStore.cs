namespace AvyInReach;

internal sealed class ScheduleStore(AppPaths paths)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<IReadOnlyList<ScheduleRecord>> ListAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await JsonFileStore.AcquireLockAsync(paths.RootDirectory, cancellationToken);
            var file = await LoadAsync(cancellationToken);
            return file.Entries;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<ScheduleRecord?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        var schedules = await ListAsync(cancellationToken);
        return schedules.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public async Task UpsertAsync(ScheduleRecord record, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await JsonFileStore.AcquireLockAsync(paths.RootDirectory, cancellationToken);
            var file = await LoadAsync(cancellationToken);
            var existingIndex = file.Entries.FindIndex(item =>
                string.Equals(item.Id, record.Id, StringComparison.OrdinalIgnoreCase));

            if (existingIndex >= 0)
            {
                file.Entries[existingIndex] = record;
            }
            else
            {
                file.Entries.Add(record);
            }

            await JsonFileStore.WriteAsync(paths.SchedulePath, file, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            await using var lease = await JsonFileStore.AcquireLockAsync(paths.RootDirectory, cancellationToken);
            var file = await LoadAsync(cancellationToken);
            file.Entries.RemoveAll(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase));
            await JsonFileStore.WriteAsync(paths.SchedulePath, file, cancellationToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private Task<ScheduleFile> LoadAsync(CancellationToken cancellationToken) =>
        JsonFileStore.ReadAsync(paths.SchedulePath, new ScheduleFile(), cancellationToken);
}

internal sealed class ScheduleFile
{
    public List<ScheduleRecord> Entries { get; init; } = [];
}

internal sealed class ScheduleRecord
{
    public string Id { get; init; } = string.Empty;

    public string Provider { get; init; } = string.Empty;

    public string Region { get; init; } = string.Empty;

    public string InReachAddress { get; init; } = string.Empty;

    public DateOnly StartDate { get; init; }

    public DateOnly EndDate { get; init; }

    public string WindowsTaskName { get; init; } = string.Empty;

    public string ExecutePath { get; init; } = string.Empty;

    public string Arguments { get; init; } = string.Empty;

    public DateTimeOffset CreatedUtc { get; init; }
}
