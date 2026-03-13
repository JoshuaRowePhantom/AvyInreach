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
}
