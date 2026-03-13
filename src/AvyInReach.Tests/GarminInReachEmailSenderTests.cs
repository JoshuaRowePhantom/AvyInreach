using System.Net;
using System.Text;
using System.Text.Json;

namespace AvyInReach.Tests;

public sealed class GarminInReachEmailSenderTests
{
    [Fact]
    public async Task SendAsync_fetches_reply_page_and_posts_standard_reply()
    {
        var handler = new GarminReplyHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://inreachlink.com"),
        };
        var paths = new AppPathsForTests();
        var smtpStore = new SmtpConfigurationStore(paths);
        var garminStore = new GarminConfigurationStore(paths);
        await smtpStore.ConfigureAsync(new SmtpServer("smtp.example.com", 25), "avyinreach@example.com", CancellationToken.None);
        await garminStore.ConfigureAsync("user@inreach.garmin.com", new Uri("https://inreachlink.com/example"), 3, CancellationToken.None);
        var sender = new GarminInReachEmailSender(httpClient, smtpStore, garminStore);

        await sender.SendAsync("user@inreach.garmin.com", "ignored", "forecast text", CancellationToken.None);

        Assert.Equal("https://inreachlink.com/example", handler.LastGetUri);
        Assert.Single(handler.PostUris);
        Assert.Equal("https://inreachlink.com/TextMessage/TxtMsg", handler.PostUris[0]);
        Assert.Single(handler.Payloads);
        var payload = handler.Payloads[0];
        Assert.Equal("avyinreach@example.com", payload.GetProperty("ReplyAddress").GetString());
        Assert.Equal("forecast text", payload.GetProperty("ReplyMessage").GetString());
        Assert.Equal("guid-123", payload.GetProperty("Guid").GetString());
        Assert.Equal("797077616", payload.GetProperty("MessageId").GetString());
    }

    [Fact]
    public async Task SendAsync_splits_across_multiple_messages_with_default_limit()
    {
        var handler = new GarminReplyHandler();
        using var httpClient = new HttpClient(handler);
        var paths = new AppPathsForTests();
        var smtpStore = new SmtpConfigurationStore(paths);
        var garminStore = new GarminConfigurationStore(paths);
        await smtpStore.ConfigureAsync(new SmtpServer("smtp.example.com", 25), "avyinreach@example.com", CancellationToken.None);
        await garminStore.ConfigureAsync("user@inreach.garmin.com", new Uri("https://inreachlink.com/example"), 3, CancellationToken.None);
        var sender = new GarminInReachEmailSender(httpClient, smtpStore, garminStore);

        await sender.SendAsync("user@inreach.garmin.com", "ignored", new string('x', 250), CancellationToken.None);

        Assert.Equal(2, handler.Payloads.Count);
        Assert.All(handler.Payloads, payload => Assert.True(payload.GetProperty("ReplyMessage").GetString()!.Length <= 160));
    }

    [Fact]
    public async Task SendAsync_throws_when_message_exceeds_configured_message_limit()
    {
        using var httpClient = new HttpClient(new GarminReplyHandler());
        var paths = new AppPathsForTests();
        var smtpStore = new SmtpConfigurationStore(paths);
        var garminStore = new GarminConfigurationStore(paths);
        await smtpStore.ConfigureAsync(new SmtpServer("smtp.example.com", 25), "avyinreach@example.com", CancellationToken.None);
        await garminStore.ConfigureAsync("user@inreach.garmin.com", new Uri("https://inreachlink.com/example"), 1, CancellationToken.None);
        var sender = new GarminInReachEmailSender(httpClient, smtpStore, garminStore);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sender.SendAsync("user@inreach.garmin.com", "ignored", new string('x', 161), CancellationToken.None));

        Assert.Contains("1 message", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class GarminReplyHandler : HttpMessageHandler
    {
        public string? LastGetUri { get; private set; }

        public List<string> PostUris { get; } = [];

        public List<JsonElement> Payloads { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                LastGetUri = request.RequestUri!.ToString();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    RequestMessage = request,
                    Content = new StringContent("""
                        <input id="MessageId" name="MessageId" type="hidden" value="797077616" />
                        <input id="Guid" name="Guid" type="hidden" value="guid-123" />
                        """, Encoding.UTF8, "text/html"),
                };
            }

            PostUris.Add(request.RequestUri!.ToString());
            var json = await request.Content!.ReadAsStringAsync(cancellationToken);
            Payloads.Add(JsonSerializer.Deserialize<JsonElement>(json));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
            };
        }
    }
}
