namespace AvyInReach.Tests;

public sealed class ScheduleLogReaderTests
{
    [Fact]
    public async Task ReadAsync_includes_path_timestamps_and_body()
    {
        var paths = new AppPathsForTests();
        Directory.CreateDirectory(paths.ScheduleLogDirectory);
        var logPath = Path.Combine(paths.ScheduleLogDirectory, "test.log");
        await File.WriteAllTextAsync(logPath, "line 1\r\nline 2", CancellationToken.None);

        var createdUtc = new DateTime(2026, 3, 13, 18, 30, 0, DateTimeKind.Utc);
        var modifiedUtc = new DateTime(2026, 3, 13, 19, 45, 0, DateTimeKind.Utc);
        File.SetCreationTimeUtc(logPath, createdUtc);
        File.SetLastWriteTimeUtc(logPath, modifiedUtc);

        var text = await ScheduleLogReader.ReadAsync(logPath, CancellationToken.None);

        Assert.Contains($"Log file: {logPath}", text);
        Assert.Contains("Created: 2026-03-13 18:30:00 +00:00", text);
        Assert.Contains("Last modified: 2026-03-13 19:45:00 +00:00", text);
        Assert.Contains("line 1", text);
        Assert.Contains("line 2", text);
    }
}
