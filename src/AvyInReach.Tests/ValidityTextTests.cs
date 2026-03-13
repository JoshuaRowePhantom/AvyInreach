namespace AvyInReach.Tests;

public sealed class ValidityTextTests
{
    [Fact]
    public void Formats_valid_until_with_timezone_abbreviation()
    {
        var text = ValidityText.Format(
            new DateTimeOffset(2026, 1, 22, 2, 0, 0, TimeSpan.Zero),
            "America/Vancouver");

        Assert.Equal("1/21 18:00PST", text);
    }
}
