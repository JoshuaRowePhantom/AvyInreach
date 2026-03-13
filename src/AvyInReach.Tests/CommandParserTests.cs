namespace AvyInReach.Tests;

public sealed class CommandParserTests
{
    [Fact]
    public void Schedule_parses_multi_word_region_and_year_rollover()
    {
        var command = Assert.IsType<ScheduleCommand>(CommandParser.Parse(
            ["schedule", "12/30", "1/2", "user@inreach.garmin.com", "avalanche-canada", "South", "Rockies"]));

        Assert.Equal(new DateOnly(DateTimeOffset.Now.Year, 12, 30), command.StartDate);
        Assert.Equal(new DateOnly(DateTimeOffset.Now.Year + 1, 1, 2), command.EndDate);
        Assert.Equal("South Rockies", command.Region);
    }

    [Fact]
    public void Update_parses_multi_word_region()
    {
        var command = Assert.IsType<UpdateCommand>(CommandParser.Parse(
            ["update", "user@inreach.garmin.com", "avalanche-canada", "Coquihalla-Harrison-Fraser-Manning-Sasquatch-Skagit"]));

        Assert.Equal("Coquihalla-Harrison-Fraser-Manning-Sasquatch-Skagit", command.Region);
    }

    [Fact]
    public void Summary_parses_multi_word_region_without_inreach()
    {
        var command = Assert.IsType<PreviewCommand>(CommandParser.Parse(
            ["preview", "user@example.com", "avalanche-canada", "South", "Rockies"]));

        Assert.Equal("user@example.com", command.RecipientAddress);
        Assert.Equal("avalanche-canada", command.Provider);
        Assert.Equal("South Rockies", command.Region);
    }

    [Fact]
    public void Recipient_command_parses_transport_and_optional_summary_budget()
    {
        var command = Assert.IsType<RecipientConfigureCommand>(CommandParser.Parse(
            ["recipient", "configure", "user@example.com", "transport", "sms", "summary", "280"]));

        Assert.Equal("user@example.com", command.RecipientAddress);
        Assert.Equal(RecipientTransport.Sms, command.Transport);
        Assert.Equal(280, command.SummaryCharacterBudget);
    }

    [Fact]
    public void Smtp_command_parses_server_and_from()
    {
        var command = Assert.IsType<SmtpConfigureCommand>(CommandParser.Parse(
            ["smtp", "server", "undead.home.phantom.to:25", "from", "avyinreach@phantom.to"]));

        Assert.Equal("undead.home.phantom.to", command.Server.Host);
        Assert.Equal(25, command.Server.Port);
        Assert.Equal("avyinreach@phantom.to", command.FromAddress);
    }

    [Fact]
    public void Garmin_command_parses_inreach_and_reply_link()
    {
        var command = Assert.IsType<GarminConfigureCommand>(CommandParser.Parse(
            ["garmin", "link", "user@inreach.garmin.com", "https://inreachlink.com/example"]));

        Assert.Equal("user@inreach.garmin.com", command.InReachAddress);
        Assert.Equal(new Uri("https://inreachlink.com/example"), command.ReplyLink);
        Assert.Equal(3, command.MaxMessages);
    }

    [Fact]
    public void Garmin_command_parses_optional_message_count()
    {
        var command = Assert.IsType<GarminConfigureCommand>(CommandParser.Parse(
            ["garmin", "link", "user@inreach.garmin.com", "https://inreachlink.com/example", "messages", "4"]));

        Assert.Equal(4, command.MaxMessages);
    }

    [Fact]
    public void Delivery_command_parses_report_limit()
    {
        var command = Assert.IsType<DeliveryConfigureCommand>(CommandParser.Parse(
            ["delivery", "reports", "4"]));

        Assert.Equal(4, command.MaxReportsPer24Hours);
    }

}
