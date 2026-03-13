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
            "garmin" => ParseGarmin(args),
            "smtp" => ParseSmtp(args),
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

    private static ParsedCommand ParseSmtp(string[] args)
    {
        if (args.Length != 5
            || !string.Equals(args[1], "server", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(args[3], "from", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliUsageException("Usage: AvyInReach.exe smtp server <host:port> from <address>");
        }

        return new SmtpConfigureCommand(ParseRequiredSmtpServer(args[2]), args[4].Trim());
    }

    private static ParsedCommand ParseGarmin(string[] args)
    {
        if ((args.Length != 4 && args.Length != 6)
            || !string.Equals(args[1], "link", StringComparison.OrdinalIgnoreCase))
        {
            throw new CliUsageException("Usage: AvyInReach.exe garmin link <inreach> <reply-url> [messages <count>]");
        }

        var maxMessages = 3;
        if (args.Length == 6)
        {
            if (!string.Equals(args[4], "messages", StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(args[5], out maxMessages)
                || maxMessages < 1)
            {
                throw new CliUsageException("Usage: AvyInReach.exe garmin link <inreach> <reply-url> [messages <count>]");
            }
        }

        return new GarminConfigureCommand(args[2].Trim(), ParseRequiredUri(args[3]), maxMessages);
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

    private static SmtpServer ParseRequiredSmtpServer(string value)
    {
        if (!TryParseSmtpServer(value, out var server))
        {
            throw new CliUsageException(
                $"Could not parse SMTP server '{value}'. Expected host:port with a port from 1 to 65535.");
        }

        return server;
    }

    private static bool TryParseSmtpServer(string value, out SmtpServer server)
    {
        server = null!;
        var separatorIndex = value.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex == value.Length - 1)
        {
            return false;
        }

        var host = value[..separatorIndex].Trim();
        var portValue = value[(separatorIndex + 1)..].Trim();
        if (string.IsNullOrWhiteSpace(host))
        {
            return false;
        }

        if (!int.TryParse(portValue, out var port) || port < 1 || port > 65535)
        {
            return false;
        }

        server = new SmtpServer(host, port);
        return true;
    }

    private static Uri ParseRequiredUri(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new CliUsageException($"Could not parse URL '{value}'. Expected an absolute http or https URL.");
        }

        return uri;
    }
}

internal abstract record ParsedCommand;

internal sealed record HelpCommand : ParsedCommand;

internal sealed record RegionsCommand(string? Provider) : ParsedCommand;

internal sealed record GarminConfigureCommand(string InReachAddress, Uri ReplyLink, int MaxMessages) : ParsedCommand;

internal sealed record SmtpConfigureCommand(SmtpServer Server, string FromAddress) : ParsedCommand;

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
          AvyInReach.exe garmin link <inreach> <reply-url> [messages <count>]
          AvyInReach.exe smtp server <host:port> from <address>
          AvyInReach.exe regions [provider]
          AvyInReach.exe summary <provider> <region>
          AvyInReach.exe send <inreach> <provider> <region>
          AvyInReach.exe update <inreach> <provider> <region>
          AvyInReach.exe schedule <start> <end> <inreach> <provider> <region>
          AvyInReach.exe schedules
          AvyInReach.exe unschedule <id>

        Examples:
          AvyInReach.exe garmin link somebody@inreach.garmin.com https://inreachlink.com/example
          AvyInReach.exe garmin link somebody@inreach.garmin.com https://inreachlink.com/example messages 3
          AvyInReach.exe smtp server smtp.example.com:25 from avyinreach@example.com
          AvyInReach.exe regions avalanche-canada
          AvyInReach.exe summary avalanche-canada Glacier
          AvyInReach.exe send somebody@inreach.garmin.com avalanche-canada Glacier
          AvyInReach.exe update somebody@inreach.garmin.com avalanche-canada "Coquihalla-Harrison-Fraser-Manning-Sasquatch-Skagit"
          AvyInReach.exe schedule 3/14 3/22 somebody@inreach.garmin.com avalanche-canada Glacier

        Notes:
          - Phase 1 supports only provider 'avalanche-canada'
          - inreach.garmin.com recipients require a configured Garmin reply link
          - Garmin replies are split into up to the configured number of 160-char messages (default 3)
          - summary prints the generated Copilot summary without sending email
          - update sends only when the final generated summary text changes
          - summaries always begin with 'valid to M/d HH:mmTZ'

        SMTP settings are stored in %LocalAppData%\AvyInReach\smtp.json.
        Garmin reply links are stored in %LocalAppData%\AvyInReach\garmin.json.
        The configure command writes server and from address there.
        JSON defaults remain enableSsl=false and useDefaultCredentials=true unless edited.
        """;
}
