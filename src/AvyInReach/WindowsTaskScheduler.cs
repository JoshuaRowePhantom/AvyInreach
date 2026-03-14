using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Xml.Linq;

namespace AvyInReach;

internal sealed class WindowsTaskScheduler(IProcessRunner processRunner, IAppLog log)
{
    [SupportedOSPlatform("windows")]
    public async Task RegisterAsync(
        ScheduleRecord record,
        ScheduledTaskCredentials credentials,
        CancellationToken cancellationToken)
    {
        EnsureWindows();
        var xml = BuildTaskXml(record, credentials.Username, "Password");
        var tempPath = Path.Combine(Path.GetTempPath(), $"{record.WindowsTaskName}.xml");

        await File.WriteAllTextAsync(tempPath, xml, Encoding.Unicode, cancellationToken);
        try
        {
            var arguments = new List<string>
            {
                "/Create",
                "/TN",
                record.WindowsTaskName,
                "/XML",
                tempPath,
                "/RU",
                credentials.Username,
                "/RP",
                credentials.Password,
            };
            arguments.Add("/F");

            var result = await processRunner.RunAsync(
                "schtasks.exe",
                arguments,
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
    private static string BuildTaskXml(ScheduleRecord record, string runAsUser, string logonType)
    {
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var startBoundary = record.StartDate.ToDateTime(TimeOnly.MinValue).ToString("yyyy-MM-ddTHH:mm:ss");
        var endBoundary = record.EndDate.ToDateTime(new TimeOnly(23, 59, 59)).ToString("yyyy-MM-ddTHH:mm:ss");

        var document = new XDocument(
            new XDeclaration("1.0", "UTF-16", null),
            new XElement(ns + "Task",
                new XAttribute("version", "1.4"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Description", "AvyInReach forecast update schedule")),
                new XElement(ns + "Triggers",
                    new XElement(ns + "CalendarTrigger",
                        new XElement(ns + "StartBoundary", startBoundary),
                        new XElement(ns + "EndBoundary", endBoundary),
                        new XElement(ns + "Enabled", "true"),
                        new XElement(ns + "ScheduleByDay",
                            new XElement(ns + "DaysInterval", "1")),
                        new XElement(ns + "Repetition",
                            new XElement(ns + "Interval", "PT15M"),
                            new XElement(ns + "Duration", "P1D"),
                            new XElement(ns + "StopAtDurationEnd", "false")))),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(ns + "UserId", runAsUser),
                        new XElement(ns + "LogonType", logonType),
                        new XElement(ns + "RunLevel", "LeastPrivilege"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(ns + "AllowHardTerminate", "true"),
                    new XElement(ns + "StartWhenAvailable", "true"),
                    new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(ns + "IdleSettings",
                        new XElement(ns + "StopOnIdleEnd", "false"),
                        new XElement(ns + "RestartOnIdle", "false")),
                    new XElement(ns + "AllowStartOnDemand", "true"),
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "Hidden", "false"),
                    new XElement(ns + "RunOnlyIfIdle", "false"),
                    new XElement(ns + "WakeToRun", "false"),
                    new XElement(ns + "ExecutionTimeLimit", "PT15M"),
                    new XElement(ns + "Priority", "7"),
                    new XElement(ns + "DeleteExpiredTaskAfter", "PT0S")),
                new XElement(ns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", record.ExecutePath),
                        new XElement(ns + "Arguments", record.Arguments)))));

        return document.ToString(SaveOptions.None);
    }
}

internal sealed record ScheduledTaskCredentials(string Username, string Password);

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

    public static ScheduledInvocation ForCurrentProcessWithLog(string logPath, params string[] args)
    {
        var baseInvocation = ForCurrentProcess(args);
        var command = $"{QuoteCmdCommandPart(baseInvocation.ExecutePath)} {baseInvocation.Arguments} > {QuoteCmdCommandPart(logPath)} 2>&1";
        return new ScheduledInvocation
        {
            ExecutePath = "cmd.exe",
            Arguments = $"/d /c {QuoteCmdCommandPart(command)}",
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

    private static string QuoteCmdCommandPart(string value) =>
        "\"" + value.Replace("\"", "\"\"") + "\"";
}
