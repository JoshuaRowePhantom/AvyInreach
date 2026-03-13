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
        var credentials = new ScheduledTaskCredentials("DOMAIN\\scheduler-user", "secret-password");

        await scheduler.RegisterAsync(BuildRecord(), credentials, CancellationToken.None);

        Assert.Equal("schtasks.exe", runner.FileName);
        Assert.Contains("/RU", runner.Arguments);
        Assert.Contains("DOMAIN\\scheduler-user", runner.Arguments);
        Assert.Contains("/RP", runner.Arguments);
        Assert.Contains("secret-password", runner.Arguments);
    }

    [Fact]
    public void ForCurrentProcessWithLog_wraps_invocation_in_cmd_redirection()
    {
        var invocation = ScheduledInvocation.ForCurrentProcessWithLog(
            @"C:\logs\schedule.log",
            "update",
            "user@example.com",
            "avalanche-canada",
            "South Rockies");

        Assert.Equal("cmd.exe", invocation.ExecutePath);
        Assert.Contains("schedule.log", invocation.Arguments);
        Assert.Contains("2>&1", invocation.Arguments);
        Assert.Contains("update", invocation.Arguments);
        Assert.Contains("user@example.com", invocation.Arguments);
        Assert.Contains("avalanche-canada", invocation.Arguments);
        Assert.Contains("South Rockies", invocation.Arguments);
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
