using System.Reflection;
using System.Security.Principal;
using System.Runtime.Versioning;
using System.Text;
using System.Xml.Linq;

namespace AvyInReach;

internal sealed class WindowsTaskScheduler(ProcessRunner processRunner, ConsoleLog log)
{
    [SupportedOSPlatform("windows")]
    public async Task RegisterAsync(ScheduleRecord record, CancellationToken cancellationToken)
    {
        EnsureWindows();
        var xml = BuildTaskXml(record);
        var tempPath = Path.Combine(Path.GetTempPath(), $"{record.WindowsTaskName}.xml");

        await File.WriteAllTextAsync(tempPath, xml, Encoding.UTF8, cancellationToken);
        try
        {
            var result = await processRunner.RunAsync(
                "schtasks.exe",
                [
                    "/Create",
                    "/TN",
                    record.WindowsTaskName,
                    "/XML",
                    tempPath,
                    "/F",
                ],
                cancellationToken);

            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException($"Failed to register scheduled task: {result.CombinedOutput}");
            }
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    public async Task DeleteAsync(string taskName, CancellationToken cancellationToken)
    {
        EnsureWindows();
        var result = await processRunner.RunAsync(
            "schtasks.exe",
            [
                "/Delete",
                "/TN",
                taskName,
                "/F",
            ],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            log.Warn($"Deleting scheduled task '{taskName}' returned: {result.CombinedOutput}");
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Task Scheduler integration is supported only on Windows.");
        }
    }

    [SupportedOSPlatform("windows")]
    private static string BuildTaskXml(ScheduleRecord record)
    {
        var startBoundary = record.StartDate.ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-ddTHH:mm:ss");
        var endBoundary = record.EndDate.ToDateTime(new TimeOnly(23, 59, 59)).ToString("yyyy-MM-ddTHH:mm:ss");
        var userSid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Could not determine current Windows user SID.");

        var document = new XDocument(
            new XElement("Task",
                new XAttribute("version", "1.4"),
                new XAttribute("xmlns", "http://schemas.microsoft.com/windows/2004/02/mit/task"),
                new XElement("RegistrationInfo",
                    new XElement("Description", "AvyInReach forecast update schedule")),
                new XElement("Triggers",
                    new XElement("CalendarTrigger",
                        new XElement("StartBoundary", startBoundary),
                        new XElement("EndBoundary", endBoundary),
                        new XElement("Enabled", "true"),
                        new XElement("ScheduleByDay",
                            new XElement("DaysInterval", "1")),
                        new XElement("Repetition",
                            new XElement("Interval", "PT15M"),
                            new XElement("Duration", "P1D"),
                            new XElement("StopAtDurationEnd", "false")))),
                new XElement("Principals",
                    new XElement("Principal",
                        new XAttribute("id", "Author"),
                        new XElement("UserId", userSid),
                        new XElement("LogonType", "InteractiveToken"),
                        new XElement("RunLevel", "LeastPrivilege"))),
                new XElement("Settings",
                    new XElement("MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement("DisallowStartIfOnBatteries", "false"),
                    new XElement("StopIfGoingOnBatteries", "false"),
                    new XElement("AllowHardTerminate", "true"),
                    new XElement("StartWhenAvailable", "true"),
                    new XElement("RunOnlyIfNetworkAvailable", "false"),
                    new XElement("IdleSettings",
                        new XElement("StopOnIdleEnd", "false"),
                        new XElement("RestartOnIdle", "false")),
                    new XElement("AllowStartOnDemand", "true"),
                    new XElement("Enabled", "true"),
                    new XElement("Hidden", "false"),
                    new XElement("RunOnlyIfIdle", "false"),
                    new XElement("WakeToRun", "false"),
                    new XElement("ExecutionTimeLimit", "PT15M"),
                    new XElement("Priority", "7"),
                    new XElement("DeleteExpiredTaskAfter", "PT0S")),
                new XElement("Actions",
                    new XAttribute("Context", "Author"),
                    new XElement("Exec",
                        new XElement("Command", record.ExecutePath),
                        new XElement("Arguments", record.Arguments)))));

        return document.ToString();
    }
}

internal sealed class ScheduledInvocation
{
    public required string ExecutePath { get; init; }

    public required string Arguments { get; init; }

    public static ScheduledInvocation ForCurrentProcess(params string[] args)
    {
        var processPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not determine current process path.");

        var assemblyPath = Assembly.GetEntryAssembly()?.Location
            ?? throw new InvalidOperationException("Could not determine current assembly path.");

        if (Path.GetFileName(processPath).Equals("dotnet", StringComparison.OrdinalIgnoreCase) ||
            Path.GetFileName(processPath).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase))
        {
            return new ScheduledInvocation
            {
                ExecutePath = processPath,
                Arguments = BuildArguments([assemblyPath, .. args]),
            };
        }

        return new ScheduledInvocation
        {
            ExecutePath = processPath,
            Arguments = BuildArguments(args),
        };
    }

    private static string BuildArguments(IEnumerable<string> args) =>
        string.Join(" ", args.Select(QuoteWindowsArgument));

    private static string QuoteWindowsArgument(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "\"\"";
        }

        if (!value.Any(ch => char.IsWhiteSpace(ch) || ch == '"'))
        {
            return value;
        }

        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
