using System.Net;
using System.Net.Mail;

namespace AvyInReach;

internal interface IEmailSender
{
    Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken);
}

internal sealed class SmtpEmailSender : IEmailSender
{
    public Task SendAsync(string toAddress, string subject, string body, CancellationToken cancellationToken)
    {
        var settings = SmtpSettings.FromEnvironment();

        using var message = new MailMessage(settings.FromAddress, toAddress, subject, body);
        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.EnableSsl,
        };

        if (!string.IsNullOrWhiteSpace(settings.Username))
        {
            client.Credentials = new NetworkCredential(settings.Username, settings.Password);
        }

        return client.SendMailAsync(message, cancellationToken);
    }
}

internal sealed class SmtpSettings
{
    public required string Host { get; init; }

    public required int Port { get; init; }

    public required string FromAddress { get; init; }

    public required bool EnableSsl { get; init; }

    public string? Username { get; init; }

    public string? Password { get; init; }

    public static SmtpSettings FromEnvironment()
    {
        var host = Require("AVYINREACH_SMTP_HOST");
        var portValue = Require("AVYINREACH_SMTP_PORT");
        var from = Require("AVYINREACH_SMTP_FROM");
        var sslValue = Require("AVYINREACH_SMTP_ENABLE_SSL");

        if (!int.TryParse(portValue, out var port))
        {
            throw new InvalidOperationException("AVYINREACH_SMTP_PORT must be an integer.");
        }

        if (!bool.TryParse(sslValue, out var enableSsl))
        {
            throw new InvalidOperationException("AVYINREACH_SMTP_ENABLE_SSL must be true or false.");
        }

        return new SmtpSettings
        {
            Host = host,
            Port = port,
            FromAddress = from,
            EnableSsl = enableSsl,
            Username = Environment.GetEnvironmentVariable("AVYINREACH_SMTP_USERNAME"),
            Password = Environment.GetEnvironmentVariable("AVYINREACH_SMTP_PASSWORD"),
        };
    }

    private static string Require(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Missing environment variable {name}.");
}
