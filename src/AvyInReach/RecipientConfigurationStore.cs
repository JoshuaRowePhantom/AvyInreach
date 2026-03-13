namespace AvyInReach;

internal enum RecipientTransport
{
    Email,
    Sms,
    InReach,
}

internal sealed class RecipientConfigurationStore(AppPaths paths)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<RecipientSettings?> GetAsync(string recipientAddress, CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(paths.RecipientConfigurationPath))
            {
                return null;
            }

            var file = await JsonFileStore.ReadAsync(
                paths.RecipientConfigurationPath,
                new RecipientConfigurationFile(),
                cancellationToken);
            var entry = file.Entries.FirstOrDefault(item =>
                string.Equals(item.RecipientAddress, recipientAddress, StringComparison.OrdinalIgnoreCase));

            return entry is null ? null : entry.ToSettings();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<RecipientSettings> GetRequiredAsync(string recipientAddress, CancellationToken cancellationToken)
    {
        var settings = await GetAsync(recipientAddress, cancellationToken);
        if (settings is not null)
        {
            return settings;
        }

        throw new InvalidOperationException(
            $"Recipient settings are not configured for '{recipientAddress}'. Run 'AvyInReach.exe recipient configure <address> transport <email|sms|inreach> [summary <count>]' first.");
    }

    public async Task<RecipientSettings> ConfigureAsync(
        string recipientAddress,
        RecipientTransport transport,
        int? summaryCharacterBudget,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(recipientAddress))
        {
            throw new InvalidOperationException("Recipient address cannot be empty.");
        }

        if (summaryCharacterBudget is <= 0)
        {
            throw new InvalidOperationException("Recipient summary budget must be at least 1.");
        }

        await _lock.WaitAsync(cancellationToken);
        try
        {
            var file = File.Exists(paths.RecipientConfigurationPath)
                ? await JsonFileStore.ReadAsync(paths.RecipientConfigurationPath, new RecipientConfigurationFile(), cancellationToken)
                : new RecipientConfigurationFile();

            var normalizedAddress = recipientAddress.Trim();
            var existing = file.Entries.FirstOrDefault(entry =>
                string.Equals(entry.RecipientAddress, normalizedAddress, StringComparison.OrdinalIgnoreCase));
            var normalizedTransport = transport.ToConfigValue();
            var budget = ResolveSummaryBudget(existing, normalizedTransport, summaryCharacterBudget);

            file.Entries.RemoveAll(entry =>
                string.Equals(entry.RecipientAddress, normalizedAddress, StringComparison.OrdinalIgnoreCase));
            file.Entries.Add(new RecipientConfigurationEntry(normalizedAddress, normalizedTransport, budget));

            await JsonFileStore.WriteAsync(paths.RecipientConfigurationPath, file, cancellationToken);
            return new RecipientSettings(normalizedAddress, transport, budget);
        }
        finally
        {
            _lock.Release();
        }
    }

    internal static int GetSeedSummaryCharacterBudget(RecipientTransport transport) =>
        transport switch
        {
            RecipientTransport.Email => 1024,
            RecipientTransport.Sms => 420,
            RecipientTransport.InReach => 480,
            _ => throw new InvalidOperationException($"Unsupported recipient transport '{transport}'."),
        };

    private static int ResolveSummaryBudget(
        RecipientConfigurationEntry? existing,
        string normalizedTransport,
        int? requestedBudget)
    {
        if (requestedBudget is int explicitBudget)
        {
            return explicitBudget;
        }

        if (existing is not null && string.Equals(existing.Transport, normalizedTransport, StringComparison.OrdinalIgnoreCase))
        {
            return existing.SummaryCharacterBudget;
        }

        return GetSeedSummaryCharacterBudget(ParseTransport(normalizedTransport));
    }

    internal static RecipientTransport ParseTransport(string value) =>
        value.Trim().ToLowerInvariant() switch
        {
            "email" => RecipientTransport.Email,
            "sms" => RecipientTransport.Sms,
            "inreach" => RecipientTransport.InReach,
            _ => throw new InvalidOperationException(
                $"Unsupported recipient transport '{value}'. Expected email, sms, or inreach."),
        };
}

internal sealed class RecipientConfigurationFile
{
    public List<RecipientConfigurationEntry> Entries { get; init; } = [];
}

internal sealed record RecipientConfigurationEntry(string RecipientAddress, string Transport, int SummaryCharacterBudget)
{
    public RecipientSettings ToSettings() =>
        new(RecipientAddress, RecipientConfigurationStore.ParseTransport(Transport), SummaryCharacterBudget);
}

internal sealed record RecipientSettings(string RecipientAddress, RecipientTransport Transport, int SummaryCharacterBudget);

internal static class RecipientTransportExtensions
{
    public static string ToConfigValue(this RecipientTransport transport) =>
        transport switch
        {
            RecipientTransport.Email => "email",
            RecipientTransport.Sms => "sms",
            RecipientTransport.InReach => "inreach",
            _ => throw new InvalidOperationException($"Unsupported recipient transport '{transport}'."),
        };
}
