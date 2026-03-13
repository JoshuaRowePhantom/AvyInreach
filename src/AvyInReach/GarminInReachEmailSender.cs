using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AvyInReach;

internal sealed class RoutingEmailSender(IEmailSender smtpSender, IEmailSender garminSender) : IEmailSender
{
    public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken) =>
        IsGarminInReachAddress(toAddress)
            ? garminSender.SendAsync(toAddress, subject, body, cancellationToken)
            : smtpSender.SendAsync(toAddress, subject, body, cancellationToken);

    internal static bool IsGarminInReachAddress(string toAddress) =>
        toAddress.EndsWith("@inreach.garmin.com", StringComparison.OrdinalIgnoreCase);
}

internal sealed partial class GarminInReachEmailSender(
    HttpClient httpClient,
    SmtpConfigurationStore smtpConfigurationStore,
    GarminConfigurationStore garminConfigurationStore) : IEmailSender
{
    private const int MaxReplyLength = 160;

    public async Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken)
    {
        var replyAddress = await smtpConfigurationStore.GetRequiredFromAddressAsync(cancellationToken);
        var settings = await garminConfigurationStore.GetRequiredAsync(toAddress, cancellationToken);
        var messages = SplitMessages(body, settings.MaxMessages);

        using var replyPageResponse = await httpClient.GetAsync(settings.ReplyLink, cancellationToken);
        replyPageResponse.EnsureSuccessStatusCode();

        var html = await replyPageResponse.Content.ReadAsStringAsync(cancellationToken);
        var pageUri = replyPageResponse.RequestMessage?.RequestUri ?? settings.ReplyLink;
        var hiddenFields = ParseHiddenFields(html);
        var request = BuildReplyRequest(pageUri, hiddenFields, replyAddress);

        foreach (var message in messages)
        {
            using var content = new StringContent(request.BuildPayload(message), Encoding.UTF8, "application/json");
            using var response = await httpClient.PostAsync(request.Endpoint, content, cancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    private static GarminReplyRequest BuildReplyRequest(
        Uri pageUri,
        IReadOnlyDictionary<string, string> hiddenFields,
        string replyAddress)
    {
        if (hiddenFields.TryGetValue("ConversationId", out var conversationId)
            && hiddenFields.TryGetValue("MessengerAccountId", out var messengerAccountId)
            && hiddenFields.TryGetValue("SenderMessengerAccountId", out var senderMessengerAccountId))
        {
            var messageGuid = GetRequiredField(hiddenFields, "Guid");
            return new GarminReplyRequest(
                new Uri(pageUri, "/TextMessage/NonExploreUserTxtMsg"),
                replyMessage => JsonSerializer.Serialize(new
                {
                    ReplyAddress = replyAddress,
                    ReplyMessage = replyMessage,
                    MessageGuid = messageGuid,
                    ConversationId = conversationId,
                    MessengerAccountId = messengerAccountId,
                    SenderMessengerAccountId = senderMessengerAccountId,
                }));
        }

        var guid = GetRequiredField(hiddenFields, "Guid");
        var messageId = GetRequiredField(hiddenFields, "MessageId");
        return new GarminReplyRequest(
            new Uri(pageUri, "/TextMessage/TxtMsg"),
            replyMessage => JsonSerializer.Serialize(new
            {
                ReplyAddress = replyAddress,
                ReplyMessage = replyMessage,
                Guid = guid,
                MessageId = messageId,
            }));
    }

    private static IReadOnlyList<string> SplitMessages(string body, int maxMessages)
    {
        var normalized = body.Replace(Environment.NewLine, " ").Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new InvalidOperationException("Garmin reply body cannot be empty.");
        }

        var parts = new List<string>();
        var remaining = normalized;
        while (remaining.Length > 0)
        {
            if (parts.Count == maxMessages)
            {
                throw new InvalidOperationException(
                    $"Garmin replies are limited to {maxMessages} message(s) of {MaxReplyLength} characters each. Generated summary was {normalized.Length} characters.");
            }

            if (remaining.Length <= MaxReplyLength)
            {
                parts.Add(remaining);
                break;
            }

            var splitIndex = remaining.LastIndexOf(' ', MaxReplyLength);
            if (splitIndex <= 0)
            {
                splitIndex = MaxReplyLength;
            }

            parts.Add(remaining[..splitIndex].Trim());
            remaining = remaining[splitIndex..].TrimStart();
        }

        return parts;
    }

    private static string GetRequiredField(IReadOnlyDictionary<string, string> hiddenFields, string fieldName)
    {
        if (hiddenFields.TryGetValue(fieldName, out var value) && !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException($"Garmin reply page did not include required field '{fieldName}'.");
    }

    private static Dictionary<string, string> ParseHiddenFields(string html)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in HiddenInputRegex().Matches(html))
        {
            var id = match.Groups["id"].Value;
            var value = WebUtility.HtmlDecode(match.Groups["value"].Value);
            if (!string.IsNullOrWhiteSpace(id))
            {
                fields[id] = value;
            }
        }

        return fields;
    }

    [GeneratedRegex("<input[^>]*id=\"(?<id>[^\"]+)\"[^>]*value=\"(?<value>[^\"]*)\"[^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex HiddenInputRegex();

    private sealed record GarminReplyRequest(Uri Endpoint, Func<string, string> BuildPayload);
}
