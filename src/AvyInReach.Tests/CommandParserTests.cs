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
    public void Schedule_log_parses_id()
    {
        var command = Assert.IsType<ScheduleLogCommand>(CommandParser.Parse(
            ["schedule", "log", "20260314091500-abcd"]));

        Assert.Equal("20260314091500-abcd", command.Id);
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
    public void Copilot_model_command_parses_optional_model_id()
    {
        var command = Assert.IsType<CopilotModelCommand>(CommandParser.Parse(
            ["copilot", "model", "gpt-5-mini"]));

        Assert.Equal("gpt-5-mini", command.ModelId);
    }

    [Fact]
    public void Copilot_model_command_allows_reading_current_model()
    {
        var command = Assert.IsType<CopilotModelCommand>(CommandParser.Parse(
            ["copilot", "model"]));

        Assert.Null(command.ModelId);
    }

    [Fact]
    public void Smtp_command_parses_server_and_from()
    {
        var command = Assert.IsType<SmtpConfigureCommand>(CommandParser.Parse(
            ["smtp", "server", "smtp.example.com:25", "from", "avyinreach@example.com"]));

        Assert.Equal("smtp.example.com", command.Server.Host);
        Assert.Equal(25, command.Server.Port);
        Assert.Equal("avyinreach@example.com", command.FromAddress);
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
