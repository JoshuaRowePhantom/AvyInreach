using System.Runtime.Versioning;

namespace AvyInReach.Tests;

[SupportedOSPlatform("windows")]
public sealed class WindowsTaskSchedulerTests
{
    [Fact]
    public async Task RegisterAsync_uses_explicit_credentials()
    {
        var runner = new RecordingProcessRunner();
        var scheduler = new WindowsTaskScheduler(runner, new ConsoleLog());
        var credentials = new ScheduledTaskCredentials("DOMAIN\\jrowe", "secret-password");

        await scheduler.RegisterAsync(BuildRecord(), credentials, CancellationToken.None);

        Assert.Equal("schtasks.exe", runner.FileName);
        Assert.Contains("/RU", runner.Arguments);
        Assert.Contains("DOMAIN\\jrowe", runner.Arguments);
        Assert.Contains("/RP", runner.Arguments);
        Assert.Contains("secret-password", runner.Arguments);
    }

    private static ScheduleRecord BuildRecord() =>
        new()
        {
            Id = "test-id",
            Provider = "avalanche-canada",
            Region = "Glacier",
            InReachAddress = "user@example.com",
            StartDate = new DateOnly(2026, 3, 14),
            EndDate = new DateOnly(2026, 3, 22),
            WindowsTaskName = "AvyInReach-test",
            ExecutePath = @"C:\AvyInReach.exe",
            Arguments = "update user@example.com avalanche-canada Glacier",
            CreatedUtc = new DateTimeOffset(2026, 3, 13, 0, 0, 0, TimeSpan.Zero),
        };

    private sealed class RecordingProcessRunner : IProcessRunner
    {
        public string FileName { get; private set; } = string.Empty;

        public IReadOnlyList<string> Arguments { get; private set; } = [];

        public Task<ProcessRunResult> RunAsync(
            string fileName,
            IEnumerable<string> arguments,
            CancellationToken cancellationToken)
        {
            FileName = fileName;
            Arguments = arguments.ToList();
            return Task.FromResult(new ProcessRunResult(0, string.Empty, string.Empty));
        }
    }
}
