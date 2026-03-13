namespace AvyInReach.Tests;

public sealed class RoutingEmailSenderTests
{
    [Fact]
    public async Task SendAsync_routes_garmin_addresses_to_garmin_sender()
    {
        var smtp = new RecordingSender();
        var garmin = new RecordingSender();
        var sender = new RoutingEmailSender(smtp, garmin);

        await sender.SendAsync("user@inreach.garmin.com", "subject", "body", CancellationToken.None);

        Assert.Equal(0, smtp.CallCount);
        Assert.Equal(1, garmin.CallCount);
    }

    [Fact]
    public async Task SendAsync_routes_other_addresses_to_smtp_sender()
    {
        var smtp = new RecordingSender();
        var garmin = new RecordingSender();
        var sender = new RoutingEmailSender(smtp, garmin);

        await sender.SendAsync("user@example.com", "subject", "body", CancellationToken.None);

        Assert.Equal(1, smtp.CallCount);
        Assert.Equal(0, garmin.CallCount);
    }

    private sealed class RecordingSender : IEmailSender
    {
        public int CallCount { get; private set; }

        public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.CompletedTask;
        }
    }
}
