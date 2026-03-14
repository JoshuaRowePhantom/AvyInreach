using System.Globalization;

namespace AvyInReach;

internal static class ScheduleLogReader
{
    public static async Task<string> ReadAsync(string logPath, CancellationToken cancellationToken)
    {
        var created = new DateTimeOffset(File.GetCreationTimeUtc(logPath), TimeSpan.Zero);
        var modified = new DateTimeOffset(File.GetLastWriteTimeUtc(logPath), TimeSpan.Zero);
        var body = await File.ReadAllTextAsync(logPath, cancellationToken);

        return string.Join(
            Environment.NewLine,
            [
                $"Log file: {logPath}",
                $"Created: {created.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}",
                $"Last modified: {modified.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)}",
                string.Empty,
                body,
            ]);
    }
}
