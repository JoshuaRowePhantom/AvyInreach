using System.Globalization;

namespace AvyInReach;

internal static class CommandParser
{
    public static ParsedCommand Parse(string[] args)
    {
        if (args.Length == 0)
        {
            return new HelpCommand();
        }

        var command = args[0].Trim().ToLowerInvariant();

        return command switch
        {
            "help" => new HelpCommand(),
            "regions" => ParseRegions(args),
            "summary" => ParseSummary(args),
            "send" => ParseSend(args),
            "update" => ParseUpdate(args),
            "schedule" => ParseSchedule(args),
            "schedules" => ParseSchedules(args),
            "unschedule" => ParseUnschedule(args),
            _ => throw new CliUsageException($"Unknown command '{args[0]}'."),
        };
    }

    private static ParsedCommand ParseRegions(string[] args)
    {
        if (args.Length > 2)
        {
            throw new CliUsageException("Usage: AvyInReach.exe regions [provider]");
        }

        return new RegionsCommand(args.Length == 2 ? args[1] : null);
    }

    private static ParsedCommand ParseSend(string[] args)
    {
        if (args.Length < 4)
        {
            throw new CliUsageException("Usage: AvyInReach.exe send <inreach> <provider> <region>");
        }

        return new SendCommand(args[1], args[2], JoinRegion(args, 3));
    }

    private static ParsedCommand ParseSummary(string[] args)
    {
        if (args.Length < 3)
        {
            throw new CliUsageException("Usage: AvyInReach.exe summary <provider> <region>");
        }

        return new SummaryCommand(args[1], JoinRegion(args, 2));
    }

    private static ParsedCommand ParseUpdate(string[] args)
    {
        if (args.Length < 4)
        {
            throw new CliUsageException("Usage: AvyInReach.exe update <inreach> <provider> <region>");
        }

        return new UpdateCommand(args[1], args[2], JoinRegion(args, 3));
    }

    private static ParsedCommand ParseSchedule(string[] args)
    {
        if (args.Length < 6)
        {
            throw new CliUsageException("Usage: AvyInReach.exe schedule <start> <end> <inreach> <provider> <region>");
        }

        var now = DateTimeOffset.Now;
        var startDate = ParseDate(args[1], now.Year);
        var endDate = ParseDate(args[2], now.Year);

        if (endDate < startDate && IsYearless(args[1]) && IsYearless(args[2]))
        {
            endDate = endDate.AddYears(1);
        }

        if (endDate < startDate)
        {
            throw new CliUsageException("End date must be on or after the start date.");
        }

        return new ScheduleCommand(
            startDate,
            endDate,
            args[3],
            args[4],
            JoinRegion(args, 5));
    }

    private static ParsedCommand ParseSchedules(string[] args)
    {
        if (args.Length != 1)
        {
            throw new CliUsageException("Usage: AvyInReach.exe schedules");
        }

        return new SchedulesCommand();
    }

    private static ParsedCommand ParseUnschedule(string[] args)
    {
        if (args.Length != 2)
        {
            throw new CliUsageException("Usage: AvyInReach.exe unschedule <id>");
        }

        return new UnscheduleCommand(args[1]);
    }

    private static string JoinRegion(string[] args, int startIndex) =>
        string.Join(' ', args[startIndex..]).Trim();

    private static DateOnly ParseDate(string value, int defaultYear)
    {
        if (DateOnly.TryParseExact(
            value,
            ["M/d/yyyy", "MM/dd/yyyy"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var fullDate))
        {
            return fullDate;
        }

        if (DateOnly.TryParseExact(
            $"{value}/{defaultYear}",
            ["M/d/yyyy", "MM/dd/yyyy"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var shortDate))
        {
            return shortDate;
        }

        throw new CliUsageException($"Could not parse date '{value}'. Expected M/d or M/d/yyyy.");
    }

    private static bool IsYearless(string value) => value.Count(ch => ch == '/') == 1;
}

internal abstract record ParsedCommand;

internal sealed record HelpCommand : ParsedCommand;

internal sealed record RegionsCommand(string? Provider) : ParsedCommand;

internal sealed record SummaryCommand(string Provider, string Region) : ParsedCommand;

internal sealed record SendCommand(string InReachAddress, string Provider, string Region) : ParsedCommand;

internal sealed record UpdateCommand(string InReachAddress, string Provider, string Region) : ParsedCommand;

internal sealed record ScheduleCommand(
    DateOnly StartDate,
    DateOnly EndDate,
    string InReachAddress,
    string Provider,
    string Region) : ParsedCommand;

internal sealed record SchedulesCommand : ParsedCommand;

internal sealed record UnscheduleCommand(string Id) : ParsedCommand;

internal sealed class CliUsageException(string message) : Exception(message);

internal static class CommandText
{
    public const string HelpText =
        """
        AvyInReach Phase 1 (Avalanche Canada only)

        Commands:
          AvyInReach.exe help
          AvyInReach.exe regions [provider]
          AvyInReach.exe summary <provider> <region>
          AvyInReach.exe send <inreach> <provider> <region>
          AvyInReach.exe update <inreach> <provider> <region>
          AvyInReach.exe schedule <start> <end> <inreach> <provider> <region>
          AvyInReach.exe schedules
          AvyInReach.exe unschedule <id>

        Examples:
          AvyInReach.exe regions avalanche-canada
          AvyInReach.exe summary avalanche-canada Glacier
          AvyInReach.exe send somebody@inreach.garmin.com avalanche-canada Glacier
          AvyInReach.exe update somebody@inreach.garmin.com avalanche-canada "Coquihalla-Harrison-Fraser-Manning-Sasquatch-Skagit"
          AvyInReach.exe schedule 3/14 3/22 somebody@inreach.garmin.com avalanche-canada Glacier

        Notes:
          - Phase 1 supports only provider 'avalanche-canada'
          - summary prints the generated Copilot summary without sending email
          - update sends only when the final generated summary text changes
          - summaries always include 'valid to M/d HH:mmTZ'

        Required environment variables for email:
          AVYINREACH_SMTP_HOST
          AVYINREACH_SMTP_PORT
          AVYINREACH_SMTP_FROM
          AVYINREACH_SMTP_ENABLE_SSL

        Optional SMTP environment variables:
          AVYINREACH_SMTP_USERNAME
          AVYINREACH_SMTP_PASSWORD
        """;
}
